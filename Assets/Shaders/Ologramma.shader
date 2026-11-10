// Ologramma: la scia del potere khole (PlayerKhole) e la sfera della sua ammo. Stessa filosofia
// del fascio di luce (Assets/Shaders/FascioLuce.shader): tutto nel fragment shader, niente texture.
//
// ADDITIVO (Blend One One): somma luce, non copre mai quello che c'e dietro, e il nero vale zero.
// Quindi per farlo sparire basta portare _Fade a 0, senza bisogno di alpha: e quello che fa
// PlayerKhole per ogni copia della scia, con un MaterialPropertyBlock.
//
// L'aspetto da ologramma viene da tre cose: i bordi accesi e il centro quasi vuoto (si guarda quanto
// la superficie e di taglio rispetto alla camera, come i bordi del fascio ma al contrario), le
// righe orizzontali che scorrono verso l'alto, e un leggero sfarfallio.
//
// Colori sopra 1 = HDR: il Bloom li prende e la scia brilla.
Shader "DancefloorInvasion/Ologramma"
{
    Properties
    {
        [HDR] _Color ("Colore", Color) = (0.3, 1.6, 2.4, 1)
        _Fade ("Dissolvenza (1 = pieno, 0 = sparito)", Range(0, 1)) = 1
        _Core ("Quanto si vede il centro (0 = solo bordi)", Range(0, 1)) = 0.15
        _RimPower ("Finezza dei bordi, alto = bordi piu sottili", Range(0.5, 8)) = 2
        _LineDensity ("Righe per metro", Range(1, 80)) = 28
        _LineSpeed ("Velocita delle righe", Range(0, 5)) = 1.2
        _LineAmount ("Quanto si vedono le righe", Range(0, 1)) = 0.45
        _Flicker ("Sfarfallio", Range(0, 1)) = 0.15
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
            Name "OlogrammaForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Fade;
                float _Core;
                float _RimPower;
                float _LineDensity;
                float _LineSpeed;
                float _LineAmount;
                float _Flicker;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 toCamera = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // 1 sui bordi (superficie di taglio), 0 dove la si guarda di fronte.
                float rim = pow(1.0 - saturate(abs(dot(normal, toCamera))), _RimPower);
                float body = lerp(_Core, 1.0, rim);

                // Righe orizzontali in coordinate mondo: non si deformano con la mesh e scorrono in su.
                float lines = 0.5 + 0.5 * sin((input.positionWS.y * _LineDensity - _Time.y * _LineSpeed * 10.0) * PI);
                body *= 1.0 - _LineAmount * lines;

                float flicker = 1.0 - _Flicker * (0.5 + 0.5 * sin(_Time.y * 37.0) * sin(_Time.y * 23.0));

                return half4(_Color.rgb * body * flicker * _Fade, 1.0);
            }
            ENDHLSL
        }
    }
}
