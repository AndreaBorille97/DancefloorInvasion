using System.Collections;
using UnityEngine;

// Da mettere sul prefab del Raver (l'alleato generato raccogliendo un Raver Ammo, vedi
// RaverPickup). Ha un po' più vita di uno Sbirro (100). Viene attaccato dagli Sbirro
// (EnemyAttack) esattamente come il Player, e le onde sonore del Player (SoundWaveProjectile)
// gli restituiscono un po' di vita invece di fargli danno.
public class RaverHealth : MonoBehaviour, IEnemyAttackTarget
{
    [Header("Vita")]
    [SerializeField] private float maxHealth = 130f; // un po' più di uno Sbirro
    [Tooltip("Danno subito ad ogni attacco andato a segno di uno Sbirro.")]
    [SerializeField] private float damagePerHit = 25f;

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

    private Renderer raverRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Color baseColor;
    private Coroutine highlightRoutine;
    private Coroutine meleeFlashRoutine;

    void Awake()
    {
        currentHealth = maxHealth;

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
        currentHealth -= amount;
        if (currentHealth <= 0f)
        {
            Destroy(gameObject);
        }
    }

    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);

        if (raverRenderer != null)
        {
            if (highlightRoutine != null)
            {
                StopCoroutine(highlightRoutine);
            }
            highlightRoutine = StartCoroutine(HealHighlightRoutine());
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
