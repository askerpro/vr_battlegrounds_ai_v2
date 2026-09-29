// Граница зоны спавна. Объём не рисуется — только его границы:
//   · стены — сетка красных «лазерных» лучей, как заграждение (всегда красная, у всех команд);
//   · пол — заливка цвета команды с яркой каймой: по цвету пола выбывший находит свою базу.
//     Виден только снаружи: стоящий в зоне видит одни лазеры;
//   · потолка нет.
// Задние грани не отсекаются: зону видно и снаружи, и стоя внутри.
//
// Коробка зоны на картах уходит под землю (центр — на уровне пола), поэтому пол рисуется не на её
// нижней грани, а на высоте пола _FloorY (мир), которую находит TeamSpawnZone; стены ниже пола
// отбрасываются. _FloorY не задан — пол на нижней грани.
//
// Геометрия — встроенный куб (объектные координаты −0.5…0.5), масштабированный под зону. Шаг
// сетки и толщина лучей — в метрах мира. Цвет пола — цвет команды (_BaseColor, его ставит
// TeamSpawnZone через MaterialPropertyBlock). Выбывшему своя зона — тем же шейдером с
// _ZTest = Always и большей _Brightness: сквозь другие объекты виден только пол, лазеры
// перекрываются геометрией всегда. Поэтому два прохода: стены (UniversalForward, ZTest LEqual) и
// пол (SRPDefaultUnlit, ZTest [_ZTest]) — URP рисует оба.
Shader "VrBattlegrounds/SpawnZoneLaser"
{
    Properties
    {
        _BaseColor ("Цвет пола (команды)", Color) = (0, 0.5, 1, 1)
        _GridColor ("Цвет лучей", Color) = (1, 0.05, 0.02, 1)
        _Brightness ("Общая яркость", Float) = 1
        _GridSpacing ("Шаг горизонтальных лучей, м", Float) = 0.25
        _VerticalSpacing ("Шаг вертикальных лучей, м", Float) = 1.0
        _LineWidth ("Толщина луча, м", Float) = 0.012
        _LineIntensity ("Яркость лучей", Float) = 2.5
        _FloorFill ("Заливка пола", Range(0, 1)) = 0.45
        _FloorRim ("Кайма пола, м", Float) = 0.12
        _FloorRimIntensity ("Яркость каймы пола", Float) = 2.0
        _PulseSpeed ("Скорость пульса", Float) = 0.8
        _ScanSpeed ("Скорость бегущего блика, м/с", Float) = 0.6
        _FloorY ("Высота пола (мир), ставит зона", Float) = -100000
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest пола (Always — сквозь стены)", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GridColor;
                float _Brightness;
                float _GridSpacing;
                float _VerticalSpacing;
                float _LineWidth;
                float _LineIntensity;
                float _FloorFill;
                float _FloorRim;
                float _FloorRimIntensity;
                float _PulseSpeed;
                float _ScanSpeed;
                float _FloorY;
            CBUFFER_END

            #define HAS_FLOOR (_FloorY > -99999.0)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Нижняя грань — на пол (чуть выше, чтобы не спорить с ним по глубине).
                if (HAS_FLOOR && input.normalOS.y < -0.5) positionWS.y = _FloorY + 0.015;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // 1 на луче, 0 между лучами; сглаживание — по размеру пикселя.
            float Lines(float coord, float spacing, float width)
            {
                float d = abs(frac(coord / spacing + 0.5) - 0.5) * spacing;
                float aa = max(fwidth(coord), 1e-4);
                return 1.0 - smoothstep(width, width + aa * 1.5, d);
            }

            float Pulse() { return 0.85 + 0.15 * sin(_Time.y * _PulseSpeed * 6.2831); }

            // Пол: только нижняя грань.
            half4 fragFloor(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 n = normalize(input.normalOS);
                float pulse = Pulse();
                if (n.y >= -0.5) discard;

                // Камера (глаз) внутри зоны в плане — пол не рисуем: стоящий в зоне видит лазеры.
                float3 eyeOS = TransformWorldToObject(GetCameraPositionWS());
                if (abs(eyeOS.x) < 0.5 && abs(eyeOS.z) < 0.5 && eyeOS.y < 0.5) discard;

                // Пол: заливка цвета команды + яркая кайма у стен. Расстояние до края — в метрах мира.
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20),
                                      length(UNITY_MATRIX_M._m01_m11_m21),
                                      length(UNITY_MATRIX_M._m02_m12_m22));
                float edge = min((0.5 - abs(input.positionOS.x)) * scale.x,
                                 (0.5 - abs(input.positionOS.z)) * scale.z);
                float rim = 1.0 - smoothstep(0.0, _FloorRim, edge);

                float a = (_FloorFill + rim * _FloorRimIntensity) * pulse * _Brightness;
                return half4(_BaseColor.rgb * (1.0 + rim), saturate(a));
            }

            // Стены: боковые грани. Потолок не рисуем: зона — ограда и пол, а не коробка.
            half4 fragWalls(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 n = normalize(input.normalOS);
                float pulse = Pulse();
                if (abs(n.y) > 0.5) discard;

                // Стены под полом не видны никому.
                if (HAS_FLOOR && input.positionWS.y < _FloorY) discard;

                // Стена: только лучи, без заливки. Горизонтальные по высоте от пола, вертикальные вдоль стены.
                float3 nWS = normalize(input.normalWS);
                float3 along = normalize(cross(float3(0, 1, 0), nWS));
                float u = dot(input.positionWS, along);
                float y = input.positionWS.y - (HAS_FLOOR ? _FloorY : 0.0);

                float horizontal = Lines(y, _GridSpacing, _LineWidth);
                float vertical = Lines(u, _VerticalSpacing, _LineWidth * 0.7);
                float beam = max(horizontal, vertical * 0.7);
                if (beam <= 0.001) discard;

                // Бегущий вверх блик — лучи «живые», как сканер.
                float scan = frac((y - _Time.y * _ScanSpeed) / (_GridSpacing * 8.0));
                float glow = beam * (1.0 + 1.5 * smoothstep(0.9, 1.0, scan));

                half3 color = _GridColor.rgb * (1.0 + glow * _LineIntensity * 0.5);
                return half4(color, saturate(glow * _LineIntensity * 0.4 * pulse * _Brightness));
            }
        ENDHLSL

        Pass
        {
            Name "SpawnZoneWalls"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragWalls
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "SpawnZoneFloor"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragFloor
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    FallBack Off
}
