using System;
using UnityEngine;

/// Modalità BERSERK: onde rosse, doppia velocità, un anello in più per cassa, più grandi, più forti, dispersione.
/// Mettilo sul robot insieme a DualSpeakerEmitter. Tasto B per attivare/disattivare (o chiama SetBerserk).
[RequireComponent(typeof(DualSpeakerEmitter))]
public class BerserkMode : MonoBehaviour
{
    public static Action<float> OnShake;          // collegalo al tuo camera shake
    public static Action<bool> OnBerserkChanged;  // per UI, musica, post-processing (vignetta rossa ecc.)

    public SonicRingShot berserkRingPrefab;       // prefab con M_SonicRing_Berserk
    public KeyCode toggleKey = KeyCode.B;
    public float duration = 0f;                   // 0 = finché non lo spegni

    [Header("Moltiplicatori")]
    public float bpmMultiplier = 1f;   // resta a 175 BPM
    public int extraRings = 0;
    public float powerMultiplier = 1.6f;
    public float ringGap = 0.07f;
    public float aimJitter = 2.5f;
    public float beatShake = 0.2f;

    public Renderer[] tintRenderers;               // es. casse/occhi del robot
    [ColorUsage(true, true)] public Color tint = new Color(3f, 0.15f, 0.08f);

    public bool IsOn { get; private set; }
    DualSpeakerEmitter em;
    SonicRingShot normalPrefab; float normalBpm, normalPower, normalGap, normalJitter; int normalRings;
    float timer;
    MaterialPropertyBlock mpb;
    static readonly int EmissionID = Shader.PropertyToID("_EmissionColor");

    void Awake() { em = GetComponent<DualSpeakerEmitter>(); mpb = new MaterialPropertyBlock(); }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) SetBerserk(!IsOn);
        if (IsOn && duration > 0f && (timer -= Time.deltaTime) <= 0f) SetBerserk(false);
    }

    public void SetBerserk(bool on)
    {
        if (on == IsOn) return;
        IsOn = on;
        if (on)
        {
            normalPrefab = em.ringPrefab; normalBpm = em.bpm; normalPower = em.power; normalGap = em.ringGap; normalJitter = em.aimJitter; normalRings = em.ringsPerBeat;
            if (berserkRingPrefab) em.ringPrefab = berserkRingPrefab;
            em.bpm = normalBpm * bpmMultiplier;
            em.ringsPerBeat = normalRings + extraRings;
            em.power = normalPower * powerMultiplier;
            em.ringGap = ringGap;
            em.aimJitter = aimJitter;
            timer = duration;
        }
        else
        {
            em.ringPrefab = normalPrefab; em.bpm = normalBpm; em.power = normalPower; em.ringGap = normalGap; em.aimJitter = normalJitter; em.ringsPerBeat = normalRings;
        }
        foreach (var r in tintRenderers)
        {
            if (!r) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionID, on ? tint : Color.black);
            r.SetPropertyBlock(mpb);
        }
        OnBerserkChanged?.Invoke(on);
    }

    void LateUpdate()
    {
        if (IsOn && beatShake > 0f && em.JustFired) OnShake?.Invoke(beatShake);
    }
}
