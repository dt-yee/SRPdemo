#ifndef MYRP_LIT_INCLUDE
#define MYRP_LIT_INCLUDE

#define MAX_VISIBLE_LIGHTS 4

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
CBUFFER_START(UnityPerDraw)
float4x4 unity_ObjectToWorld;
CBUFFER_END

CBUFFER_START(UnityPerFrame)
float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(_LightBuffer)
    float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END

//CBUFFER_START(UnityPerMaterial)
//float4 _Color;
//CBUFFER_END

#define UNITY_MATRIX_M unity_ObjectToWorld
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
UNITY_INSTANCING_BUFFER_START(PerInstance)
UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)


float3 DiffuseLight(int index, float3 normal, float3 worldPos)
{
    float3 lightColor = _VisibleLightColors[index].rgb;
    float4 lightData = _VisibleLightDirectionsOrPositions[index];
    float3 lightVector = lightData.xzy - worldPos * lightData.w;
    float4 spotDirection = _VisibleLightSpotDirections[index];

    float3 lightDir = normalize(lightVector);
    float diffuse = saturate(dot(normal, lightDir));

    float4 lightAtt = _VisibleLightAttenuations[index];

    float dis = dot(lightDir, lightDir);

    float rangeFade = dis * lightAtt.x;
    rangeFade = saturate(1-rangeFade*rangeFade);
    rangeFade *= rangeFade;

    float spotFade = dot(spotDirection, lightDirection);
    spotFade = saturate(spotFade * lightAtt.z + lightAtt.w);
    spotFade *= spotFade;


    diffuse *= rangefade * spotFade / max(dis, 1e-3f);
    return diffuse * lightColor;
}

struct VertexInput{
    float4 pos : POSITION;
    float3 normal : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput{
    float4 clipPos : SV_POSITION;
    float3 normal : TEXCOORD0;
    float3 worldPos : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID

};

VertexOutput LitPassVertex(VertexInput input){
    VertexOutput output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
    output.clipPos = mul(unity_MatrixVP, worldPos);
    output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);
    output.worldPos = worldPos;
    return output;
}

float4 LitPassFragment(VertexOutput input) :SV_TARGET{
    //return _Color;
    UNITY_SETUP_INSTANCE_ID(input);
    input.normal = normalize(input.normal);
    float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;

    float3 diffuseLight = 0;
    for (int i = 0; i < MAX_VISIBLE_LIGHTS; ++i)
        diffuseLight += DiffuseLight(i, input.normal, input.worldPos);

    float3 color = diffuseLight * albedo;
    return float4(color, 1.0);
}       

#endif //MYRP_UNLIT_INCLUDE