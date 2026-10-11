// Orizzonte degli eventi: nero assoluto con anello fotonico sul bordo. Built-in e URP.
Shader "BlackHole/Horizon"
{
    Properties
    {
        [HDR] _RimColor ("Colore anello fotonico", Color) = (1, 0.8, 0.55, 1)
        _RimPower ("Nitidezza anello", Range(1, 16)) = 6
        _RimIntensity ("Intensità anello", Range(0, 10)) = 1.5
        _Psyche ("Psichedelia", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _RimColor;
            float _RimPower, _RimIntensity, _Psyche;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; float3 o : TEXCOORD2; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz;
                o.o = v.vertex.xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float f = pow(1.0 - saturate(dot(normalize(i.n), normalize(i.v))), _RimPower);
                float h = atan2(i.o.z, i.o.x) / 6.2832 * 2.0 + i.o.y + _Time.y * 0.3;
                float3 rb = (0.5 + 0.5 * cos(6.2832 * (float3(0, 0.33, 0.67) + h))) * 1.6;
                return float4(lerp(_RimColor.rgb, rb, _Psyche) * f * _RimIntensity, 1);
            }
            ENDCG
        }
    }
}
