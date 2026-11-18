Shader "BaseConquest/SunSprite"
{
 Properties { [MainTexture] _BaseMap("Texture",2D)="white"{} [MainColor] _BaseColor("Color",Color)=(1,1,1,1) _SunTint("Sun tint",Color)=(1,1,1,1) _SunFactor("Shade",Float)=1 [HideInInspector] _ActorFadeCount("Actor masks",Float)=0 }
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
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Input { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
   struct Output { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 screen:TEXCOORD1; };
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST,_BaseColor,_SunTint;float _SunFactor,_ActorFadeCount;
   float4 _ActorFadeMasks[32];
   CBUFFER_END
   Output vert(Input v)
   {
    Output o;
    VertexPositionInputs position=GetVertexPositionInputs(v.positionOS.xyz);
    o.positionCS=position.positionCS;
    o.uv=TRANSFORM_TEX(v.uv,_BaseMap);
    o.screen=position.positionNDC;
    return o;
   }
   half4 frag(Output v):SV_Target
   {
    half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.uv)*_BaseColor;
    color.rgb*=_SunTint.rgb*_SunFactor;
    if(color.a==0||_ActorFadeCount==0)return color;
    float2 position=v.screen.xy/v.screen.w;
    float opacity=1;
    [loop] for(int i=0;i<(int)_ActorFadeCount;i++)
    {
     float4 mask=_ActorFadeMasks[i];
     float distanceFromActor=length((position-mask.xy)/mask.zw);
     opacity=min(opacity,lerp(.2,1,smoothstep(.74,1,distanceFromActor)));
    }
    color.a*=opacity;
    return color;
   }
   ENDHLSL
  }
 }
}
