Shader "VR Battlegrounds/Holographic Arsenal Fence"
{
    Properties
    {
        _TintA ("Основной оттенок", Color) = (0.18, 0.47, 0.38, 0.35)
        _TintB ("Перелив", Color) = (0.48, 0.67, 0.52, 0.35)
        _Speed ("Скорость перелива", Float) = 0.45
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _TintA;
                half4 _TintB;
                float _Speed;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half wave=.5+.5*sin(input.positionWS.y*2.5-_Time.y*_Speed);
                half edge=1-abs(dot(normalize(input.normalWS),SafeNormalize(GetWorldSpaceViewDir(input.positionWS))));
                half4 tint=lerp(_TintA,_TintB,wave);
                return half4(tint.rgb*(.65+.45*edge),tint.a*(.7+.3*wave));
            }
            ENDHLSL
        }
    }
}
