Shader "BaseConquest/RoadTransition"
{
 Properties { [MainTexture] _BaseMap("Road",2D)="white"{} _MaskMap("Edge alpha",2D)="white"{} _Grid("Grid width, height, cell size",Vector)=(24,14,1.2,0) }
 SubShader
 {
  Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off ZTest LEqual Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Input {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
   struct Output {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float2 roadUV:TEXCOORD1;};
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_MaskMap);SAMPLER(sampler_MaskMap);
   CBUFFER_START(UnityPerMaterial) float4 _Grid; CBUFFER_END
   Output vert(Input v){Output o;float3 p=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(p);o.uv=v.uv;o.roadUV=p.xz/_Grid.z+_Grid.xy*.5;return o;}
   half4 frag(Output v):SV_Target{half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.roadUV);c.a*=SAMPLE_TEXTURE2D(_MaskMap,sampler_MaskMap,v.uv).a;return c;}
   ENDHLSL
  }
 }
}
