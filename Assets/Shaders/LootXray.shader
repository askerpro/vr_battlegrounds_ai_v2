// Силуэт предмета, закрытого другой геометрией (труп над оружием, T-35): рисуется только там,
// где предмет чем-то заслонён (ZTest Greater), полупрозрачным контуром-«ореолом». Сам предмет
// рисуется как обычно; копия с этим материалом видна только сквозь заслоняющее.
Shader "VrBattlegrounds/LootXray"
{
	Properties
	{
		_Color("Color", Color) = (1, 0.8, 0.25, 0.55)
	}
	SubShader
	{
		Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
		LOD 100

		Pass
		{
			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			ZTest Greater
			Cull Back

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float rim : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			fixed4 _Color;

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.vertex = UnityObjectToClipPos(v.vertex);
				float3 normal = normalize(UnityObjectToWorldNormal(v.normal));
				float3 view = normalize(WorldSpaceViewDir(v.vertex));
				o.rim = 1.0 - saturate(dot(normal, view));
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				return fixed4(_Color.rgb, _Color.a * (0.35 + 0.65 * i.rim));
			}
			ENDCG
		}
	}
}
