// Mare cartoon per la spiaggia di GameplayV2. Da usare sul piano creato dal comando
// Tools/Mare/Crea o aggiorna il mare (vedi Assets/Editor/MareCartoonBuilder.cs): un semplice
// Quad sdraiato, tutto il resto lo fa questo shader.
//
// Perche non il pack ocean&lakeShaderPack gia nel progetto: quello disegna la schiuma dove
// l'acqua incontra il fondale, leggendo la profondita della scena. Qui la spiaggia e un cubo
// perfettamente piatto (l'oggetto Floor), quindi non esiste nessuna linea di riva da trovare:
// l'acqua coprirebbe tutto o niente. La battigia qui e invece disegnata per conto suo, a una
// distanza decisa da _ShoreOffset, e proprio per questo puo fare quello che un bordo
// geometrico non potrebbe: andare avanti e indietro, ed essere sinuosa invece che dritta.
//
// Tutto e in metri e procedurale: niente texture da importare, niente Depth/Opaque Texture da
// accendere nell'asset URP. Il piano e un Quad 1x1 ruotato di 90 gradi sull'asse X, quindi:
//   - il bordo a monte (verso la spiaggia) e quello a +Z, ed e lo zero da cui si contano i metri;
//   - la coordinata locale Y va da +0.5 (riva) a -0.5 (largo), la X corre lungo la riva.
// Le distanze sono ricavate dalla scala dell'oggetto, cosi i parametri restano in metri veri
// comunque venga ridimensionato il piano.
Shader "DancefloorInvasion/Mare Cartoon"
{
    Properties
    {
        [Header(Acqua)]
        _ShallowColor ("Colore a riva", Color) = (0.45, 0.86, 0.82, 0.30)
        _DeepColor ("Colore al largo", Color) = (0.05, 0.37, 0.62, 0.85)
        _DeepDistance ("Metri per arrivare al colore del largo", Float) = 10
        _ColorSteps ("Gradini di colore, 0 = sfumatura continua", Range(0, 10)) = 4
        _BandWobble ("Quanto le fasce di colore serpeggiano in metri", Range(0, 12)) = 1.2

        [Header(Battigia)]
        _ShoreOffset ("Distanza della battigia dal bordo a monte in metri", Float) = 2.5
        _WashDistance ("Quanto avanza e si ritira la risacca in metri", Float) = 0.9
        _WashSpeed ("Respiri di risacca al secondo", Float) = 0.13
        _EdgeLength ("Lunghezza delle anse della battigia in metri", Float) = 14
        _EdgeDepth ("Quanto la battigia e sinuosa in metri", Float) = 0.6
        _EdgeSoftness ("Sfumatura del bordo bagnato in metri", Float) = 0.25

        [Header(Schiuma)]
        _FoamColor ("Colore della schiuma", Color) = (1, 1, 1, 0.9)
        _FoamWidth ("Larghezza della schiuma in metri", Float) = 0.7
        _WetSandColor ("Velo di sabbia bagnata, alpha 0 = spento", Color) = (0.29, 0.24, 0.17, 0)
        _WetSandWidth ("Quanto resta bagnata la sabbia in metri", Float) = 2.2

        [Header(Ondine verso riva)]
        _RippleColor ("Colore delle ondine", Color) = (1, 1, 1, 0.45)
        _RippleSpacing ("Distanza tra una ondina e la successiva in metri", Float) = 3.5
        _RippleWidth ("Spessore delle ondine", Range(0.01, 0.5)) = 0.1
        _RippleSpeed ("Velocita con cui arrivano a riva in metri al secondo", Float) = 1.1
        _RippleReach ("Fin dove al largo si vedono le ondine in metri", Float) = 16

        [Header(Onda lunga)]
        _SwellHeight ("Altezza dell onda lunga in metri", Float) = 0.07
        _SwellLength ("Lunghezza dell onda lunga in metri", Float) = 8
        _SwellSpeed ("Velocita dell onda lunga", Float) = 0.5

        [Header(Riflessi)]
        _SkyColor ("Colore del cielo riflesso", Color) = (0.74, 0.89, 1, 1)
        _SkyAmount ("Quanto si specchia il cielo", Range(0, 1)) = 0.45
        _FresnelPower ("Quanto e stretto il riflesso radente", Range(0.5, 8)) = 3
        _GlintAmount ("Quantita di luccichii", Range(0, 1)) = 0.35
        _GlintSize ("Soglia dei luccichii, piu alta = piu radi", Range(0.8, 0.999)) = 0.965
        _GlintSpeed ("Velocita dei luccichii", Float) = 0.4
        _GlintFade ("Entro quanti metri dalla camera si vedono i luccichii", Float) = 90
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "MareCartoonForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            // Il mare sta appoggiato sulla sabbia, quasi complanare: l'offset lo tira verso la
            // camera quel tanto che basta a non sfarfallare contro il pavimento.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define SEA_TAU 6.2831853

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _DeepDistance;
                float _ColorSteps;
                float _BandWobble;

                float _ShoreOffset;
                float _WashDistance;
                float _WashSpeed;
                float _EdgeLength;
                float _EdgeDepth;
                float _EdgeSoftness;

                float4 _FoamColor;
                float _FoamWidth;
                float4 _WetSandColor;
                float _WetSandWidth;

                float4 _RippleColor;
                float _RippleSpacing;
                float _RippleWidth;
                float _RippleSpeed;
                float _RippleReach;

                float _SwellHeight;
                float _SwellLength;
                float _SwellSpeed;

                float4 _SkyColor;
                float _SkyAmount;
                float _FresnelPower;
                float _GlintAmount;
                float _GlintSize;
                float _GlintSpeed;
                float _GlintFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                // x: metri dal bordo a monte, cresce verso il largo. y: metri lungo la riva.
                float2 shore : TEXCOORD1;
            };

            // L'onda lunga del largo: due creste incrociate appena accennate, il mare e calmo.
            // Restituisce l'altezza e, in slope, la pendenza: e la normale a far luccicare i
            // riflessi, non uno spostamento vero dei vertici (a questa distanza non si vedrebbe).
            float SeaSwell(float2 p, float t, out float2 slope)
            {
                float2 d1 = normalize(float2(0.92, 0.39));
                float2 d2 = normalize(float2(-0.35, 0.94));
                float k1 = SEA_TAU / max(_SwellLength, 0.01);
                float k2 = SEA_TAU / max(_SwellLength * 0.57, 0.01);
                float a1 = _SwellHeight;
                float a2 = _SwellHeight * 0.55;
                float phase1 = dot(p, d1) * k1 + t * _SwellSpeed;
                float phase2 = dot(p, d2) * k2 - t * _SwellSpeed * 1.37;
                slope = d1 * (a1 * k1 * cos(phase1)) + d2 * (a2 * k2 * cos(phase2));
                return a1 * sin(phase1) + a2 * sin(phase2);
            }

            Varyings Vertex(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;

                // Quanto e grande il piano in metri: le colonne della matrice sono i suoi assi
                // locali visti nel mondo, e la loro lunghezza e la scala su quell'asse.
                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float alongShoreScale = length(float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20));
                float outToSeaScale = length(float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21));

                // Il Quad va da -0.5 a +0.5: +0.5 e il bordo a monte, quello verso la spiaggia.
                OUT.shore.x = (0.5 - IN.positionOS.y) * outToSeaScale;
                OUT.shore.y = IN.positionOS.x * alongShoreScale;
                return OUT;
            }

            half4 Fragment(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float offshore = IN.shore.x; // metri dal bordo a monte
                float along = IN.shore.y;    // metri lungo la riva

                // --- fin dove arriva l'acqua in questo istante ------------------------------
                // La battigia non e dritta (due seni di lunghezza diversa la rendono sinuosa) e
                // respira avanti e indietro, molto piano: e la risacca.
                float edge = sin(along * SEA_TAU / max(_EdgeLength, 0.01) + t * 0.27) * _EdgeDepth
                           + sin(along * SEA_TAU / max(_EdgeLength * 0.43, 0.01) - t * 0.19) * _EdgeDepth * 0.45;
                float wash = sin(t * SEA_TAU * _WashSpeed) * _WashDistance;
                float shoreLine = _ShoreOffset + wash + edge;

                // Positivo dentro l'acqua, negativo sulla sabbia asciutta.
                float depth = offshore - shoreLine;
                float water = smoothstep(0.0, max(_EdgeSoftness, 0.001), depth);

                // --- onda lunga: serve alla normale e a far ondeggiare le fasce di colore ----
                float2 slope;
                float swell = SeaSwell(IN.positionWS.xz, t, slope);
                float3 normalWS = normalize(float3(-slope.x, 1.0, -slope.y));

                // --- colore dell'acqua, a gradini come un disegno ---------------------------
                // Le fasce non corrono dritte: l'onda lunga le fa serpeggiare di qualche metro
                // avanti e indietro, ed e quello che le fa sembrare disegnate a mano.
                float swell01 = swell / max(_SwellHeight * 1.55, 0.0001); // circa da -1 a +1
                float deep01 = saturate((depth + swell01 * _BandWobble) / max(_DeepDistance, 0.001));
                float banded = _ColorSteps >= 1.0 ? floor(deep01 * _ColorSteps + 0.5) / _ColorSteps : deep01;
                float3 rgb = lerp(_ShallowColor.rgb, _DeepColor.rgb, banded);
                float alpha = lerp(_ShallowColor.a, _DeepColor.a, deep01) * water;

                // --- riflesso del cielo di taglio -------------------------------------------
                float3 viewWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float fresnel = pow(saturate(1.0 - dot(normalWS, viewWS)), _FresnelPower);
                rgb = lerp(rgb, _SkyColor.rgb, saturate(fresnel * _SkyAmount));

                // --- ondine che corrono verso riva ------------------------------------------
                // La fase cresce col tempo, quindi la stessa cresta si ritrova ogni volta un po
                // piu vicina a riva: e questo a dare il senso di onde che arrivano.
                float ripplePhase = frac(depth / max(_RippleSpacing, 0.01)
                                       + t * _RippleSpeed / max(_RippleSpacing, 0.01)
                                       + sin(along * 0.11) * 0.15);
                float rippleLine = 1.0 - smoothstep(0.0, _RippleWidth, min(ripplePhase, 1.0 - ripplePhase));
                float ripples = rippleLine * saturate(1.0 - depth / max(_RippleReach, 0.001)) * water;
                rgb = lerp(rgb, _RippleColor.rgb, ripples * _RippleColor.a);
                alpha = max(alpha, ripples * _RippleColor.a * water);

                // --- schiuma sul bagnasciuga -------------------------------------------------
                // Il bordo della schiuma e merlettato: un secondo disturbo piu fitto del primo.
                // La schiuma resta piena per meta fascia e poi si taglia in fretta: sfumandola
                // da subito lasciava un alone slavato dietro all'onda, che di cartoon non ha
                // niente.
                float lace = 0.55 + 0.45 * sin(along * 0.9 + t * 0.8) * sin(along * 0.31 - t * 0.5);
                float foamWidth = max(_FoamWidth * lace, 0.001);
                float foam = (1.0 - smoothstep(foamWidth * 0.55, foamWidth, depth)) * water;
                rgb = lerp(rgb, _FoamColor.rgb, foam * _FoamColor.a);
                alpha = max(alpha, foam * _FoamColor.a);

                // --- luccichii --------------------------------------------------------------
                float2 g = IN.positionWS.xz;
                float sparkle = sin(g.x * 0.9 + t * _GlintSpeed)
                              * sin(g.y * 1.13 - t * _GlintSpeed * 0.8)
                              * sin((g.x + g.y) * 0.61 + t * _GlintSpeed * 1.3);
                // Da lontano i luccichii sono piu fitti di un pixel e diventerebbero un
                // formicolio: oltre _GlintFade metri si spengono e resta il colore pulito.
                float viewDistance = length(_WorldSpaceCameraPos - IN.positionWS);
                float glintFade = saturate(1.0 - viewDistance / max(_GlintFade, 1.0));
                float glint = smoothstep(_GlintSize, 1.0, abs(sparkle)) * _GlintAmount * water * glintFade;
                rgb += glint;
                alpha = saturate(alpha + glint * 0.5);

                // --- sabbia lasciata bagnata dall'onda che si ritira -------------------------
                float wet = (1.0 - smoothstep(0.0, max(_WetSandWidth, 0.001), -depth)) * (1.0 - water);
                rgb = lerp(rgb, _WetSandColor.rgb, wet);
                alpha = max(alpha, wet * _WetSandColor.a);

                return half4(rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
