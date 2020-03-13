Shader "My Pipeline/Unlit"{
	Properties{
		_Color ("Color", Color) = (1,1,1,1)
	}
		SubShader{
		Pass
		{
			HLSLPROGRAM

			#pragma target 3.5

			#pragma multi_compile_instancing

			#pragma vertex UnlitPassVertex
			#pragma fragment UnlitPassFragment

			#include "../ShaderLab/Unlit.hlsl"
			ENDHLSL
		
		}
	}
}