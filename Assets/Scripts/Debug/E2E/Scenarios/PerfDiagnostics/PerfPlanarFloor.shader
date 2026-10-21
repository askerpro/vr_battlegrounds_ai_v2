// Диагностический пол для E2E normal A/B (PerfRenderDiagnosticControl): одна выборка текстуры с проекцией
// сверху по мировым X/Z (uv = positionWS.xz / _MainTile — как SubG_WorldAlignedTexture пака, но без
// тройной проекции и смешивания слоёв). _PLANAR_LIT: + запечённый свет (лайтмапа или SH), без PBR и нормалей.
// Не используется материалами сцены; в Development-сборку его добавляет PerfDiagnosticShaderBuildProcessor,
// в Prod не попадает. Не путь к релизу.
Shader "VR Battlegrounds/Diagnostics/PerfPlanarFloor"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _MainTile ("World Tile (m)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _PLANAR_LIT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _MainTile;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 planarUV : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.planarUV = positionWS.xz / max(_MainTile, 0.0001);
                output.lightmapUV = input.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.planarUV).rgb * _BaseColor.rgb;
            #if defined(_PLANAR_LIT)
                #if defined(LIGHTMAP_ON)
                    color *= SampleLightmap(input.lightmapUV, normalize(input.normalWS));
                #else
                    color *= SampleSH(normalize(input.normalWS));
                #endif
            #endif
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
