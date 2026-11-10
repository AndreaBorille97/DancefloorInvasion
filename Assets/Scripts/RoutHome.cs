using System.Collections.Generic;
using UnityEngine;

// La strada di casa di uno Sbirro in fuga (RoutState): i waypoint del percorso d'arrivo che
// aveva già superato, a ritroso, e infine il punto in cui è nato, fuori dall'arena. Usato da
// EnemyChase ed EnemyRangedAttack, che lo seguono una tappa dopo l'altra e all'ultima spariscono.
public class RoutHome
{
    private readonly List<Vector3> stops = new List<Vector3>();
    private readonly float reachRadius;
    private int index;

    // reached: quanti waypoint di arrivalPath aveva già raggiunto (tutti, se l'aveva finito).
    public RoutHome(Vector3 spawnPoint, Vector3[] arrivalPath, int reached, float reachRadius)
    {
        if (arrivalPath != null)
        {
            for (int i = Mathf.Min(reached, arrivalPath.Length) - 1; i >= 0; i--)
            {
                stops.Add(arrivalPath[i]);
            }
        }

        stops.Add(spawnPoint);
        this.reachRadius = reachRadius;
    }

    // La prossima tappa da raggiungere partendo da position; false quando è arrivato a casa.
    public bool TryGetNextStop(Vector3 position, out Vector3 stop)
    {
        while (index < stops.Count)
        {
            stop = stops[index];
            Vector3 toStop = stop - position;
            toStop.y = 0f;
            if (toStop.magnitude > reachRadius)
            {
                stop.y = position.y;
                return true;
            }

            index++;
        }

        stop = position;
        return false;
    }
}
