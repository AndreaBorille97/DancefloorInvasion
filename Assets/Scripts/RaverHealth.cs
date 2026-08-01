using System.Collections;
using UnityEngine;

// Da mettere sul prefab del Raver (l'alleato generato raccogliendo un Raver Ammo, vedi
// RaverPickup). Ha un po' più vita di uno Sbirro (100). Viene attaccato dagli Sbirro
// (EnemyAttack) esattamente come il Player, e le onde sonore del Player (SoundWaveProjectile)
// gli restituiscono un po' di vita invece di fargli danno.
//
// Quando la vita arriva a zero il Raver non sparisce: entra in stato "a terra" (isDown),
// disabilita RaverChase/RaverAttack (resta fermo, non ingaggia più nessuno) e cambia colore,
// ma il collider resta attivo così l'onda sonora del Player continua a rilevarlo e può
// rianimarlo. La rianimazione richiede di riportarlo a vita piena (non basta una cura
// parziale): sotto quella soglia resta a terra.
public class RaverHealth : MonoBehaviour, IEnemyAttackTarget
{
    [Header("Vita")]
    [SerializeField] private float maxHealth = 200f;
    [Tooltip("Danno subito ad ogni attacco andato a segno di uno Sbirro.")]
    [SerializeField] private float damagePerHit = 25f;

    [Header("Stato a terra")]
    [Tooltip("Colore del Raver mentre è a terra, in attesa di essere rianimato dal cono del Player.")]
    [SerializeField] private Color downColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    [Tooltip("Di quanto si allontana dal punto di guardia quando va a terra: un corpo a terra esattamente sul presidio farebbe comunque da muro passivo (il collider resta solido) anche se la polizia lo ignora come bersaglio.")]
    [SerializeField] private float downDisplacementDistance = 1.5f;

    [Header("Evidenziazione onda")]
    [Tooltip("Quanto si schiarisce il colore del Raver quando viene curato da un'onda sonora (1 = nessun cambiamento).")]
    [SerializeField] private float healHighlightBrightness = 1.6f;
    [Tooltip("Durata del lampo di colore, in secondi.")]
    [SerializeField] private float healHighlightDuration = 0.15f;

    [Header("Colpo fendente Robosbirro")]
    [Tooltip("Colore del lampo quando il Raver viene colpito dal fendente ad area del Robosbirro (vedi RobosbirroMeleeAttack), per distinguerlo da un colpo Sbirro normale.")]
    [SerializeField] private Color meleeFlashColor = Color.black;
    [SerializeField] private float meleeFlashDuration = 0.15f;

    private float currentHealth;
    private bool isDown;
    private bool isInvulnerable;

    private RaverChase raverChase;
    private RaverAttack raverAttack;
    private Rigidbody rb;

    private Renderer raverRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Color baseColor;
    private Coroutine highlightRoutine;
    private Coroutine meleeFlashRoutine;

    // Usato da CamperVehicle/PlayerCamperSummon: un Raver a terra non può essere richiamato a pilotare il Camper.
    public bool IsDown => isDown;

    // Usato dalla minimappa (vedi MinimapUI) per mostrare quanta vita resta; 0 se a terra.
    public float HealthFraction => isDown ? 0f : Mathf.Clamp01(currentHealth / maxHealth);

    // Chiamato da CamperVehicle su Mount/Dismount: mentre pilota il Camper il Raver non
    // subisce alcun danno, a prescindere da chi/cosa lo colpisce.
    public void SetInvulnerable(bool value)
    {
        isInvulnerable = value;
    }

    void Awake()
    {
        currentHealth = maxHealth;

        rb = GetComponent<Rigidbody>();
        raverChase = GetComponent<RaverChase>();
        raverAttack = GetComponent<RaverAttack>();

        raverRenderer = GetComponent<Renderer>();
        if (raverRenderer != null)
        {
            propertyBlock = new MaterialPropertyBlock();
            baseColor = raverRenderer.sharedMaterial.color;
        }
    }

    public void TakeHit()
    {
        // Chiamato da EnemyAttack tramite IEnemyAttackTarget, stesso schema del Player.
        TakeDamage(damagePerHit);
    }

    public void TakeDamage(float amount)
    {
        if (isDown || isInvulnerable)
        {
            // Già a terra: nessun ulteriore danno, evita che la vita scenda sotto zero
            // all'infinito mentre aspetta di essere rianimato. Alla guida del Camper:
            // invulnerabile, vedi SetInvulnerable.
            return;
        }

        currentHealth -= amount;
        if (currentHealth <= 0f)
        {
            GoDown();
        }
    }

    // Chiamato dall'onda sonora del Player. Da vivo cura normalmente (capped a maxHealth).
    // Da terra invece accumula verso la rianimazione: serve riportarlo a vita piena, una
    // cura parziale non basta a farlo rialzare.
    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);

        if (isDown)
        {
            if (currentHealth >= maxHealth)
            {
                Reactivate();
            }
            return;
        }

        if (raverRenderer != null)
        {
            if (highlightRoutine != null)
            {
                StopCoroutine(highlightRoutine);
            }
            highlightRoutine = StartCoroutine(HealHighlightRoutine());
        }
    }

    // Invece di Destroy: disabilita movimento e attacco, tinge il Raver di downColor, ma
    // lascia il collider attivo così l'onda sonora del Player continua a rilevarlo (vedi
    // SoundWaveProjectile.HealRaversInRange) e può rianimarlo.
    private void GoDown()
    {
        isDown = true;
        currentHealth = 0f;

        if (raverChase != null)
        {
            raverChase.enabled = false;
        }
        if (raverAttack != null)
        {
            raverAttack.enabled = false;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // Si allontana un po' dal punto di guardia prima di bloccarsi, per non restare
            // esattamente sul presidio come un muro passivo (il collider resta solido anche
            // se la polizia lo ignora come bersaglio, vedi EnemyChase.FindNearestEngageableRaver).
            Vector3 awayDirection = raverChase != null
                ? rb.position - raverChase.GuardPosition
                : Vector3.zero;
            awayDirection.y = 0f;
            if (awayDirection.sqrMagnitude < 0.01f)
            {
                // Praticamente in piedi sul punto di guardia: nessuna direzione sensata da cui
                // allontanarsi, ne sceglie una a caso.
                Vector2 randomOffset = Random.insideUnitCircle;
                awayDirection = new Vector3(randomOffset.x, 0f, randomOffset.y);
            }
            rb.position += awayDirection.normalized * downDisplacementDistance;

            // Con RaverChase disabilitato nessuno azzera più la velocità ad ogni FixedUpdate:
            // senza bloccare anche X/Z (oltre a rotazione e Y, già frozen da RaverChase.Awake),
            // gli urti di chi lo tocca lo spingerebbero via per inerzia. A terra deve restare
            // esattamente dov'è (nella nuova posizione, spostata di poco).
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        if (raverRenderer != null)
        {
            raverRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", downColor); // URP/Lit
            propertyBlock.SetColor("_Color", downColor); // Standard/fallback
            raverRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    // Chiamato da Heal() quando la vita accumulata da terra raggiunge maxHealth: torna a
    // fare la guardia come un Raver normale (RaverChase.Awake è già passato, guardPosition
    // resta quella originale anche riabilitando il componente).
    private void Reactivate()
    {
        isDown = false;

        if (raverChase != null)
        {
            raverChase.enabled = true;
        }
        if (raverAttack != null)
        {
            raverAttack.enabled = true;
        }

        if (rb != null)
        {
            // Stesso vincolo impostato da RaverChase.Awake(): libera X/Z, tiene rotazione e Y frozen.
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        }

        if (raverRenderer != null)
        {
            propertyBlock.Clear();
            raverRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    // Lampo di colore che segnala visivamente il momento in cui l'onda sonora del Player cura
    // questo Raver (Heal viene chiamato una sola volta a onda, vedi SoundWaveProjectile.alreadyHealed).
    private IEnumerator HealHighlightRoutine()
    {
        Color highlighted = baseColor;
        highlighted.r *= healHighlightBrightness;
        highlighted.g *= healHighlightBrightness;
        highlighted.b *= healHighlightBrightness;

        raverRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", highlighted); // URP/Lit
        propertyBlock.SetColor("_Color", highlighted); // Standard/fallback
        raverRenderer.SetPropertyBlock(propertyBlock);

        yield return new WaitForSeconds(healHighlightDuration);

        propertyBlock.Clear();
        raverRenderer.SetPropertyBlock(propertyBlock);
        highlightRoutine = null;
    }

    // Chiamato da RobosbirroMeleeAttack quando il fendente ad area colpisce questo Raver.
    public void PlayMeleeFlash()
    {
        if (raverRenderer == null)
        {
            return;
        }

        if (meleeFlashRoutine != null)
        {
            StopCoroutine(meleeFlashRoutine);
        }
        meleeFlashRoutine = StartCoroutine(MeleeFlashRoutine());
    }

    private IEnumerator MeleeFlashRoutine()
    {
        raverRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", meleeFlashColor); // URP/Lit
        propertyBlock.SetColor("_Color", meleeFlashColor); // Standard/fallback
        raverRenderer.SetPropertyBlock(propertyBlock);

        yield return new WaitForSeconds(meleeFlashDuration);

        propertyBlock.Clear();
        raverRenderer.SetPropertyBlock(propertyBlock);
        meleeFlashRoutine = null;
    }

    // Usato da RaverDrugBuff: scala vita massima e attuale insieme, così la percentuale di
    // vita resta invariata (es. moltiplicando per 2 e poi per 0.5 si torna esattamente al valore di partenza).
    public void MultiplyHealth(float factor)
    {
        maxHealth *= factor;
        currentHealth *= factor;
    }
}
