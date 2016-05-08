using UnityEngine;
using UnityEngine.Assertions;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if UNITY_EDITOR
using UnityEditor;
#endif

struct Particle
{
    public bool active;
    public Vector3 position;
    public Vector3 velocity;
    public Vector3 rotation;
    public Vector3 angVelocity;
    public Color color;
    public float scale;
    public float time;
    public float lifeTime;
}

public class PseudoInstancedGPUParticleManager : MonoBehaviour
{
    const int MAX_VERTEX_NUM = 65534;

    [SerializeField, Tooltip("This cannot be changed while running.")]
    int maxParticleNum;
    [SerializeField]
    Mesh mesh;
    [SerializeField]
    Shader shader;
    [SerializeField]
    ComputeShader computeShader;

    [SerializeField]
    Vector3 velocity = new Vector3(2f, 5f, 2f);
    [SerializeField]
    Vector3 angVelocity = new Vector3(45f, 45f, 45f);
    [SerializeField]
    Vector3 range = Vector3.one;
    [SerializeField]
    float scale = 0.2f;
    [SerializeField]
    float lifeTime = 2f;
    [SerializeField, Range(1, 100)]
    int emitGroupNum = 10;

    Mesh combinedMesh_;
    ComputeBuffer particlesBuffer_;
    ComputeBuffer particlePoolBuffer_;
    ComputeBuffer particleArgsBuffer_;
    int[] particleArgs_;
    int updateKernel_;
    int emitKernel_;
    Material material_;
    List<MaterialPropertyBlock> propertyBlocks_ = new List<MaterialPropertyBlock>();
    int particleNumPerMesh_;
    int meshNum_;

    Mesh CreateCombinedMesh(Mesh mesh, int num)
    {
        Assert.IsTrue(mesh.vertexCount * num <= MAX_VERTEX_NUM);

        var meshIndices = mesh.GetIndices(0);
        var indexNum = meshIndices.Length;

        var vertices = new List<Vector3>();
        var indices = new int[num * indexNum];
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var uv0 = new List<Vector2>();
        var uv1 = new List<Vector2>();

        for (int id = 0; id < num; ++id)
        {
            vertices.AddRange(mesh.vertices);
            normals.AddRange(mesh.normals);
            tangents.AddRange(mesh.tangents);
            uv0.AddRange(mesh.uv);

            // 各メッシュのインデックスは（1 つのモデルの頂点数 * ID）分ずらす
            for (int n = 0; n < indexNum; ++n)
            {
                indices[id * indexNum + n] = id * mesh.vertexCount + meshIndices[n];
            }

            // 2 番目の UV に ID を格納しておく
            for (int n = 0; n < mesh.uv.Length; ++n)
            {
                uv1.Add(new Vector2(id, id));
            }
        }

        var combinedMesh = new Mesh();
        combinedMesh.SetVertices(vertices);
        combinedMesh.SetIndices(indices, MeshTopology.Triangles, 0);
        combinedMesh.SetNormals(normals);
        combinedMesh.RecalculateNormals();
        combinedMesh.SetTangents(tangents);
        combinedMesh.SetUVs(0, uv0);
        combinedMesh.SetUVs(1, uv1);
        combinedMesh.RecalculateBounds();
        combinedMesh.bounds.SetMinMax(Vector3.one * -100f, Vector3.one * 100f);

        return combinedMesh;
    }

    float[] GetViewProjectionArray()
    {
        var camera = Camera.main;
        var view = camera.worldToCameraMatrix;
        var proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
        var vp = proj * view;
        return new float[] {
            vp.m00, vp.m10, vp.m20, vp.m30,
            vp.m01, vp.m11, vp.m21, vp.m31,
            vp.m02, vp.m12, vp.m22, vp.m32,
            vp.m03, vp.m13, vp.m23, vp.m33
        };
    }

    int GetParticlePoolSize()
    {
        particleArgsBuffer_.SetData(particleArgs_);
        ComputeBuffer.CopyCount(particlePoolBuffer_, particleArgsBuffer_, 0);
        particleArgsBuffer_.GetData(particleArgs_);
        return particleArgs_[0];
    }

    void OnEnable()
    {
        // メッシュの結合
        {
            particleNumPerMesh_ = MAX_VERTEX_NUM / mesh.vertexCount;
            meshNum_ = (int)Mathf.Ceil((float)maxParticleNum / particleNumPerMesh_);
            combinedMesh_ = CreateCombinedMesh(mesh, particleNumPerMesh_);
        }

        // 必要な数だけマテリアルを作成
        material_ = new Material(shader);
        for (int i = 0; i < meshNum_; ++i)
        {
            var props = new MaterialPropertyBlock();
            props.SetFloat("_IdOffset", particleNumPerMesh_ * i);
            propertyBlocks_.Add(props);
        }

        // ComputeBuffer の初期化
        {
            particlesBuffer_ = new ComputeBuffer(maxParticleNum, Marshal.SizeOf(typeof(Particle)), ComputeBufferType.Default);
            particlePoolBuffer_ = new ComputeBuffer(maxParticleNum, sizeof(int), ComputeBufferType.Append);
            particlePoolBuffer_.SetCounterValue(0);
            particleArgsBuffer_ = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
            particleArgs_ = new int[] { 0, 1, 0, 0 };
        }

        updateKernel_ = computeShader.FindKernel("Update");
        emitKernel_ = computeShader.FindKernel("Emit");

        DispatchInit();
    }

    void OnDisable()
    {
        particlesBuffer_.Release();
        particlePoolBuffer_.Release();
        particleArgsBuffer_.Release();
    }

    void Update()
    {
        if (Input.GetMouseButton(0)) {
            RaycastHit hit;
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out hit)) {
                var toNormal = Quaternion.FromToRotation(Vector3.up, hit.normal);
                computeShader.SetVector("_Position", hit.point + hit.normal * 0.1f);
                computeShader.SetVector("_Velocity", toNormal * velocity);
                DispatchEmit(emitGroupNum);
            }
        }
        DispatchUpdate();
        RegisterDraw(Camera.main);
#if UNITY_EDITOR
        if (SceneView.lastActiveSceneView) {
            RegisterDraw(SceneView.lastActiveSceneView.camera);
        }
#endif
    }

    void DispatchInit()
    {
        var initKernel = computeShader.FindKernel("Init");
        computeShader.SetBuffer(initKernel, "_Particles", particlesBuffer_);
        computeShader.SetBuffer(initKernel, "_DeadList", particlePoolBuffer_);
        computeShader.Dispatch(initKernel, maxParticleNum / 8, 1, 1);
    }

    void DispatchEmit(int groupNum)
    {
        var camera = Camera.main;

        computeShader.SetBuffer(emitKernel_, "_Particles", particlesBuffer_);
        computeShader.SetBuffer(emitKernel_, "_ParticlePool", particlePoolBuffer_);
        computeShader.SetVector("_AngVelocity", angVelocity * Mathf.Deg2Rad);
        computeShader.SetVector("_Range", range);
        computeShader.SetFloat("_Scale", scale);
        computeShader.SetFloat("_DeltaTime", Time.deltaTime);
        computeShader.SetFloat("_ScreenWidth", camera.pixelWidth);
        computeShader.SetFloat("_ScreenHeight", camera.pixelHeight);
        computeShader.SetFloat("_LifeTime", lifeTime);
        computeShader.Dispatch(emitKernel_, Mathf.Min(groupNum, GetParticlePoolSize() / 8), 1, 1);
    }

    void DispatchUpdate()
    {
        computeShader.SetFloats("_ViewProj", GetViewProjectionArray());
        computeShader.SetTexture(updateKernel_, "_CameraDepthTexture", GBufferUtils.GetDepthTexture());
        computeShader.SetTexture(updateKernel_, "_CameraGBufferTexture2", GBufferUtils.GetGBufferTexture(2));
        computeShader.SetBuffer(updateKernel_, "_Particles", particlesBuffer_);
        computeShader.SetBuffer(updateKernel_, "_DeadList", particlePoolBuffer_);
        computeShader.Dispatch(updateKernel_, maxParticleNum / 8, 1, 1);
    }

    void RegisterDraw(Camera camera)
    {
        material_.SetBuffer("_Particles", particlesBuffer_);
        for (int i = 0; i < meshNum_; ++i) {
            var props = propertyBlocks_[i];
            props.SetFloat("_IdOffset", particleNumPerMesh_ * i);
            Graphics.DrawMesh(combinedMesh_, transform.position, transform.rotation, material_, 0, camera, 0, props);
        }
    }
}
