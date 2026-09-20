using UnityEngine;

// Da mettere sul prefab dell'IdroSbirro (vedi IdroSbirro.prefab), insieme a EnemyHealth.
// Il modello (mesh) sta coricato a terra come il Camper, non in piedi: è un child
// separato ("Model", vedi il prefab) con una rotazione locale fissa di 90° su X, così
// resta a terra a prescindere da come ruota il GameObject padre. Il padre invece ruota
// SOLO sull'asse orizzontale (yaw): la mira si basa su transform.forward e deve restare sul
// piano orizzontale, la mesh inclinata è puramente estetica e non tocca quella logica.
// Non punta mai Console/DJ o SoundSystem, e non punta MAI il Player: appena ingaggia il
// Raver ingaggiabile più vicino (entro range) resta bloccato su di lui (AcquireNearestTarget
// non lo cambia più finché resta valido, vedi IsCurrentTargetEngageable) finché non lo
// uccide, solo allora passa al prossimo più vicino.
// Non spruzza più un getto continuo: ogni fireInterval secondi lancia una "gettata d'acqua"
// (vedi IdroSbirroWaterball, un componente indipendente creato a runtime — nessun prefab da
// collegare a mano) con una traiettoria ad arco che parte dal punto più alto del modello
// (vedi GetLaunchPosition) e atterra esattamente sul bersaglio agganciato al momento del
// lancio, infliggendogli damagePerHit: il colpo è garantito a fine volo, non dipende da un
// trigger fisico né dalla posizione del bersaglio all'atterraggio, quindi il Raver non
// riesce a schivarlo spostandosi (non ha comunque nessuna IA di schivata). Il volo è gestito
// dal proprio componente sulla propria GameObject, non da una Coroutine qui sopra: così
// continua fino in fondo anche se questo IdroSbirro muore mentre il colpo è ancora in aria
// (vedi IdroSbirroWaterball per i dettagli). Un arco che scavalca qualunque ostacolo fisico
// elimina il bisogno di gestire l'interruzione del getto (niente più raycast) e l'elusione
// da parte degli altri Sbirro (niente più traiettoria continua da cui scansarsi): era tutta
// complessità legata al vecchio getto a raggio continuo, qui semplicemente non serve più.
// A differenza degli altri Sbirro non si muove affatto: resta fisso nella posizione in cui
// è spawnato (vedi IdroSbirroSpawner), non insegue e non si ritira dal Player.
public class IdroSbirroHoseAttack : MonoBehaviour
{
    [Header("Mira")]
    [Tooltip("Distanza massima entro cui un Raver viene considerato ingaggiabile (usata solo per scegliere il bersaglio, vedi FindNearestEngageableRaver): l'arco scavalca gli ostacoli, ma non ha senso ingaggiare qualcuno dall'altra parte della mappa.")]
    [SerializeField] private float range = 24f;
    [Tooltip("Quanti metri \"pesa\" ogni altro nemico già impegnato con un Raver, quando ce n'è più di uno a portata di range: a parità di distanza si preferisce quello libero, anche se leggermente più lontano, invece che far convergere il fuoco di più IdroSbirro sullo stesso (stesso meccanismo di EnemyChase).")]
    [SerializeField] private float raverOccupancyPenalty = 2.5f;
    [Tooltip("Ogni quanti secondi, mentre è agganciato a un bersaglio vivo, lancia una nuova palla d'acqua.")]
    [SerializeField] private float fireInterval = 2.2f;

    [Header("Danno")]
    [Tooltip("Danno inflitto a un Raver colpito in pieno da una gettata d'acqua: tarato per ucciderlo (200 vita, vedi RaverHealth.maxHealth) in 4 colpi.")]
    [SerializeField] private float damagePerHit = 50f;

    [Header("Palla d'acqua")]
    [Tooltip("Velocità orizzontale della gettata: usata per calcolare quanto dura il volo in base alla distanza dal bersaglio (stesso schema di ParabolicProjectile). Più bassa di prima apposta: il volo dura di più, quindi lo spruzzo appare più lungo, a fronte di fireInterval più alto (spara meno spesso).")]
    [SerializeField] private float projectileSpeed = 9f;
    [SerializeField] private float arcHeight = 4f; // altezza raggiunta a metà tragitto
    [Tooltip("Spessore del fascio (\"colpo di laser\"), in unità di mondo, sulla testa; la coda è più sottile (vedi IdroSbirroWaterball).")]
    [SerializeField] private float beamWidth = 0.25f;
    [SerializeField] private Color waterColor = new Color(0.4f, 0.8f, 1f, 0.9f);

    [Header("Visuale")]
    [Tooltip("Punto da cui parte la palla d'acqua: di solito il child \"Model\" (il corpo visibile). Se lasciato vuoto, in Awake si cerca automaticamente un child chiamato \"Model\"; se non lo trova, usa transform. Il lancio parte dal punto più alto del suo Renderer (vedi GetLaunchPosition), non dal suo centro.")]
    [SerializeField] private Transform muzzle;

    private Transform target;
    private float fireTimer;

    void Awake()
    {
        if (muzzle == null)
        {
            muzzle = transform.Find("Model");
        }
        if (muzzle == null)
        {
            muzzle = transform;
        }

        AcquireNearestTarget();
    }

    void OnDestroy()
    {
        // Se questo IdroSbirro muore mentre ha un Raver come bersaglio, libera subito il suo
        // "posto" (vedi RaverHealth.EngagedCount), stesso motivo di EnemyChase.OnDestroy.
        if (target != null && target.TryGetComponent(out RaverHealth engagedRaver))
        {
            engagedRaver.Disengage();
        }
    }

    void Update()
    {
        // Se il boss è morto (vedi RoutState, attivato da RobosbirroHealth.Die()) gli Sbirro
        // rimasti fuggono invece di attaccare: l'IdroSbirro smette semplicemente di lanciare,
        // stesso spirito di EnemyChase/EnemyRangedAttack in fuga (lui però non si muove: resta
        // fermo e basta).
        if (RoutState.IsActive)
        {
            return;
        }

        if (!IsCurrentTargetEngageable())
        {
            AcquireNearestTarget();
        }

        if (target == null)
        {
            return;
        }

        fireTimer += Time.deltaTime;
        if (fireTimer >= fireInterval)
        {
            fireTimer = 0f;
            LaunchWaterball();
        }
    }

    // Il target resta bloccato sul Raver ingaggiato finché non va a terra (RaverHealth.IsDown):
    // niente ricalcolo periodico che lo farebbe saltare su un Raver più vicino a metà
    // combattimento.
    private bool IsCurrentTargetEngageable()
    {
        if (target == null)
        {
            return false;
        }

        RaverHealth raverHealth = target.GetComponent<RaverHealth>();
        return raverHealth != null && !raverHealth.IsDown;
    }

    // Mai il Player come bersaglio: solo il Raver ingaggiabile più vicino entro range, o
    // nessuno (resta fermo, non lancia nulla finché non ne trova uno). Un Raver che sta
    // facendo il giocoliere (vedi RaverJuggler) entro il proprio raggio di attrazione ha
    // priorità su tutto il resto, stesso trattamento di EnemyChase/EnemyRangedAttack.
    private void AcquireNearestTarget()
    {
        Transform attractor = RaverJuggler.FindNearestAttractor(transform.position);
        SetTarget(attractor ?? FindNearestEngageableRaver());
    }

    // Se il bersaglio cambia e quello vecchio o quello nuovo è un Raver, aggiorna il suo
    // EngagedCount (vedi RaverHealth.Engage/Disengage): stesso meccanismo di
    // EnemyChase.SetTarget, per non far convergere il fuoco di più IdroSbirro sullo stesso
    // Raver quando ce n'è un altro libero a portata.
    private void SetTarget(Transform newTarget)
    {
        if (newTarget != target)
        {
            if (target != null && target.TryGetComponent(out RaverHealth previousRaver))
            {
                previousRaver.Disengage();
            }
            if (newTarget != null && newTarget.TryGetComponent(out RaverHealth nextRaver))
            {
                nextRaver.Engage();
            }
        }

        target = newTarget;
    }

    // Un Raver a terra (RaverHealth.IsDown) è inerte: ignorato come bersaglio, stesso
    // trattamento di EnemyChase/EnemyRangedAttack.
    private Transform FindNearestEngageableRaver()
    {
        Transform nearest = null;
        float bestScore = float.MaxValue;
        float sqrRange = range * range;

        foreach (RaverHealth raver in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            if (raver.IsDown)
            {
                continue;
            }

            float sqrDistance = (raver.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance > sqrRange)
            {
                continue;
            }

            // Stesso meccanismo di EnemyChase.FindNearestEngageableRaver: a parità di distanza
            // un Raver già impegnato pesa come se fosse più lontano, per preferire quello libero.
            float score = Mathf.Sqrt(sqrDistance) + raver.EngagedCount * raverOccupancyPenalty;
            if (score < bestScore)
            {
                nearest = raver.transform;
                bestScore = score;
            }
        }

        return nearest;
    }

    // Crea un GameObject a sé (con IdroSbirroWaterball) invece di far girare una Coroutine
    // qui sopra: il volo prosegue fino in fondo anche se questo IdroSbirro viene distrutto
    // mentre il colpo è ancora in aria (una Coroutine su questo componente si fermerebbe a
    // metà, lasciando il colpo bloccato a mezz'aria per sempre — vedi IdroSbirroWaterball).
    private void LaunchWaterball()
    {
        RaverHealth raverHealth = target.GetComponent<RaverHealth>();
        if (raverHealth == null || raverHealth.IsDown)
        {
            return;
        }

        GameObject waterballObject = new GameObject("Waterball");
        IdroSbirroWaterball waterball = waterballObject.AddComponent<IdroSbirroWaterball>();
        waterball.Init(GetLaunchPosition(), target.position, projectileSpeed, arcHeight, damagePerHit, raverHealth, waterColor, beamWidth);
    }

    // Il punto più alto del modello (il child "Model", di solito), non il suo centro: preso
    // dai bounds del Renderer così resta corretto a qualunque scala/altezza venga dato al
    // prefab, senza valori fissi da ritarare a mano.
    private Vector3 GetLaunchPosition()
    {
        Renderer modelRenderer = muzzle.GetComponentInChildren<Renderer>();
        if (modelRenderer == null)
        {
            modelRenderer = GetComponentInChildren<Renderer>();
        }

        if (modelRenderer == null)
        {
            return muzzle.position;
        }

        return new Vector3(muzzle.position.x, modelRenderer.bounds.max.y, muzzle.position.z);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(waterColor.r, waterColor.g, waterColor.b, 0.5f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
