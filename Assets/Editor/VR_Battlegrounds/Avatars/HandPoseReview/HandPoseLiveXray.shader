Shader "Hidden/VRBattlegrounds/HandPoseLiveXray"
{
    Properties { _Alpha("Alpha",Float)=.8 _UseField("Use field",Float)=0 _Tint("Tint",Color)=(.85,.86,.9,1) _DistanceField("Distance field",3D)=""{} }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE3D(_DistanceField);SAMPLER(sampler_DistanceField);
            float4 _FieldMinimum,_FieldSize,_FieldDimensions,_Tint;
            float _Alpha,_UseField,_FieldVoxel,_FieldError;
            struct Input {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
            struct Output {float4 positionCS:SV_POSITION;float3 positionOS:TEXCOORD0;float3 normalOS:TEXCOORD1;};
            Output vert(Input i){Output o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.positionOS=i.positionOS.xyz;o.normalOS=i.normalOS;return o;}
            half4 frag(Output i):SV_Target{
                float3 tint=_Tint.rgb;
                if(_UseField>.5 && all(i.positionOS>=_FieldMinimum.xyz)&&all(i.positionOS<=_FieldMinimum.xyz+_FieldSize.xyz)){
                    float3 cell=(i.positionOS-_FieldMinimum.xyz)/_FieldVoxel;
                    float2 d=SAMPLE_TEXTURE3D_LOD(_DistanceField,sampler_DistanceField,(cell+.5)/_FieldDimensions.xyz,0).rg;
                    if(d.y>.9999&&d.x+_FieldError<0)tint=float3(1,.12,.12);
                    else if(abs(d.x)-_FieldError<=.0020000001)tint=(d.y>.9999&&d.x-_FieldError>=0&&d.x+_FieldError<=.0020000001)?float3(.1,1,.2):float3(1,.8,.1);
                }
                float shade=.65+.35*abs(dot(normalize(i.normalOS+1e-10),normalize(float3(.4,.8,.6))));
                return half4(tint*shade,_Alpha);
            }
            ENDHLSL
        }
    }
}
