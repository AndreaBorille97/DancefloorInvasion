// Fascio di luce da palco (quello che si vede nell'aria sotto un faro), stessa filosofia del
// fuoco (Assets/Shaders/FuocoCartoon.shader): tutto nel fragment shader, nessuna texture.
//
// Da usare sul cono generato da StageLightBeam (comando Tools/Luci/Crea un faro a cono).
//
// ── Come si vede ─────────────────────────────────────────────────────────────────────────────
// Il cono e ADDITIVO (Blend One One): somma luce a quello che c'e dietro, non lo copre mai, ed
// e quello che fa sembrare il fascio aria illuminata invece che plastica colorata. Il nero vale
// zero, quindi dove il fascio si spegne sparisce da solo, senza bisogno di alpha.
//
// I bordi del cono vanno sfumati, altrimenti si vede la sagoma netta della mesh. Lo si fa
// guardando quanto la superficie e di taglio rispetto alla camera: al centro del fascio la si
// guarda di fronte (luminoso), ai bordi di taglio (spento). Il cono e disegnato da tutte e due le
// facce (Cull Off), cosi al centro si sommano la parete davanti e quella dietro, come in un vero
// volume di luce.
//
// Lungo il fascio la luce cala allontanandosi dal faro, e all'attacco sfuma per un attimo
// invece di partire con un bordo netto sulla lente. Le striature (il "pulviscolo") sono raggi
// piu chiari e piu scuri che girano piano attorno all'asse: quello che fa sembrare il fascio
// fumo attraversato dalla luce.
//
// Colori sopra 1 = HDR: il Bloom li prende e il faro "brilla" anche con la scena chiara.
Shader "DancefloorInvasion/Fascio di Luce"
{
    Properties
    {
        [HDR] _Color ("Colore", Color) = (2.5, 2.2, 1.6, 1)
        _Intensity ("Intensita", Range(0, 4)) = 0.6
        _LengthFalloff ("Quanto si spegne verso la fine", Range(0.1, 5)) = 1.6
        _EdgeSoftness ("Morbidezza dei bordi, alto = fascio piu stretto", Range(0.3, 8)) = 1.8
        _SourceFade ("Sfumatura all'attacco del faro", Range(0.001, 0.3)) = 0.04
        _DustAmount ("Pulviscolo, quanto si vedono le striature", Range(0, 1)) = 0.35
        _DustStripes ("Quante striature attorno al fascio", Range(1, 30)) = 9
        _DustSpeed ("Velocita delle striature", Range(0, 3)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FascioLuceForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _LengthFalloff;
                float _EdgeSoftness;
                float _SourceFade;
                float _DustAmount;
                float _DustStripes;
                float _DustSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2; // x = giro attorno all'asse (0..1), y = lungo il fascio (0 al faro, 1 in fondo)
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 toCamera = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // 1 dove la parete del cono si guarda di fronte (centro del fascio), 0 dove e di taglio (bordi).
                float facing = pow(saturate(abs(dot(normal, toCamera))), _EdgeSoftness);

                float along = input.uv.y;
                float lengthFade = pow(saturate(1.0 - along), _LengthFalloff) * smoothstep(0.0, _SourceFade, along);

                float stripes = sin(input.uv.x * TWO_PI * _DustStripes + _Time.y * _DustSpeed * TWO_PI + along * 4.0);
                float dust = 1.0 - _DustAmount * (0.5 + 0.5 * stripes);

                float3 color = _Color.rgb * _Intensity * facing * lengthFade * dust;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
