// Fiamma cartoon, stessa filosofia del mare (Assets/Shaders/MareCartoon.shader): tutta disegnata
// nel fragment shader, nessuna texture da importare, colori a gradini netti invece che sfumati.
//
// Da usare sul figlio "Fiamma" creato dal comando Tools/Fuoco/Crea un fuocherello.
//
// ── Come sta in piedi ────────────────────────────────────────────────────────────────────────
// Il quad e un cartellone CILINDRICO: gira attorno all'asse verticale per stare sempre di fronte
// alla camera, ma non si corica mai. Un cartellone normale, che guarda la camera con tutte e due
// gli assi, da una camera inclinata di 45 gradi come questa si vedrebbe sdraiato: la fiamma
// sembrerebbe stesa a terra invece che in piedi sul fuoco.
//
// Il quad e appoggiato per il bordo BASSO sul pivot dell'oggetto, non per il centro: cosi si
// posiziona il fuoco dove tocca terra, che e l'unico punto che interessa.
//
// ── Come e fatta la fiamma ───────────────────────────────────────────────────────────────────
// Una goccia: larga e arrotondata in basso, a punta in cima. Di ogni pixel si guarda quanto e
// lontano dall'asse della fiamma rispetto a quanto e larga la fiamma a quella quota — un numero
// che vale 0 sull'asse e 1 sul bordo. Quel numero e il "calore", ed e lui a scegliere il colore.
//
// I colori non sfumano: si passa da una banda all'altra di scatto (step), ed e questo a dare il
// disegnato invece del realistico. Sono tutti sopra 1, cioe in HDR, quindi il Bloom li prende e
// la fiamma resta accesa anche quando la scena intorno e chiara.
//
// La lingua ondeggia con due seni che salgono lungo la fiamma, moltiplicati per la quota: alla
// base l'ondeggiamento vale zero (il fuoco e ancorato alla legna) e cresce salendo, cosi a
// ballare e solo la punta. L'altezza intanto pulsa piano, che e quello che rende il guizzo.
Shader "DancefloorInvasion/Fuoco Cartoon"
{
    Properties
    {
        [Header(Forma)]
        _Width ("Larghezza della fiamma", Range(0.05, 1)) = 0.34
        _Taper ("Quanto va a punta, alto = piu affusolata", Range(0.3, 3)) = 1.0
        _Wobble ("Quanto sbanda di lato la punta", Range(0, 0.5)) = 0.17
        _Lick ("Quanto si strozza in lingue. Sopra 1 le lingue si staccano", Range(0, 1.5)) = 0.8
        _Flicker ("Quanto sussulta in altezza", Range(0, 0.5)) = 0.17
        _Speed ("Velocita del guizzo", Range(0.1, 10)) = 3.4
        _Softness ("Sfumatura del bordo, tienila bassa", Range(0.001, 0.2)) = 0.02

        [Header(Colori HDR)]
        [HDR] _CoreColor ("Cuore", Color) = (6, 5.4, 2.4, 1)
        [HDR] _HotColor ("Giallo", Color) = (5, 2.6, 0.5, 1)
        [HDR] _MidColor ("Arancione", Color) = (3.2, 0.9, 0.12, 1)
        [HDR] _EdgeColor ("Bordo rosso", Color) = (1.6, 0.25, 0.05, 1)

        [Header(Dove cambia banda)]
        _Band1 ("Bordo verso arancione", Range(0, 1)) = 0.18
        _Band2 ("Arancione verso giallo", Range(0, 1)) = 0.42
        _Band3 ("Giallo verso cuore", Range(0, 1)) = 0.68
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
            Name "FuocoCartoonForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Width;
                float _Taper;
                float _Wobble;
                float _Lick;
                float _Flicker;
                float _Speed;
                float _Softness;
                float4 _CoreColor;
                float4 _HotColor;
                float4 _MidColor;
                float4 _EdgeColor;
                float _Band1;
                float _Band2;
                float _Band3;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 flame : TEXCOORD0; // x: da -1 a 1 in larghezza, y: da 0 (base) a 1 (punta)
            };

            Varyings Vertex(Attributes IN)
            {
                Varyings OUT;

                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float3 centerWS = float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23);
                float width = length(float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20));
                float height = length(float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21));

                // Cartellone cilindrico: l'alto resta l'alto del mondo, e solo il fianco si gira
                // verso la camera. Se la camera fosse esattamente sopra, il prodotto vettoriale
                // degenererebbe: in quel caso un asse qualsiasi va bene, tanto il quad si vede
                // di taglio.
                float3 up = float3(0, 1, 0);
                float3 toCamera = _WorldSpaceCameraPos - centerWS;
                float3 right = cross(up, toCamera);
                right = dot(right, right) > 0.0001 ? normalize(right) : float3(1, 0, 0);

                // Il bordo basso del quad sul pivot: +0.5 perche il Quad di Unity va da -0.5 a 0.5.
                float alongHeight = IN.positionOS.y + 0.5;
                float3 positionWS = centerWS
                                  + right * (IN.positionOS.x * width)
                                  + up * (alongHeight * height);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.flame = float2(IN.positionOS.x * 2.0, alongHeight);
                return OUT;
            }

            half4 Fragment(Varyings IN) : SV_Target
            {
                float t = _Time.y * _Speed;

                // L'altezza sussulta. Tre battiti con periodi che non sono multipli fra loro: la
                // loro somma non torna mai uguale a se stessa, quindi l'occhio non ci trova un
                // motivo che si ripete. Con due soli battiti a periodo semplice si sentirebbe il
                // ritmo, e un fuoco a tempo non e un fuoco.
                float pulse = 1.0 + (sin(t * 0.9) * 0.5 + sin(t * 2.37) * 0.3 + sin(t * 4.11) * 0.2) * _Flicker;
                float height = saturate(IN.flame.y / max(pulse, 0.2));

                // Quanto e larga la fiamma a questa quota: a punta in cima, arrotondata in basso.
                float taper = pow(saturate(1.0 - height), _Taper);
                float rounded = smoothstep(0.0, 0.12, height);
                float halfWidth = _Width * taper * rounded;

                // Le lingue. Un'onda sale lungo la fiamma e le strozza la larghezza mentre passa:
                // dove strozza, la fiamma si assottiglia; se _Lick e abbastanza alto la
                // strozzatura arriva a chiudersi del tutto e quel pezzo si STACCA, salendo da
                // solo. E il comportamento che distingue una torcia da un fuocherello tranquillo.
                // Moltiplicata per la quota, perche alla base la fiamma resta attaccata alla legna.
                float lick = sin(height * 13.0 - t * 3.1) * 0.6 + sin(height * 21.0 - t * 4.9) * 0.4;
                halfWidth *= 1.0 + lick * _Lick * height;

                // Lo sbandamento laterale, con una terza armonica veloce che spezza la regolarita
                // delle altre due. Cresce con la quota: la base e ancorata, balla la punta.
                float sway = (sin(height * 7.0 - t * 1.6) * 0.6
                            + sin(height * 13.0 - t * 2.7) * 0.25
                            + sin(height * 27.0 - t * 4.3) * 0.15)
                             * _Wobble * height;

                // 0 sull'asse della fiamma, 1 sul bordo: e questo il "calore".
                float offAxis = abs(IN.flame.x - sway) / max(halfWidth, 0.0001);
                float heat = (1.0 - offAxis) * (1.0 - height * 0.55);

                // Bande nette, non sfumature: e quello che la fa sembrare disegnata.
                float3 rgb = _EdgeColor.rgb;
                rgb = lerp(rgb, _MidColor.rgb, step(_Band1, heat));
                rgb = lerp(rgb, _HotColor.rgb, step(_Band2, heat));
                rgb = lerp(rgb, _CoreColor.rgb, step(_Band3, heat));

                // Fuori dalla sagoma non c'e niente. Il bordo tagliato netto, con appena un pelo
                // di sfumatura per non vedere la scaletta dei pixel.
                float alpha = smoothstep(0.0, _Softness, 1.0 - offAxis);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
