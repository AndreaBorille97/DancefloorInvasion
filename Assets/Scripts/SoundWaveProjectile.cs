using System.Collections.Generic;
using UnityEngine;

// Da mettere su un GameObject vuoto usato come prefab (vedi PlayerShooting.projectilePrefab).
// L'onda sonora non si muove: si espande come un cono (tipo shotgun) davanti al punto in cui
// è stata creata, nella direzione transform.forward, colpendo i nemici che entrano nell'area
// finché non arriva al raggio massimo.
public class SoundWaveProjectile : MonoBehaviour
{
    [Header("Espansione")]
    [SerializeField] private float maxRadius = 15f; // raggio massimo raggiunto dall'onda
    [SerializeField] private float expandSpeed = 20f; // velocità di espansione (unità al secondo)

    [Header("Forma del cono")]
    [SerializeField] private float coneAngle = 60f; // apertura totale del cono, in gradi, centrata su transform.forward
    [SerializeField] private int wedgeSegments = 16; // quanti triangoli usare per approssimare l'arco (più alto = più arrotondato)

    [Header("Danno")]
    [SerializeField] private float damage = 10f;
    [SerializeField] private LayerMask targetMask; // layer dei nemici colpibili (da impostare in Inspector)

    [Header("Cura Raver")]
    [Tooltip("Quanta vita restituisce ad ogni Raver colpito dall'onda (una sola volta per onda, come il danno ai nemici).")]
    [SerializeField] private float healAmount = 10f;

    [Header("Visuale")]
    [SerializeField] private Color waveColor = new Color(0.4f, 0.9f, 1f, 0.35f); // colore e trasparenza dell'area visibile
    [SerializeField] private float visualYOffset = 0.05f; // quanto sollevare il disco da terra, per evitare z-fighting col pavimento

    private float currentRadius;
    private readonly HashSet<Collider> alreadyHit = new HashSet<Collider>();
    private readonly HashSet<Collider> alreadyHealed = new HashSet<Collider>();

    private Transform visual;
    private MeshFilter visualMeshFilter;
    private MeshRenderer visualRenderer;
    private Material visualMaterial;

    void Awake()
    {
        CreateVisual();
    }

    // Chiamato da PlayerShooting subito dopo l'Instantiate, prima che Update parta:
    // applica i bonus attivi su PlayerSoundWavePowerUps (area = coneAngle, deep = maxRadius)
    // e ricostruisce la mesh dello spicchio, già creata in Awake con coneAngle di base.
    public void ApplyPowerUps(float bonusConeAngle, float bonusMaxRadius)
    {
        if (bonusConeAngle <= 0f && bonusMaxRadius <= 0f)
        {
            return;
        }

        coneAngle += bonusConeAngle;
        maxRadius += bonusMaxRadius;

        if (visualMeshFilter != null)
        {
            visualMeshFilter.mesh = BuildWedgeMesh(coneAngle, wedgeSegments);
        }
    }

    void Update()
    {
        currentRadius += expandSpeed * Time.deltaTime;

        DamageEnemiesInRange();
        HealRaversInRange();
        UpdateVisual();

        if (currentRadius >= maxRadius)
        {
            Destroy(gameObject);
        }
    }

    private void DamageEnemiesInRange()
    {
        // Il raggio di ricerca resta una sfera (Physics non ha un OverlapCone),
        // il filtro angolare sotto esclude chi è dentro il raggio ma fuori dal cono.
        Collider[] hits = Physics.OverlapSphere(transform.position, currentRadius, targetMask);
        foreach (var hit in hits)
        {
            if (alreadyHit.Contains(hit))
            {
                continue;
            }

            Vector3 toTarget = hit.transform.position - transform.position;
            toTarget.y = 0f; // confronto sul piano orizzontale, come EnemyChase

            if (Vector3.Angle(transform.forward, toTarget) > coneAngle * 0.5f)
            {
                continue; // dentro al raggio ma fuori dall'apertura del cono
            }

            alreadyHit.Add(hit);
            hit.GetComponent<EnemyHealth>()?.TakeDamage(damage);
        }
    }

    private void HealRaversInRange()
    {
        // Nessun LayerMask qui: il Raver non è detto sia sul layer nemici (targetMask),
        // quindi cerchiamo direttamente chi ha un RaverHealth, come fa CamperProjectile con EnemyHealth.
        Collider[] hits = Physics.OverlapSphere(transform.position, currentRadius);
        foreach (var hit in hits)
        {
            if (alreadyHealed.Contains(hit))
            {
                continue;
            }

            RaverHealth raverHealth = hit.GetComponent<RaverHealth>();
            if (raverHealth == null)
            {
                continue;
            }

            Vector3 toTarget = hit.transform.position - transform.position;
            toTarget.y = 0f;

            if (Vector3.Angle(transform.forward, toTarget) > coneAngle * 0.5f)
            {
                continue; // dentro al raggio ma fuori dall'apertura del cono
            }

            alreadyHealed.Add(hit);
            raverHealth.Heal(healAmount);
        }
    }

    // Crea come figlio uno spicchio piatto e semitrasparente che mostra a schermo (non solo
    // nella Scene view) l'area colpita dall'onda mentre si espande.
    private void CreateVisual()
    {
        GameObject visualObject = new GameObject("WaveVisual");
        visualObject.transform.SetParent(transform, false);
        visualObject.transform.localPosition = Vector3.up * visualYOffset;

        visualMeshFilter = visualObject.AddComponent<MeshFilter>();
        visualMeshFilter.mesh = BuildWedgeMesh(coneAngle, wedgeSegments);

        visualRenderer = visualObject.AddComponent<MeshRenderer>();
        visualRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        visualRenderer.receiveShadows = false;

        // Sprites/Default supporta la trasparenza via material.color ed è a due facce,
        // senza bisogno di configurare manualmente un materiale Standard Transparent.
        visualMaterial = new Material(Shader.Find("Sprites/Default"));
        visualRenderer.material = visualMaterial;

        visual = visualObject.transform;
        UpdateVisual();
    }

    // Genera uno spicchio (fan di triangoli) di raggio unitario sul piano XZ, centrato
    // sull'asse Z locale: verrà poi semplicemente scalato in base a currentRadius.
    private Mesh BuildWedgeMesh(float angleDegrees, int segments)
    {
        Mesh mesh = new Mesh { name = "SoundWaveWedge" };

        Vector3[] vertices = new Vector3[segments + 2];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero; // centro: il punto da cui parte l'onda

        float halfAngle = angleDegrees * 0.5f;
        float angleStep = angleDegrees / segments;

        for (int i = 0; i <= segments; i++)
        {
            float angle = -halfAngle + angleStep * i;
            vertices[i + 1] = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        }

        for (int i = 0; i < segments; i++)
        {
            int t = i * 3;
            triangles[t] = 0;
            triangles[t + 1] = i + 1;
            triangles[t + 2] = i + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private void UpdateVisual()
    {
        visual.localScale = new Vector3(currentRadius, 1f, currentRadius);

        // L'onda si affievolisce man mano che si avvicina al raggio massimo.
        float fade = 1f - Mathf.Clamp01(currentRadius / maxRadius);
        Color fadedColor = waveColor;
        fadedColor.a *= fade;
        visualMaterial.color = fadedColor;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(waveColor.r, waveColor.g, waveColor.b, 0.6f);

        float halfAngle = coneAngle * 0.5f;
        Vector3 previousPoint = transform.position + Quaternion.Euler(0f, -halfAngle, 0f) * transform.forward * maxRadius;

        Gizmos.DrawLine(transform.position, previousPoint);

        int gizmoSegments = Mathf.Max(wedgeSegments, 1);
        for (int i = 1; i <= gizmoSegments; i++)
        {
            float angle = -halfAngle + coneAngle / gizmoSegments * i;
            Vector3 point = transform.position + Quaternion.Euler(0f, angle, 0f) * transform.forward * maxRadius;
            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }

        Gizmos.DrawLine(transform.position, previousPoint);
    }
}
