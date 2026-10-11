Shader "VR Battlegrounds/Lobby/Distant Ground"
{
    Properties
    {
        _BaseMap("Гравий", 2D) = "white" {}
        _BaseColor("Цвет", Color) = (.8,.8,.75,1)
        _FogColor("Дальняя дымка", Color) = (.44,.55,.61,1)
        _TileMeters("Размер текстуры в метрах", Float) = 2
        _FogStart("Начало дымки", Float) = 80
        _FogEnd("Полная дымка", Float) = 260
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _FogColor;
            float _TileMeters, _FogStart, _FogEnd;
            CBUFFER_END
            struct Input { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Output Vert(Input v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Output o=(Output)0; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world); return o;
            }
            half4 Frag(Output i) : SV_Target
            {
                // Без локальных источников света и normal map; дымка вычисляется по пикселю, а не углам большого quad.
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 ground=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.world.xz/max(.1,_TileMeters)).rgb*_BaseColor.rgb;
                float distanceXZ=distance(_WorldSpaceCameraPos.xz,i.world.xz);
                float fog=smoothstep(_FogStart,_FogEnd,distanceXZ);
                return half4(lerp(ground,_FogColor.rgb,fog),1);
            }
            ENDHLSL
        }
    }
}
