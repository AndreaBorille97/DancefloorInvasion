using System.Collections;
using UnityEngine;

// Da mettere sul prefab del Raver, insieme a RaverHealth/RaverAttack/RaverChase. Attivato
// da DrugPowerUpPickup su ogni Raver già in scena al momento della raccolta: raddoppia per
// buffDuration secondi vita, danno e velocità di movimento (e applica il material "drug" come
// indicatore visivo), poi riporta tutto ai valori/material base.
[RequireComponent(typeof(RaverHealth))]
[RequireComponent(typeof(RaverAttack))]
[RequireComponent(typeof(RaverChase))]
[RequireComponent(typeof(Renderer))]
public class RaverDrugBuff : MonoBehaviour
{
    private const float Multiplier = 2f;

    [Header("Visuale")]
    [SerializeField] private Material drugMaterial; // material da applicare finché il buff è attivo (vedi Assets/Material/Drug.mat)

    private RaverHealth raverHealth;
    private RaverAttack raverAttack;
    private RaverChase raverChase;
    private Renderer raverRenderer;
    private Material baseMaterial;

    private Coroutine buffRoutine;

    void Awake()
    {
        raverHealth = GetComponent<RaverHealth>();
        raverAttack = GetComponent<RaverAttack>();
        raverChase = GetComponent<RaverChase>();
        raverRenderer = GetComponent<Renderer>();
        baseMaterial = raverRenderer.sharedMaterial;
    }

    public void Activate(float duration)
    {
        bool alreadyActive = buffRoutine != null;
        if (alreadyActive)
        {
            StopCoroutine(buffRoutine);
        }

        // Se già attivo, rinnova solo la durata: non raddoppia di nuovo statistiche già raddoppiate.
        buffRoutine = StartCoroutine(BuffRoutine(duration, applyMultiplier: !alreadyActive));
    }

    private IEnumerator BuffRoutine(float duration, bool applyMultiplier)
    {
        if (applyMultiplier)
        {
            raverHealth.MultiplyHealth(Multiplier);
            raverAttack.MultiplyDamage(Multiplier);
            raverChase.MultiplyMoveSpeed(Multiplier);

            if (drugMaterial != null)
            {
                raverRenderer.sharedMaterial = drugMaterial;
            }
        }

        yield return new WaitForSeconds(duration);

        raverHealth.MultiplyHealth(1f / Multiplier);
        raverAttack.MultiplyDamage(1f / Multiplier);
        raverChase.MultiplyMoveSpeed(1f / Multiplier);
        raverRenderer.sharedMaterial = baseMaterial;
        buffRoutine = null;
    }
}
