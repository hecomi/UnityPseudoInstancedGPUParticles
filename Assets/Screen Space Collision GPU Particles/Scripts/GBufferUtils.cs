using UnityEngine;
using UnityEngine.Assertions;
using System.Collections;

public class GBufferUtils : MonoBehaviour
{
    static GBufferUtils Instance;

    [SerializeField]
    Shader gbufferCopyShader;
    Material gbufferCopyMaterial_;

    Mesh quad_;
    RenderTexture depthTexture_;
    RenderTexture[] gbufferTextures_ = new RenderTexture[4];

    static new Camera camera
    {
        get { return Camera.main; }
    }

    static public GBufferUtils GetInstance()
    {
        Assert.IsTrue(Instance != null, "At least one GBufferUtils must be attached to a camera and be set as active.");
        return Instance;
    }

    static public RenderTexture GetDepthTexture()
    {
        return GetInstance().depthTexture_;
    }

    static public RenderTexture GetGBufferTexture(int index)
    {
        Assert.IsTrue(index >= 0 && index < 4);
        return GetInstance().gbufferTextures_[index];
    }

    Mesh CreateQuad()
    {
        var mesh = new Mesh();
        mesh.name = "Quad";
        mesh.vertices = new Vector3[4] {
            new Vector3( 1f, 1f, 0f),
            new Vector3(-1f, 1f, 0f),
            new Vector3(-1f,-1f, 0f),
            new Vector3( 1f,-1f, 0f),
        };
        mesh.triangles = new int[6] {
            0, 1, 2,
            2, 3, 0
        };
        return mesh;
    }

    RenderTexture CreateRenderTexture(RenderTextureFormat format, int depth)
    {
        var texture = new RenderTexture(camera.pixelWidth, camera.pixelHeight, depth, format);
        texture.filterMode = FilterMode.Point;
        texture.useMipMap = false;
        texture.generateMips = false;
        texture.enableRandomWrite = false;
        texture.Create();
        return texture;
    }

    void Start()
    {
        quad_ = CreateQuad();
        gbufferCopyMaterial_ = new Material(gbufferCopyShader);
    }

    void OnEnable()
    {
        Assert.IsTrue(Instance == null, "Multiple GBUfferUtils are set as active at the same time.");
        Instance = this;
        UpdateRenderTextures();
    }

    void OnDisable()
    {
        Instance = null;

        if (depthTexture_ != null) {
            depthTexture_.Release();
            depthTexture_ = null;
        }

        for (int i = 0; i < 4; ++i) {
            if (gbufferTextures_[i] != null) {
                gbufferTextures_[i].Release();
                gbufferTextures_[i] = null;
            }
        }
    }

    IEnumerator OnPostRender()
    {
        yield return new WaitForEndOfFrame();
        UpdateRenderTextures();
        UpdateGBuffer();
    }

    void UpdateRenderTextures()
    {
        if (depthTexture_ == null || 
            depthTexture_.width != camera.pixelWidth || 
            depthTexture_.height != camera.pixelHeight)
        {
            if (depthTexture_ != null) depthTexture_.Release();
            depthTexture_ = CreateRenderTexture(RenderTextureFormat.Depth, 24);
        }

        for (int i = 0; i < 4; ++i) {
            if (gbufferTextures_[i] == null ||
                gbufferTextures_[i].width != camera.pixelWidth ||
                gbufferTextures_[i].height != camera.pixelHeight)
            {
                if (gbufferTextures_[i] != null) gbufferTextures_[i].Release();
                gbufferTextures_[i] = CreateRenderTexture(RenderTextureFormat.ARGB32, 0);
            }
        }
    }

    void UpdateGBuffer()
    {
        var gbuffers = new RenderBuffer[4];
        for (int i = 0; i < 4; ++i) {
            gbuffers[i] = gbufferTextures_[i].colorBuffer;
        }

        gbufferCopyMaterial_.SetPass(0);
        Graphics.SetRenderTarget(gbuffers, depthTexture_.depthBuffer);
        Graphics.DrawMeshNow(quad_, Matrix4x4.identity);
        Graphics.SetRenderTarget(null);
    }
}
