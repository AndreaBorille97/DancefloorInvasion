using System.Collections.Generic;
using UnityEngine;

// Percorso obbligatorio che un nemico segue dallo spawn fino "dentro" l'arena, invece di
// puntare dritto al bersaglio (che lo farebbe entrare dal punto più vicino, spesso proprio
// sopra/dietro l'obiettivo). Da mettere su un GameObject vuoto in scena: i waypoint sono i
// suoi figli diretti, in ordine di gerarchia (oppure la lista esplicita qui sotto, se
// riempita). Un EnemySpawner punta a un EnemyPath e lo passa a ogni nemico che genera
// (vedi EnemySpawner.path e EnemyChase/EnemyRangedAttack.SetPath): raggiunto l'ultimo
// waypoint il nemico torna all'AI normale.
public class EnemyPath : MonoBehaviour
{
    [Tooltip("Lascia vuoto per usare tutti i figli diretti di questo oggetto come waypoint, in ordine di gerarchia. Riempi solo se vuoi un ordine diverso o waypoint che stanno altrove.")]
    [SerializeField] private List<Transform> waypoints = new List<Transform>();

    [Tooltip("Distanza entro cui un waypoint è considerato raggiunto e il nemico punta al successivo. Se il percorso passa stretto tra ostacoli, tienilo basso; se i nemici sembrano 'rimbalzare' sui waypoint, alzalo.")]
    [SerializeField] private float waypointReachRadius = 1.5f;

    [Header("Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(1f, 0.3f, 0.2f);
    [SerializeField] private float gizmoWaypointSize = 0.5f;

    public float WaypointReachRadius => waypointReachRadius;

    public int Count => ResolveWaypoints().Count;

    // Posizioni mondo dei waypoint, catturate al momento della chiamata (i waypoint non si
    // muovono a runtime, quindi il nemico può lavorare su una copia statica).
    public Vector3[] GetWorldPoints()
    {
        List<Transform> points = ResolveWaypoints();
        Vector3[] result = new Vector3[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            result[i] = points[i].position;
        }

        return result;
    }

    private List<Transform> ResolveWaypoints()
    {
        List<Transform> resolved = new List<Transform>();

        if (waypoints != null && waypoints.Count > 0)
        {
            foreach (Transform waypoint in waypoints)
            {
                if (waypoint != null)
                {
                    resolved.Add(waypoint);
                }
            }

            return resolved;
        }

        foreach (Transform child in transform)
        {
            resolved.Add(child);
        }

        return resolved;
    }

    void OnDrawGizmos()
    {
        List<Transform> points = ResolveWaypoints();
        if (points.Count == 0)
        {
            return;
        }

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.4f);
        Gizmos.DrawLine(transform.position, points[0].position);

        Gizmos.color = gizmoColor;
        for (int i = 0; i < points.Count; i++)
        {
            Gizmos.DrawWireSphere(points[i].position, gizmoWaypointSize);
            if (i > 0)
            {
                Gizmos.DrawLine(points[i - 1].position, points[i].position);
            }

#if UNITY_EDITOR
            UnityEditor.Handles.Label(points[i].position + Vector3.up * (gizmoWaypointSize + 0.3f), i.ToString());
#endif
        }
    }
}
