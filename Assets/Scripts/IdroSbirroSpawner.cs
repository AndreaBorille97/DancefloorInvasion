using UnityEngine;

// Da associare a un GameObject "spawner" dedicato in scena, unico (stesso approccio di
// RobosbirroSpawner/CamperSpawner/RaverSpawner: se ce ne fossero più d'uno in scena,
// ognuno proverebbe a spawnare il proprio IdroSbirro). Compare una sola volta per
// partita, esattamente a spawnTime secondi dall'inizio (default 240 = 4 minuti): stesso
// schema deterministico del Robosbirro (vedi RobosbirroSpawner.spawnTime), non più a
// probabilità/tentativi ripetuti come prima.
// Spawna sempre esattamente sulla posizione di questo GameObject (transform.position), non
// in un punto casuale del pavimento: è lo spawner stesso, piazzato a mano in scena, a
// decidere dove comparirà l'IdroSbirro.
// La vittoria (vedi WinState, controllato da WinUI) scatta alla morte di questo IdroSbirro,
// non più dopo un tempo di sopravvivenza fisso (vedi SurvivalTimer, che ora serve solo per
// il conto alla rovescia in minimappa): da qui in Update, una volta spawnato, si limita a
// controllare se il riferimento è diventato null (Unity lo "finge" nullo dopo Destroy, cioè
// quando EnemyHealth.TakeDamage lo porta a zero vita), stesso schema con cui
// RobosbirroHealth.Die() innescava la sequenza di vittoria del vecchio boss.
public class IdroSbirroSpawner : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject idroSbirroPrefab;
    [Tooltip("Secondi di gioco trascorsi prima che compaia l'IdroSbirro (una sola volta per partita, stesso valore di SurvivalTimer.survivalDuration per restare in sincrono col conto alla rovescia in minimappa).")]
    [SerializeField] private float spawnTime = 240f;

    private float elapsedTime;
    private bool spawned;
    private GameObject idroSbirroInstance;

    void Update()
    {
        // Boss morto (vecchio Robosbirro): fine partita, stesso comportamento di EnemySpawner.
        if (RoutState.IsActive)
        {
            return;
        }

        if (!spawned)
        {
            elapsedTime += Time.deltaTime;
            if (elapsedTime >= spawnTime)
            {
                SpawnIdroSbirro();
            }
            return;
        }

        // Spawnato: appena il riferimento diventa null l'IdroSbirro è morto, vittoria.
        if (idroSbirroInstance == null)
        {
            WinState.Trigger();
        }
    }

    private void SpawnIdroSbirro()
    {
        if (idroSbirroPrefab == null)
        {
            return;
        }

        spawned = true;
        idroSbirroInstance = Instantiate(idroSbirroPrefab, transform.position, Quaternion.identity);
    }
}
