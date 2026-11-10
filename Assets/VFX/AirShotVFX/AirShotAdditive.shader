Shader "AirShot/Additive"
{
    Properties { _Color("Color", Color) = (1,1,1,1) _MainTex("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One   // additive
        ZWrite Off  Cull Off  Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            sampler2D _MainTex;
            struct a2v { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            v2f vert(a2v v){ v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o; }
            fixed4 frag(v2f i):SV_Target { fixed4 t=tex2D(_MainTex,i.uv); return t*i.color; }
            ENDCG
        }
    }
    FallBack "Particles/Additive"
}
