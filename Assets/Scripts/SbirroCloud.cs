using System.Collections.Generic;
using UnityEngine;

// Da mettere su un GameObject vuoto usato come prefab (vedi ParabolicProjectile.cloudPrefab).
// È la nuvoletta che la sfera dello Sbirro lascia a terra quando finisce la sua corsa
// (colpo diretto o atterraggio a vuoto): resta ferma per lifespan secondi, sfumando via
// via, e infligge un colpo (come un attacco corpo a corpo di Sbirro, vedi IEnemyAttackTarget)
// a chi la tocca — una sola volta per bersaglio, anche se ci resta dentro più a lungo.
// Console/DJ e SoundSystem sono ignorati anche se la toccano: il fumogeno non deve mai
// far danno a loro (vedi ParabolicProjectile/EnemyRangedAttack).
[RequireComponent(typeof(SphereCollider))]
public class SbirroCloud : MonoBehaviour
{
    [Header("Durata")]
    [SerializeField] private float lifespan = 3f; // quanto resta a terra prima di sparire

    [Header("Aspetto")]
    [SerializeField] private float radius = 2f; // raggio della nuvola, sia visivo sia del trigger
    [SerializeField] private Color cloudColor = new Color(0.5f, 0.5f, 0.55f, 0.6f);

    private readonly HashSet<Collider> alreadyHit = new HashSet<Collider>();
    private float elapsed;
    private Material visualMaterial;

    void Awake()
    {
        SphereCollider trigger = GetComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = radius;

        CreateVisual();
    }

    // Tiene il collider sincronizzato con radius anche in editor (non solo a runtime via
    // Awake): senza, modificando radius nell'Inspector il valore serializzato del
    // SphereCollider resta quello vecchio finché non si va in Play, disallineando hitbox
    // e raggio visivo nel frattempo (successo con SbirroNuvola: radius 1.5, m_Radius 2).
    void OnValidate()
    {
        SphereCollider trigger = GetComponent<SphereCollider>();
        if (trigger != null)
        {
            trigger.radius = radius;
        }
    }

    void Update()
    {
        elapsed += Time.deltaTime;

        // Sfuma via via che si avvicina alla scadenza, stesso trattamento di SoundWaveProjectile.
        float fade = 1f - Mathf.Clamp01(elapsed / lifespan);
        Color fadedColor = cloudColor;
        fadedColor.a *= fade;
        visualMaterial.color = fadedColor;

        if (elapsed >= lifespan)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (alreadyHit.Contains(other))
        {
            return;
        }

        if (other.GetComponentInParent<DJConsoleHealth>() != null || other.GetComponentInParent<SoundSystem>() != null)
        {
            return;
        }

        IEnemyAttackTarget target = other.GetComponentInParent<IEnemyAttackTarget>();
        if (target == null)
        {
            return;
        }

        alreadyHit.Add(other);
        target.TakeHit();
    }

    // Genera una sfera appiattita e semitrasparente, senza bisogno di un mesh/materiale
    // dedicato in Assets: stessa tecnica (Sprites/Default) usata da SoundWaveProjectile.
    private void CreateVisual()
    {
        GameObject visualObject = new GameObject("CloudVisual");
        visualObject.transform.SetParent(transform, false);
        visualObject.transform.localScale = new Vector3(radius * 2f, radius * 1.2f, radius * 2f);

        MeshFilter meshFilter = visualObject.AddComponent<MeshFilter>();
        meshFilter.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

        MeshRenderer meshRenderer = visualObject.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        visualMaterial = new Material(Shader.Find("Sprites/Default"));
        visualMaterial.color = cloudColor;
        meshRenderer.material = visualMaterial;
    }
}
