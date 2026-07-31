using UnityEngine;

// Da mettere su un GameObject vuoto usato come prefab (vedi ParabolicProjectile.cloudPrefab).
// È la nuvoletta di fumo che la sfera dello Sbirro lascia a terra quando finisce la sua corsa
// (a contatto o ad atterraggio a vuoto): puramente visiva, non infligge alcun danno. Resta
// ferma per lifespan secondi, sfumando via via, poi sparisce.
public class SbirroCloud : MonoBehaviour
{
    [Header("Durata")]
    [SerializeField] private float lifespan = 3f; // quanto resta a terra prima di sparire

    [Header("Aspetto")]
    [SerializeField] private float radius = 2f; // raggio visivo della nuvola
    [SerializeField] private Color cloudColor = new Color(0.5f, 0.5f, 0.55f, 0.6f);

    private float elapsed;
    private Material visualMaterial;

    void Awake()
    {
        CreateVisual();
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
