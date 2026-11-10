// Patina oro metallizzata sopra il Player durante l'anfetamina (MetallicPatina.cs): un guscio
// trasparente disegnato sopra il modello. Il metallo è finto, senza luci: un riflesso a gradiente
// preso dalla normale in vista (come una matcap: cielo chiaro sopra, terra scura sotto), un
// punto di luce speculare, il bordo acceso (fresnel) e una striscia di luce che scorre.
// Funziona in Built-in e URP (pass senza LightMode = SRPDefaultUnlit), come MiniNuke/Puff.
Shader "DancefloorInvasion/MetallicPatina"
{
    Properties
    {
        _Color ("Oro", Color) = (1, 0.78, 0.2, 1)
        _Alpha ("Intensità", Range(0, 1)) = 0.6
        _SweepSpeed ("Velocità striscia", Float) = 1.4
        _Inflate ("Spessore (m)", Float) = 0.02
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Alpha;
            float _SweepSpeed;
            float _Inflate;

            struct a2v { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 viewNormal : TEXCOORD0; float3 world : TEXCOORD1; };

            // Il guscio è gonfiato di _Inflate metri lungo la normale, in mondo (la scala del
            // modello non conta): sta appena fuori dal corpo e non deve contendersi la profondità
            // con lui punto per punto.
            v2f vert (a2v v)
            {
                v2f o;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz + worldNormal * _Inflate;
                o.pos = UnityWorldToClipPos(world);
                o.viewNormal = normalize(mul((float3x3)UNITY_MATRIX_V, worldNormal));
                o.world = world;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.viewNormal);
                float env = smoothstep(-0.6, 0.9, n.y);                       // riflesso cielo/terra
                float spec = pow(saturate(dot(n, normalize(float3(0.35, 0.6, 0.75)))), 28);
                float rim = pow(1 - saturate(n.z), 3);
                float sweep = frac((i.world.y * 0.8 + i.world.x * 0.35 + i.world.z * 0.2) * 0.5 - _Time.y * _SweepSpeed * 0.5);
                float band = smoothstep(0.0, 0.06, sweep) * (1 - smoothstep(0.06, 0.14, sweep));

                float3 gold = _Color.rgb;
                float3 col = gold * (0.3 + 1.0 * env) + float3(1, 0.97, 0.85) * (spec * 1.2 + band * 0.7) + gold * rim * 0.8;
                float a = _Alpha * saturate(0.55 + 0.45 * env + rim * 0.6 + spec + band * 0.5);
                return float4(col, a);
            }
            ENDCG
        }
    }
}
