using UnityEngine;

/// <summary>
/// Poi di fuoco — danza continua a contro-rotazione.
///
/// Un solo movimento rotatorio: è il PIANO del cerchio che scivola di continuo
/// fra tre orientamenti (frontale → sagittale → sopra la testa → frontale).
/// La testa completa un giro esattamente DURANTE ogni passaggio, quindi non
/// esiste mai un istante di sosta su un piano.
///
/// Lo script muove solo due Transform (le teste). Le fiamme sono ParticleSystem
/// figli di quelle teste, in Simulation Space = World: la scia nasce da sola.
///
/// Uso: attacca a un GameObject vuoto alla base del personaggio (Y a terra,
/// +Z davanti). Assegna headLeft / headRight, o lascia vuoto e premi
/// "Crea teste" dal menu contestuale.
/// </summary>
[DisallowMultipleComponent]
public class PoiFireDance : MonoBehaviour
{
    [Header("Velocità")]
    [Tooltip("Giri al secondo. Un giro = un passaggio di piano, quindi questo " +
             "parametro governa anche il ritmo della danza. Range utile 0.2 – 3.")]
    [Range(0.05f, 4f)]
    public float spinSpeed = 1.15f;

    [Tooltip("Moltiplicatore runtime, comodo per slow-motion o power-up. " +
             "La velocità effettiva è spinSpeed * speedMultiplier.")]
    public float speedMultiplier = 1f;

    [Header("Geometria")]
    [Tooltip("Lunghezza della catena: distanza pivot → testa, in metri.")]
    public float chainRadius = 0.95f;

    [Tooltip("Scala globale della figura. 1 = personaggio alto ~1.8 m.")]
    public float rigScale = 1f;

    [Header("Pausa / ripresa")]
    [Tooltip("Secondi che le teste impiegano a scendere a terra quando la danza si interrompe, " +
             "e a risalire quando riprende. Vedi Lower/Resume.")]
    public float lowerDuration = 1.2f;

    [Tooltip("Posizione di riposo di una testa quando è a terra, relativa alla base del " +
             "personaggio. La X viene specchiata per il lato destro. Y bassa ma non zero: " +
             "le fiamme devono posarsi sul terreno, non sprofondarci.")]
    public Vector3 restPosition = new Vector3(0.5f, 0.12f, 0.35f);

    [Header("Teste (i ParticleSystem vanno qui sotto)")]
    public Transform headLeft;
    public Transform headRight;

    [Header("Debug")]
    public bool drawGizmos = true;

    // ---- i tre orientamenti del piano -------------------------------------
    // normal: normale al piano del cerchio. pivot: posizione della mano.
    // side: -1 = sinistra, +1 = destra.
    static Vector3 PlaneNormal(int idx, int side)
    {
        switch (idx)
        {
            case 0: return new Vector3(0f, 0f, 1f);        // muro frontale
            case 1: return new Vector3(side, 0f, 0f);      // ruota sagittale
            default: return new Vector3(0f, 1f, 0f);       // elica sopra la testa
        }
    }

    static Vector3 PlanePivot(int idx, int side)
    {
        switch (idx)
        {
            case 0: return new Vector3(0.52f * side, 1.36f, 1.25f);
            case 1: return new Vector3(1.15f * side, 1.42f, 0.06f);
            default: return new Vector3(0.20f * side, 2.62f, 0.04f);
        }
    }

    const int PLANE_COUNT = 3;
    const float CYCLE_REVS = 3f;   // 1 giro per passaggio

    // stato: angolo della testa + frame del cerchio per lato
    float ang;
    struct Frame { public Vector3 n, u, v; }
    Frame frameL = new Frame { n = Vector3.forward, u = Vector3.right, v = Vector3.up };
    Frame frameR = new Frame { n = Vector3.forward, u = Vector3.right, v = Vector3.up };

    // 1 = danza piena, 0 = teste appoggiate a terra e ferme. Si muove fra i due estremi in
    // lowerDuration secondi, e governa insieme la discesa e il rallentamento: la rotazione
    // scala con lui, così il moto si spegne mentre le fiamme scendono invece di fermarsi di
    // colpo a mezz'aria.
    float performT = 1f;
    bool performing = true;

    public float CurrentSpeed { get { return spinSpeed * speedMultiplier; } }

    /// <summary>Interrompe la danza: le teste scendono dolcemente a terra e il moto si ferma.</summary>
    public void Lower() { performing = false; }

    /// <summary>Riprende la danza: le teste risalgono e il moto riparte da dove si era fermato.</summary>
    public void Resume() { performing = true; }

    /// <summary>Angolo corrente in giri — utile per sincronizzare audio o VFX.</summary>
    public float Revolutions { get { return ang / (Mathf.PI * 2f); } }

    void Reset()
    {
        if (headLeft == null || headRight == null) CreateHeads();
    }

    void Update()
    {
        performT = lowerDuration > 0f
            ? Mathf.MoveTowards(performT, performing ? 1f : 0f, Time.deltaTime / lowerDuration)
            : (performing ? 1f : 0f);

        // Il moto rallenta insieme alla discesa e a terra è fermo del tutto: senza questo
        // fattore le teste continuerebbero a girare appoggiate al suolo.
        ang += Time.deltaTime * CurrentSpeed * performT * Mathf.PI * 2f;

        int i, j; float k;
        Sequence(out i, out j, out k);

        Apply(-1, ref frameL, headLeft, i, j, k);
        Apply(+1, ref frameR, headRight, i, j, k);
    }

    // Fase continua, velocità costante: nessuna tenuta, nessun fermo-riparte.
    void Sequence(out int i, out int j, out float k)
    {
        float p = Mod(ang / (Mathf.PI * 2f), CYCLE_REVS) / CYCLE_REVS;
        float s = p * PLANE_COUNT;
        i = Mathf.Clamp((int)Mathf.Floor(s), 0, PLANE_COUNT - 1);
        j = (i + 1) % PLANE_COUNT;
        k = s - Mathf.Floor(s);
    }

    void Apply(int side, ref Frame f, Transform head, int i, int j, float k)
    {
        if (head == null) return;

        // 1. normale interpolata sulla geodetica fra i due orientamenti
        Vector3 target = Slerp(PlaneNormal(i, side), PlaneNormal(j, side), k);

        // 2. trasporto parallelo del frame: la testa non salta mai
        Transport(ref f, target);

        // 3. pivot su spline chiusa (Catmull-Rom periodica): niente gomiti
        Vector3 pivot = CatmullPivot(side, i, k) * rigScale;

        // 4. contro-rotazione: le due teste girano SEMPRE in verso opposto
        float a = (side > 0) ? Mathf.PI - ang : ang;
        float R = chainRadius * rigScale;
        Vector3 local = pivot + (Mathf.Cos(a) * f.u + Mathf.Sin(a) * f.v) * R;

        // 5. discesa a terra quando la danza è interrotta (vedi Lower/Resume). SmoothStep e non
        // una Lerp lineare: così la testa parte e arriva morbida, senza lo scatto secco che si
        // vedrebbe ai due estremi del movimento.
        if (performT < 1f)
        {
            Vector3 rest = new Vector3(restPosition.x * side, restPosition.y, restPosition.z) * rigScale;
            local = Vector3.Lerp(rest, local, Mathf.SmoothStep(0f, 1f, performT));
        }

        head.position = transform.TransformPoint(local);
    }

    static void Transport(ref Frame f, Vector3 target)
    {
        Vector3 axis = Vector3.Cross(f.n, target);
        float len = axis.magnitude;
        if (len > 1e-7f)
        {
            Vector3 ax = axis / len;
            float th = Mathf.Atan2(len, Vector3.Dot(f.n, target));
            Quaternion q = Quaternion.AngleAxis(th * Mathf.Rad2Deg, ax);
            f.u = q * f.u;
            f.v = q * f.v;
        }
        f.n = target;
        // ri-ortogonalizzazione: evita la deriva numerica su lunghe sessioni
        f.u = Vector3.Cross(f.v, f.n).normalized;
        f.v = Vector3.Cross(f.n, f.u);
    }

    static Vector3 CatmullPivot(int side, int i, float k)
    {
        Vector3 p0 = PlanePivot(Mod(i - 1, PLANE_COUNT), side);
        Vector3 p1 = PlanePivot(i, side);
        Vector3 p2 = PlanePivot(Mod(i + 1, PLANE_COUNT), side);
        Vector3 p3 = PlanePivot(Mod(i + 2, PLANE_COUNT), side);
        float k2 = k * k, k3 = k2 * k;
        return 0.5f * ((2f * p1)
            + (-p0 + p2) * k
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * k2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * k3);
    }

    static Vector3 Slerp(Vector3 a, Vector3 b, float t)
    {
        float d = Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f);
        if (d > 0.9995f) return Vector3.Lerp(a, b, t).normalized;
        float th = Mathf.Acos(d), s = 1f / Mathf.Sin(th);
        return (Mathf.Sin((1f - t) * th) * s) * a + (Mathf.Sin(t * th) * s) * b;
    }

    static float Mod(float a, float n) { return ((a % n) + n) % n; }
    static int Mod(int a, int n) { return ((a % n) + n) % n; }

    [ContextMenu("Crea teste")]
    public void CreateHeads()
    {
        if (headLeft == null)
        {
            headLeft = new GameObject("Poi_Head_L").transform;
            headLeft.SetParent(transform, false);
        }
        if (headRight == null)
        {
            headRight = new GameObject("Poi_Head_R").transform;
            headRight.SetParent(transform, false);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        int i, j; float k;
        if (!Application.isPlaying) { i = 0; j = 1; k = 0f; } else Sequence(out i, out j, out k);

        for (int s = -1; s <= 1; s += 2)
        {
            Frame f = new Frame { n = Vector3.forward, u = Vector3.right, v = Vector3.up };
            Transport(ref f, Slerp(PlaneNormal(i, s), PlaneNormal(j, s), k));
            Vector3 piv = CatmullPivot(s, i, k) * rigScale;
            float R = chainRadius * rigScale;

            Gizmos.color = new Color(1f, 0.48f, 0.09f, 0.55f);
            Vector3 prev = transform.TransformPoint(piv + f.u * R);
            for (int n = 1; n <= 48; n++)
            {
                float a = n / 48f * Mathf.PI * 2f;
                Vector3 cur = transform.TransformPoint(piv + (Mathf.Cos(a) * f.u + Mathf.Sin(a) * f.v) * R);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
            Gizmos.color = new Color(0.3f, 0.34f, 0.42f, 1f);
            Gizmos.DrawWireSphere(transform.TransformPoint(piv), 0.05f * rigScale);
        }
    }
}
