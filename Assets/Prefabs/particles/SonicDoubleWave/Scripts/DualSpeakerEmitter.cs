using UnityEngine;

/// Sul robot. Spara a ritmo (default 175 BPM) un'onda da ciascuna cassa sulla schiena.
/// speakerLeft / speakerRight: Transform figli posizionati sulle casse, asse Z (blu) = direzione di sparo.
public class DualSpeakerEmitter : MonoBehaviour
{
    public SonicRingShot ringPrefab;
    public Transform speakerLeft, speakerRight;

    [Header("Ritmo")]
    public bool autoFire = true;
    public float bpm = 175f;
    [Tooltip("Anelli per cassa a ogni battuta")] [Range(1, 6)] public int ringsPerBeat = 2;
    public float ringGap = 0.07f;
    public float power = 1.1f;
    [Tooltip("Gradi di dispersione casuale per anello")] public float aimJitter = 0f;

    [Header("Feedback (opzionali)")]
    public Transform[] speakerMembranes;   // scalati a ogni battuta
    public float membranePunch = 1.6f;
    public AudioSource beatAudio;          // suono per battuta

    float nextBeat, pulse;
    public bool JustFired { get; private set; }

    void OnEnable() => nextBeat = Time.time;

    void Update()
    {
        JustFired = false;
        if (autoFire || Input.GetMouseButton(0))
        {
            while (Time.time >= nextBeat) { Fire(); nextBeat += 60f / Mathf.Max(1f, bpm); }
        }
        else nextBeat = Mathf.Max(nextBeat, Time.time);

        pulse = Mathf.Max(0f, pulse - Time.deltaTime * 6f);
        if (speakerMembranes != null)
            foreach (var m in speakerMembranes) if (m) m.localScale = Vector3.one * (1f + pulse * membranePunch);
    }

    public void Fire()
    {
        if (!ringPrefab) return;
        if (speakerLeft) FireFrom(speakerLeft);
        if (speakerRight) FireFrom(speakerRight);
        pulse = 1f; JustFired = true;
        if (beatAudio) beatAudio.Play();
    }

    void FireFrom(Transform from)
    {
        for (int i = 0; i < ringsPerBeat; i++)
        {
            float k = ringsPerBeat > 1 ? i / (float)(ringsPerBeat - 1) : 0f;
            var rot = from.rotation * Quaternion.Euler(0f, Random.Range(-aimJitter, aimJitter), 0f);
            var r = Instantiate(ringPrefab, from.position, rot);
            r.endRadius *= 1f - k * 0.2f;
            r.range *= 1f - k * 0.2f;
            r.Init(power * 0.75f * (1f - k * 0.45f), i * ringGap);
        }
    }
}
