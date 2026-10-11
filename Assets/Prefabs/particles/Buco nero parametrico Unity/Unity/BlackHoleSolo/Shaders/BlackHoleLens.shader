// Lente gravitazionale ray-traced (disco avvolto, ombra, anello, flash e onda d'urto).
// URP: attiva "Opaque Texture" nell'URP Asset. Built-in: usa GrabPass.
// Richiede BlackHoleLens.cginc nella stessa cartella. I valori sono guidati da BlackHole.cs.
Shader "BlackHole/Lens"
{
    Properties
    {
        [HDR] _RingColor ("Colore anello", Color) = (1, 0.83, 0.6, 1)
        [HDR] _FlashColor ("Colore flash/onda", Color) = (4, 3.2, 2.6, 1)
        [HDR] _HotColor ("Disco interno", Color) = (1, 0.95, 0.84, 1)
        [HDR] _MidColor ("Disco medio", Color) = (1, 0.63, 0.25, 1)
        [HDR] _CoolColor ("Disco esterno", Color) = (0.63, 0.16, 0.03, 1)
        _Doppler ("Doppler", Range(0, 1)) = 0.6
        _NoiseScale ("Scala turbolenza", Range(0.5, 10)) = 4
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off ZTest LEqual Cull Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertLens
            #pragma fragment fragLens
            sampler2D _CameraOpaqueTexture;
            #define BH_TEX _CameraOpaqueTexture
            #define BH_SCREEN(p) ComputeScreenPos(p)
            #include "BlackHoleLens.cginc"
            ENDCG
        }
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_GrabTexture" }
        ZWrite Off ZTest LEqual Cull Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertLens
            #pragma fragment fragLens
            sampler2D _GrabTexture;
            #define BH_TEX _GrabTexture
            #define BH_SCREEN(p) ComputeGrabScreenPos(p)
            #include "BlackHoleLens.cginc"
            ENDCG
        }
    }
}
