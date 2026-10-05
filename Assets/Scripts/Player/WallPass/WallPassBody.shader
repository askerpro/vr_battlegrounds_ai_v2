Shader "VR Battlegrounds/WallPass/Body"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            ZWrite Off ZTest LEqual Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float _Strength;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half rim = pow(1.0 - saturate(abs(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)))), 2.0);
                half stripe = smoothstep(0.65, 0.85, frac(dot(input.positionWS, float3(0, 24, 5))));
                return half4(half3(0.22, 0.86, 1.0) * (0.65 + rim * 0.35), saturate(_Strength) * (0.3 + stripe * 0.35 + rim * 0.35));
            }
            ENDHLSL
        }
    }
}
