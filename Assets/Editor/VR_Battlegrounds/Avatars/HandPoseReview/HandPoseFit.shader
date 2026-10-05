Shader "Hidden/VRBattlegrounds/HandPoseFit"
{
    Properties { _Src("Src",Float)=1 _Dst("Dst",Float)=0 _ZWrite("ZWrite",Float)=1 _ZTest("ZTest",Float)=4 _Shade("Shade",Float)=1 _Alpha("Alpha",Float)=1 }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass {
            Blend [_Src] [_Dst]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Input {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR;};
            struct Output {float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;float4 color:COLOR;};
            float _Shade, _Alpha;
            Output vert(Input i) {Output o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.color=i.color;return o;}
            half4 frag(Output i):SV_Target {float light=.45+.55*abs(dot(normalize(i.normalWS+float3(0,0,.00001)),normalize(float3(.4,.8,.6))));return half4(i.color.rgb*lerp(1,light,_Shade),i.color.a*_Alpha);}
            ENDHLSL
        }
    }
}
