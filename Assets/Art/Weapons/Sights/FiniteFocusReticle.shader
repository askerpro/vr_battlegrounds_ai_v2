Shader "VR Battlegrounds/Weapons/Finite Focus Reticle"
{
    Properties
    {
        _GlassTint("Glass", Color) = (0.08,0.12,0.15,0.06)
        _ReticleColor("Reticle", Color) = (1,0.025,0.012,1)
        _RingRadius("Angular ring radius", Float) = 0.01
        _RingWidth("Angular ring width", Float) = 0.0004
        _DotRadius("Angular centre radius", Float) = 0.0003
        [HideInInspector] _OpticValid("Valid", Float) = 0
        [HideInInspector] _OpticZeroWS("Zero point", Vector) = (0,0,15,15)
        [HideInInspector] _OpticForwardWS("Forward", Vector) = (0,0,1,0)
        [HideInInspector] _OpticRightWS("Right", Vector) = (1,0,0,0)
        [HideInInspector] _OpticUpWS("Up", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "FiniteFocusReticle"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlassTint, _ReticleColor;
                float _RingRadius, _RingWidth, _DotRadius, _OpticValid;
                float4 _OpticZeroWS, _OpticForwardWS, _OpticRightWS, _OpticUpWS;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_OpticValid < 0.5) return _GlassTint;
                // URP выбирает unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex].
                float3 eye = GetCameraPositionWS();
                float3 ray = input.positionWS - eye;
                float denominator = dot(ray, _OpticForwardWS.xyz);
                float numerator = dot(_OpticZeroWS.xyz - eye, _OpticForwardWS.xyz);
                if (denominator <= 0.0 || numerator <= 0.0) return _GlassTint;
                float3 onVirtualPlane = eye + ray * (numerator / denominator);
                float3 delta = onVirtualPlane - _OpticZeroWS.xyz;
                float2 angular = float2(dot(delta, _OpticRightWS.xyz), dot(delta, _OpticUpWS.xyz)) / _OpticZeroWS.w;
                float radius = length(angular);
                float aa = max(fwidth(radius), 0.000001);
                float ring = 1.0 - smoothstep(_RingWidth * 0.5, _RingWidth * 0.5 + aa, abs(radius - _RingRadius));
                float centre = 1.0 - smoothstep(_DotRadius, _DotRadius + aa, radius);
                float cross = (1.0 - smoothstep(_RingWidth * 0.5, _RingWidth * 0.5 + aa, min(abs(angular.x), abs(angular.y))))
                    * (1.0 - smoothstep(_RingRadius + _RingWidth * 4, _RingRadius + _RingWidth * 4 + aa, radius))
                    * smoothstep(_RingRadius - _RingWidth * 2, _RingRadius - _RingWidth * 2 + aa, radius);
                half coverage = saturate(max(centre, max(ring, cross)));
                return half4(lerp(_GlassTint.rgb, _ReticleColor.rgb, coverage), lerp(_GlassTint.a, _ReticleColor.a, coverage));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
