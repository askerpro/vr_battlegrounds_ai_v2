Shader "VR Battlegrounds/WallPass/World"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float _Strength;
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half luminance = dot(source.rgb, half3(0.2126, 0.7152, 0.0722));
                half3 cold = luminance * half3(0.48, 0.78, 0.94);
                half3 changed = lerp(source.rgb, cold, 0.85);
                return half4(lerp(source.rgb, changed, saturate(_Strength)), source.a);
            }
            ENDHLSL
        }
    }
}
