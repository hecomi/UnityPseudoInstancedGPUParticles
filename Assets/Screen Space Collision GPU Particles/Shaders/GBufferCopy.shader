Shader "Hidden/GBufferCopy" 
{

CGINCLUDE

#include "UnityCG.cginc"

sampler2D _CameraGBufferTexture0; // rgb: diffuse,  a: occlusion
sampler2D _CameraGBufferTexture1; // rgb: specular, a: smoothness
sampler2D _CameraGBufferTexture2; // rgb: normal,   a: unused
sampler2D _CameraGBufferTexture3; // rgb: emission, a: unused
sampler2D_float _CameraDepthTexture;

struct appdata
{
    float4 vertex : POSITION;
};

struct v2f
{
    float4 vertex    : SV_POSITION;
    float4 screenPos : TEXCOORD0;
};

struct gbuffer_out
{
    float4 diffuse  : SV_Target0; // rgb: diffuse,  a: occlusion
    float4 specular : SV_Target1; // rgb: specular, a: smoothness
    float4 normal   : SV_Target2; // rgb: normal,   a: unused
    float4 emission : SV_Target3; // rgb: emission, a: unused
    float  depth    : SV_Depth;
};

v2f vert(appdata v)
{
    v2f o;
    o.vertex = v.vertex;
    o.screenPos = v.vertex;
#if UNITY_UV_STARTS_AT_TOP
    o.screenPos.y *= -1.0;
#endif
    return o;
}

gbuffer_out frag(v2f v)
{
    float2 uv = (v.screenPos * 0.5 + 0.5);

    gbuffer_out o;
    o.diffuse  = tex2D(_CameraGBufferTexture0, uv);
    o.specular = tex2D(_CameraGBufferTexture1, uv);
    o.normal   = tex2D(_CameraGBufferTexture2, uv);
    o.emission = tex2D(_CameraGBufferTexture3, uv);
    o.depth    = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
    return o;
}

ENDCG

SubShader
{
    Tags { "RenderType" = "Opaque" }
    Blend Off
    ZTest Always
    ZWrite On
    Cull Off

    Pass 
    {
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        ENDCG
    }
}

}