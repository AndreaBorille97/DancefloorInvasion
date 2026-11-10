using UnityEngine;
using UnityEngine.AI;

// Percorso sul NavMesh di EnemyNavMesh verso una meta, per chi si muove da sé col suo Rigidbody
// (EnemyChase, RaverChase): niente NavMeshAgent, solo la prossima tappa da raggiungere. Se la
// strada è libera (percorso di due soli punti) o il NavMesh non c'è, la tappa è la meta stessa e
// si va in linea retta come sempre. Se c'è un ostacolo in mezzo (casse, o IdroSbirro
// parcheggiato, che si ritaglia il buco da solo), è il prossimo angolo del percorso che lo
// aggira. L'ultimo tratto, dall'ultimo angolo alla meta, va comunque dritto sulla meta vera: il
// punto trovato sul NavMesh può differirne di poco (margini dell'agente).
public class NavPathFollower
{
    // Ogni quanto si ricalcola il percorso, o prima se la meta si è spostata parecchio: gli
    // ostacoli che si muovono (l'IdroSbirro che parcheggia) entrano così nel giro di poco.
    private const float RefreshInterval = 0.25f;
    // Quanto può spostarsi la meta prima di forzare un nuovo percorso senza aspettare il timer.
    private const float GoalMoveThreshold = 1f;
    // Distanza entro cui un angolo del percorso conta come raggiunto e si passa al successivo.
    private const float CornerReachRadius = 0.3f;
    // Quanto lontano dal NavMesh si cerca il punto più vicino, per partenza e meta: copre la
    // differenza d'altezza fra pivot e pavimento e l'eventuale sconfinamento dentro il margine
    // di un ostacolo.
    private const float SampleDistance = 3f;

    // Creato al primo ricalcolo e non qui: chi usa questa classe la crea come campo di un
    // MonoBehaviour, e Unity non permette di costruire un NavMeshPath da lì (eccezione, e il
    // componente non parte proprio).
    private NavMeshPath path;
    private readonly Vector3[] corners = new Vector3[32];
    private int cornerCount;
    private int cornerIndex;
    private float refreshTimer = RefreshInterval; // il primo passo calcola subito il percorso
    private Vector3 goal;

    // Gli angoli del percorso corrente, per i gizmo di debug.
    public int CornerCount => cornerCount;
    public Vector3 GetCorner(int index) => corners[index];

    // Da chiamare a ogni FixedUpdate in cui ci si muove verso destination.
    public Vector3 GetNextWaypoint(Vector3 position, Vector3 destination, out bool isPathCorner)
    {
        isPathCorner = false;

        refreshTimer += Time.fixedDeltaTime;
        if (refreshTimer >= RefreshInterval || (destination - goal).sqrMagnitude > GoalMoveThreshold * GoalMoveThreshold)
        {
            refreshTimer = 0f;
            goal = destination;
            Recalculate(position, destination);
        }

        while (cornerIndex < cornerCount - 1 && HorizontalDistance(position, corners[cornerIndex]) <= CornerReachRadius)
        {
            cornerIndex++;
        }

        if (cornerIndex >= cornerCount - 1)
        {
            return destination;
        }

        isPathCorner = true;
        return corners[cornerIndex];
    }

    // Il prossimo passo ricalcola da capo (es. dopo un tratto in cui non si è seguito il percorso).
    public void Invalidate()
    {
        cornerCount = 0;
        cornerIndex = 0;
        refreshTimer = RefreshInterval;
    }

    private void Recalculate(Vector3 position, Vector3 destination)
    {
        cornerCount = 0;
        cornerIndex = 0;

        if (!NavMesh.SamplePosition(position, out NavMeshHit start, SampleDistance, NavMesh.AllAreas)
            || !NavMesh.SamplePosition(destination, out NavMeshHit end, SampleDistance, NavMesh.AllAreas))
        {
            return;
        }

        if (path == null)
        {
            path = new NavMeshPath();
        }

        // Anche un percorso parziale (meta irraggiungibile) va bene: porta il più vicino possibile.
        if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
            || path.status == NavMeshPathStatus.PathInvalid)
        {
            return;
        }

        cornerCount = path.GetCornersNonAlloc(corners);
        cornerIndex = 1; // l'angolo 0 è la partenza
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
