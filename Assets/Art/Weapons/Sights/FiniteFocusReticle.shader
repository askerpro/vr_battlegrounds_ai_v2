Shader "VR Battlegrounds/Weapons/Finite Focus Reticle"
{
    Properties
    {
        _GlassTint("Glass", Color) = (0.08,0.12,0.15,0.06)
        _ReticleColor("Reticle", Color) = (1,0.025,0.012,1)
        _DotColor("Centre dot (alpha 0 — цвет сетки)", Color) = (0,0,0,0)
        _RingRadius("Angular ring radius", Float) = 0.01
        _RingWidth("Angular ring width", Float) = 0.0004
        _DotRadius("Angular centre radius", Float) = 0.0003
        [Enum(Ring,0,Crosshair,1)] _ReticleStyle("Reticle style", Float) = 0
        [HideInInspector] _ScopeActive("Scope active", Float) = 0
        [HideInInspector] _ScopeEquipped("Scope equipped", Float) = 0
        [HideInInspector] _ScopeApparentHalfField("Scope apparent half field", Float) = 0.1
        [HideInInspector] _ScopeTex("Scope image", 2D) = "black" {}
        [HideInInspector] _ScopeEyepieceWS("Scope eyepiece", Vector) = (0,0,0,0)
        [HideInInspector] _ScopeAxisWS("Scope axis", Vector) = (0,0,1,0)
        [HideInInspector] _ScopePupil("Scope exit pupil", Float) = 0.004
        [HideInInspector] _ScopeLensRadius("Scope lens radius", Float) = 0.02
        [HideInInspector] _ScopeClarity("Scope clarity", Float) = 1
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
                half4 _GlassTint, _ReticleColor, _DotColor;
                float _RingRadius, _RingWidth, _DotRadius, _OpticValid, _ReticleStyle, _ScopeActive, _ScopeEquipped, _ScopeApparentHalfField, _ScopePupil, _ScopeLensRadius, _ScopeClarity;
                float4 _OpticZeroWS, _OpticForwardWS, _OpticRightWS, _OpticUpWS, _ScopeEyepieceWS, _ScopeAxisWS;
            CBUFFER_END
            // Кадр камеры кратности (WeaponScopeCamera): моно, центр — точка сведения, оси — оси ShotSource.
            TEXTURE2D(_ScopeTex);
            SAMPLER(sampler_ScopeTex);

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
            // Покрытие сетки по углу от точки сведения (рад): кольцо с точкой или перекрестие со столбиками.
            half Reticle(float2 angular, out half centre)
            {
                float radius = length(angular);
                float aa = max(fwidth(radius), 0.000001);
                centre = 1.0 - smoothstep(_DotRadius, _DotRadius + aa, radius);
                float axisDistance = min(abs(angular.x), abs(angular.y));
                if (_ReticleStyle < 0.5)
                {
                    float ring = 1.0 - smoothstep(_RingWidth * 0.5, _RingWidth * 0.5 + aa, abs(radius - _RingRadius));
                    float cross = (1.0 - smoothstep(_RingWidth * 0.5, _RingWidth * 0.5 + aa, axisDistance))
                        * (1.0 - smoothstep(_RingRadius + _RingWidth * 4, _RingRadius + _RingWidth * 4 + aa, radius))
                        * smoothstep(_RingRadius - _RingWidth * 2, _RingRadius - _RingWidth * 2 + aa, radius);
                    return saturate(max(centre, max(ring, cross)));
                }
                // Перекрестие через всё поле: тонкие нити у центра, за радиусом кольца — толстые столбики.
                float width = radius > _RingRadius ? _RingWidth * 3.0 : _RingWidth;
                float thread = 1.0 - smoothstep(width * 0.5, width * 0.5 + aa, axisDistance);
                return saturate(max(centre, thread));
            }

            half4 Compose(half4 background, half coverage, half centre)
            {
                half4 color = lerp(background, _ReticleColor, coverage);
                // Точка в центре — своим цветом (снайперская сетка: чёрные нити, красная точка).
                half4 dotColor = _DotColor.a > 0.0 ? _DotColor : _ReticleColor;
                return lerp(color, dotColor, centre);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_OpticValid < 0.5) return _GlassTint;
                // URP выбирает unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex].
                float3 eye = GetCameraPositionWS();
                half centre;

                if (_ScopeActive > 0.5)
                {
                    // Увеличивающая оптика: картинка камеры и сетка привязаны к стеклу окуляра (как в трубе настоящего
                    // прицела), центр стекла — точка сведения. Поле окуляра = радиус стекла / удаление глаза (WeaponScopeCamera).
                    float field = max(_ScopeApparentHalfField, 0.000001);
                    float3 onGlass = input.positionWS - _ScopeEyepieceWS.xyz;
                    float2 lens = float2(dot(onGlass, _OpticRightWS.xyz), dot(onGlass, _OpticUpWS.xyz)) / max(_ScopeLensRadius, 0.00001);
                    // Чёткость (WeaponScopeCamera.Clarity, решение пользователя 2026-10-24): вдали от окуляра и вбок от оси
                    // картинка размыта и тусклее, по мере выравнивания плавно становится чёткой; сетка — только при чёткой.
                    float clarity = saturate(_ScopeClarity);
                    float2 uv = 0.5 + 0.5 * lens;
                    float blur = (1.0 - clarity) * 0.06;
                    half3 image = SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv).rgb;
                    if (blur > 0.001)
                    {
                        image += SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv + float2(blur, 0)).rgb;
                        image += SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv - float2(blur, 0)).rgb;
                        image += SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv + float2(0, blur)).rgb;
                        image += SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv - float2(0, blur)).rgb;
                        image += SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv + float2(blur, blur) * 0.7).rgb;
                        image += SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv - float2(blur, blur) * 0.7).rgb;
                        image += SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv + float2(blur, -blur) * 0.7).rgb;
                        image += SAMPLE_TEXTURE2D(_ScopeTex, sampler_ScopeTex, uv - float2(blur, -blur) * 0.7).rgb;
                        image /= 9.0;
                    }
                    half centreLight = lerp(0.4, 1.0, clarity);
                    // Зрачок выхода (решение пользователя 2026-10-24): светлое пятно всегда в центре стекла, вокруг — затенение
                    // с мягким краем. Не в ракурсе — размытое пятно около половины радиуса линзы; по мере выравнивания
                    // (чёткость по расстоянию и углу и смещение глаза в долях допуска ракурса) оно расширяется до всей линзы.
                    float3 fromEyepiece = eye - _ScopeEyepieceWS.xyz;
                    float3 lateral = fromEyepiece - _ScopeAxisWS.xyz * dot(fromEyepiece, _ScopeAxisWS.xyz);
                    float2 offset = float2(dot(lateral, _OpticRightWS.xyz), dot(lateral, _OpticUpWS.xyz)) / max(_ScopePupil, 0.00001);
                    float miss = length(offset);
                    float open = clamp(min(lerp(0.5, 1.3, clarity), 1.3 - 0.4 * miss), 0.5, 1.3);
                    // Край пятна: в размытии узкий (вокруг пятна не видно деталей), при прицеливании мягкий.
                    half soft = lerp(0.12, 0.3, clarity);
                    half inside = 1.0 - smoothstep(open - soft, open, length(lens));
                    half tube = 1.0 - smoothstep(0.92, 1.0, length(lens));
                    // Периферия вокруг пятна — затемнение 90% (решение пользователя 2026-10-24): 10% яркости картинки.
                    half4 background = half4(image * lerp(0.1, centreLight, inside) * tube, 1.0);
                    half sharp = smoothstep(0.5, 1.0, clarity);
                    half coverage = Reticle(lens * field, centre) * inside * tube * sharp;
                    return Compose(background, coverage, centre * inside * tube * sharp);
                }

                // Коллиматор (и оптика без картинки): сетка конечного фокуса по лучу глаза к точке сведения.
                float3 ray = input.positionWS - eye;
                float denominator = dot(ray, _OpticForwardWS.xyz);
                float numerator = dot(_OpticZeroWS.xyz - eye, _OpticForwardWS.xyz);
                if (denominator <= 0.0 || numerator <= 0.0) return _GlassTint;
                float3 onVirtualPlane = eye + ray * (numerator / denominator);
                float3 delta = onVirtualPlane - _OpticZeroWS.xyz;
                float2 angular = float2(dot(delta, _OpticRightWS.xyz), dot(delta, _OpticUpWS.xyz)) / _OpticZeroWS.w;
                half coverage = Reticle(angular, centre);
                // Увеличивающая оптика без картинки (глаз не у окуляра) — только тёмное стекло, сетку видно лишь в окуляр.
                half visible = _ScopeEquipped > 0.5 ? 0.0 : 1.0;
                return Compose(_GlassTint, coverage * visible, centre * visible);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
