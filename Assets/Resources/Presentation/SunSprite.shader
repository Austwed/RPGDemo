Shader "BaseConquest/SunSprite"
{
 Properties { [MainTexture] _BaseMap("Texture",2D)="white"{} [MainColor] _BaseColor("Color",Color)=(1,1,1,1) _SunTint("Sun tint",Color)=(1,0.98,0.94,1) _SunFactor("Shade",Float)=1 }
 SubShader
 {
  Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   ZTest LEqual
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Input { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
   struct Output { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST,_BaseColor,_SunTint;float _SunFactor;
   CBUFFER_END
   Output vert(Input v){Output o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=TRANSFORM_TEX(v.uv,_BaseMap);return o;}
   half4 frag(Output v):SV_Target{half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.uv)*_BaseColor;c.rgb*=_SunTint.rgb*_SunFactor;return c;}
   ENDHLSL
  }
 }
}
