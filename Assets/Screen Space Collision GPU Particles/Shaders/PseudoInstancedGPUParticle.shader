Shader "GPUParticle/PseudoInstancedGPUParticle"
{

SubShader 
{

Tags { "RenderType" = "Opaque" }

CGINCLUDE

#include "UnityCG.cginc"
#include "UnityStandardShadow.cginc"

struct Particle
{
    bool active;
    float3 position;
    float3 velocity;
    float3 rotation;
    float3 angVelocity;
    float4 color;
    float scale;
    float time;
    float lifeTime;
};

#ifdef SHADER_API_D3D11
StructuredBuffer<Particle> _Particles;
#endif
float _IdOffset;

struct appdata
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float2 uv1 : TEXCOORD1;
};

struct v2f
{
    float4 position : SV_POSITION;
    float3 normal : NORMAL;
    float2 uv1 : TEXCOORD1;
};

struct v2f_shadow
{
    V2F_SHADOW_CASTER;
};

struct gbuffer_out
{
    float4 diffuse  : SV_Target0; // rgb: diffuse,  a: occlusion
    float4 specular : SV_Target1; // rgb: specular, a: smoothness
    float4 normal   : SV_Target2; // rgb: normal,   a: unused
    float4 emission : SV_Target3; // rgb: emission, a: unused
    float  depth    : SV_Depth;
};

inline int getId(float2 uv1)
{
    return (int)(uv1.x + 0.5) + (int)_IdOffset;
}

float3 rotate(float3 p, float3 rotation)
{
    float3 a = normalize(rotation);
    float angle = length(rotation);
    if (abs(angle) < 0.001) return p;
    float s = sin(angle);
    float c = cos(angle);
    float r = 1.0 - c;
    float3x3 m = float3x3(
        a.x * a.x * r + c,
        a.y * a.x * r + a.z * s,
        a.z * a.x * r - a.y * s,
        a.x * a.y * r - a.z * s,
        a.y * a.y * r + c,
        a.z * a.y * r + a.x * s,
        a.x * a.z * r + a.y * s,
        a.y * a.z * r - a.x * s,
        a.z * a.z * r + c
    );
    return mul(m, p);
}

v2f vert(appdata v)
{
#ifdef SHADER_API_D3D11
    Particle p = _Particles[getId(v.uv1)];
    v.vertex.xyz *= p.scale;
    v.vertex.xyz = rotate(v.vertex.xyz, p.rotation);
    v.vertex.xyz += p.position;
    v.normal = rotate(v.normal, p.rotation);
#endif
    v2f o;
    o.uv1 = v.uv1;
    o.position = mul(UNITY_MATRIX_VP, v.vertex);
    o.normal = v.normal;
    return o;
}

gbuffer_out frag(v2f i) : SV_Target
{
    gbuffer_out o;
    o.diffuse = 0;
    o.normal = float4(0.5 * i.normal + 0.5, 1);
    o.emission = o.diffuse * 0.1;
    o.specular = 0;
    o.depth = i.position;

#ifdef SHADER_API_D3D11
    Particle p;
    p = _Particles[getId(i.uv1)];
    o.diffuse = p.color;
#endif

    return o;
}

v2f_shadow vert_shadow(appdata v)
{
#ifdef SHADER_API_D3D11
    Particle p = _Particles[getId(v.uv1)];
    v.vertex.xyz = rotate(v.vertex.xyz, p.rotation);
    v.vertex.xyz *= p.scale;
    v.vertex.xyz += p.position;
#endif
    v2f_shadow o;
    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
    o.pos = mul(UNITY_MATRIX_VP, v.vertex);
    return o;
}

float4 frag_shadow(v2f_shadow i) : SV_Target
{
    SHADOW_CASTER_FRAGMENT(i)
}

ENDCG

Pass
{
    Tags { "LightMode" = "Deferred" }
    ZWrite On

    CGPROGRAM
    #pragma target 3.0
    #pragma vertex vert 
    #pragma fragment frag 
    ENDCG
}

Pass
{
    Tags { "LightMode" = "ShadowCaster" }
    Fog { Mode Off }
    ZWrite On 
    ZTest LEqual
    Cull Off
    Offset 1, 1

    CGPROGRAM
    #pragma target 3.0
    #pragma vertex vert_shadow
    #pragma fragment frag_shadow
    #pragma multi_compile_shadowcaster
    #pragma fragmentoption ARB_precision_hint_fastest
    ENDCG
}

} 

FallBack "Diffuse"

}