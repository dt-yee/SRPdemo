#ifndef MYRP_UNLIT_INCLUDE
#define MYRP_UNLIT_INCLUDE

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
CBUFFER_START(UnityPerDraw)
float4x4 unity_ObjectToWorld;
CBUFFER_END

CBUFFER_START(UnityPerFrame)
float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerMaterial)
float4 _Color;
CBUFFER_END

#define UNITY_MATRIX_M unity_ObjectToWorld
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

struct VertexInput{
    float4 pos : POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput{
    float4 clipPos : SV_POSITION;
};

VertexOutput UnlitPassVertex(VertexInput input){
    VertexOutput output;
    UNITY_SETUP_INSTANCE_ID(input);
    float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
    output.clipPos = mul(unity_MatrixVP, worldPos);
    return output;
}

float4 UnlitPassFragment(VertexOutput input):SV_TARGET{
    return _Color;
}       

#endif //MYRP_UNLIT_INCLUDE