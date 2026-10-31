Shader "BaseConquest/GreyFog"
{
    Properties { _BaseColor("Grey and opacity", Color) = (.4,.42,.44,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _BaseColor;
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 world:TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o;float3 w=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(w);o.world=w.xz;return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p) { float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y); }
            half4 frag(Varyings i):SV_Target { float n=noise(i.world*.55+float2(_Time.y*.055,_Time.y*.025));return half4(_BaseColor.rgb+(n-.5)*.09,_BaseColor.a); }
            ENDHLSL
        }
    }
}
