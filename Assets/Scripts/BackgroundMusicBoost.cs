using System.Collections;
using UnityEngine;

// Da mettere sul GameObject della traccia di sottofondo in scena (quello con
// l'AudioSource: "SetTekno"). Quando il power-up del sound system è attivo, alza per
// boostDuration secondi sia il volume della traccia sia i bassi, con una dissolvenza in
// entrata e in uscita (niente scatti bruschi di volume).
//
// I bassi NON vengono accentuati tagliando gli alti sulla traccia originale (quel
// AudioLowPassFilter ovattava l'intero mix invece di rinforzare solo il basso): qui invece
// viene creato in Awake un secondo AudioSource ("BassBoostLayer", figlio di questo
// GameObject) che riproduce lo stesso clip in parallelo, filtrato con un suo
// AudioLowPassFilter con cutoff molto basso così isola solo le frequenze basse. Durante il
// boost questo layer viene sincronizzato e portato a volume udibile e sommato sopra la
// traccia originale (che resta a piena banda, solo più alta di volume): il risultato è più
// basso percepito e più volume, senza ovattare gli alti. Alla scadenza del timer tutto torna
// ai valori originali.
[RequireComponent(typeof(AudioSource))]
public class BackgroundMusicBoost : MonoBehaviour
{
    [Header("Volume")]
    [SerializeField] private float boostedVolume = 0.5f; // N: volume della traccia originale durante il boost

    [Header("Bassi")]
    [Tooltip("Frequenza di taglio del layer bassi: isola le frequenze sotto questo valore, così il boost aggiunge solo bassi invece di ovattare tutto il mix.")]
    [SerializeField] private float bassCutoffFrequency = 200f;
    [Tooltip("Volume del layer bassi durante il boost, sommato sopra la traccia originale.")]
    [SerializeField] private float bassBoostVolume = 0.6f;

    [Header("Fade")]
    [Tooltip("Durata della dissolvenza in entrata/uscita del boost (secondi).")]
    [SerializeField] private float fadeDuration = 1f;

    private AudioSource audioSource; // traccia originale, resta sempre a piena banda
    private AudioSource bassSource; // duplicato dello stesso clip, filtrato per isolare solo i bassi e sommato sopra durante il boost

    private float baseVolume;
    private Coroutine boostRoutine;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        baseVolume = audioSource.volume;

        GameObject bassLayer = new GameObject("BassBoostLayer");
        bassLayer.transform.SetParent(transform, false);

        bassSource = bassLayer.AddComponent<AudioSource>();
        bassSource.clip = audioSource.clip;
        bassSource.loop = audioSource.loop;
        bassSource.playOnAwake = false;
        bassSource.volume = 0f; // silenzioso finché non parte il boost

        AudioLowPassFilter lowPassFilter = bassLayer.AddComponent<AudioLowPassFilter>();
        lowPassFilter.cutoffFrequency = bassCutoffFrequency;
    }

    // Chiamato da SoundSystem.Activate(): il timer si rinnova (non si somma alla durata
    // rimasta) se richiamato mentre il boost è già attivo, stesso comportamento di
    // PlayerSoundWavePowerUps. La dissolvenza riparte dal volume corrente (anche se è a metà
    // di un fade precedente), quindi non si sente uno scatto.
    public void Boost(float duration)
    {
        if (boostRoutine != null)
        {
            StopCoroutine(boostRoutine);
        }

        boostRoutine = StartCoroutine(BoostRoutine(duration));
    }

    private IEnumerator BoostRoutine(float duration)
    {
        if (audioSource.isPlaying)
        {
            // Riallinea il layer bassi alla posizione corrente della traccia principale,
            // altrimenti risuonerebbe fuori fase (lo stesso giro di basso in due momenti diversi).
            bassSource.time = audioSource.time;
            if (!bassSource.isPlaying)
            {
                bassSource.Play();
            }
        }

        // Il fade non può durare più della metà del boost, altrimenti su durate brevi
        // entrata e uscita si sovrapporrebbero e il volume non raggiungerebbe mai il picco.
        float fade = Mathf.Min(fadeDuration, duration / 2f);

        yield return FadeVolumes(audioSource.volume, boostedVolume, bassSource.volume, bassBoostVolume, fade);

        yield return new WaitForSeconds(Mathf.Max(0f, duration - fade * 2f));

        yield return FadeVolumes(audioSource.volume, baseVolume, bassSource.volume, 0f, fade);

        bassSource.Stop();
        boostRoutine = null;
    }

    private IEnumerator FadeVolumes(float fromVolume, float toVolume, float fromBassVolume, float toBassVolume, float duration)
    {
        if (duration <= 0f)
        {
            audioSource.volume = toVolume;
            bassSource.volume = toBassVolume;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float progress = t / duration;
            audioSource.volume = Mathf.Lerp(fromVolume, toVolume, progress);
            bassSource.volume = Mathf.Lerp(fromBassVolume, toBassVolume, progress);
            yield return null;
        }

        audioSource.volume = toVolume;
        bassSource.volume = toBassVolume;
    }
}
