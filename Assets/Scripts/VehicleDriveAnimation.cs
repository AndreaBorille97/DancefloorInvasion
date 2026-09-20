using System.Collections.Generic;
using UnityEngine;

// Fa girare e sterzare le ruote del mezzo mentre è guidato. Sta sullo stesso GameObject del
// CamperVehicle (se non c'è, se lo aggiunge lui da solo) e cerca le ruote fra i figli.
//
// Le ruote NON vengono mosse con le clip dell'FBX. In MEZZO.fbx le take sono una per ruota e
// mettono insieme rotolamento e sterzata nella stessa clip ("Ruota_+X_-Y_v1_rotolamento_e_sterzo"):
// riprodurle in loop fa sterzare ciclicamente tutte e quattro le ruote, anche quelle dietro, che
// è proprio quello che non deve succedere. Qui le ruote si muovono invece per conto nostro:
//
//   - rotolamento: tutte e quattro, alla velocità a cui il mezzo si sta davvero spostando (il
//     raggio della ruota è misurato dal modello, quindi non c'è niente da tarare: se il mezzo
//     va a 8 unità al secondo, le ruote fanno i giri che servono a coprirle);
//   - sterzata: solo le due davanti, di un angolo proporzionale a quanto il mezzo sta curvando.
//
// Niente di tutto questo si scrive a mano, si misura dal modello, e il punto di partenza è una
// cosa sola: di una ruota, l'asse sottile è l'assale. Da lì viene tutto il resto —
//
//   - l'asse su cui rotola ogni ruota (l'assale, appunto, col verso portato tutto da una parte,
//     perché le ruote dei due lati sono speculari e altrimenti girerebbero in senso opposto);
//   - l'asse lungo del mezzo, che è perpendicolare agli assali: è quello che dice quali ruote
//     stanno davanti, ed è più affidabile dell'ingombro della carrozzeria, che su un mezzo
//     tozzo può benissimo risultare più largo che lungo (vedi MeasureLengthAxis: è il motivo
//     per cui è CamperVehicle a chiedere a noi da che parte guarda il muso).
//
// Il segnale di sterzata è invece ricavato dalla rotazione reale del mezzo (quanti gradi al
// secondo sta ruotando attorno all'asse verticale) invece che da CamperVehicle: così lo script
// non dipende da come è implementata la guida e continuerebbe a funzionare anche se cambiasse.
//
// A mezzo parcheggiato non si muove niente: accende e spegne CamperVehicle su Mount/Dismount.
public class VehicleDriveAnimation : MonoBehaviour
{
    [Header("Ruote")]
    [Tooltip("Pezzo di nome (maiuscole/minuscole indifferenti) degli oggetti del modello che sono ruote: nell'FBX del mezzo si chiamano Ruota_+X_+Y, Ruota_-X_+Y... All'avvio lo script stampa in Console quante ne ha trovate, quali ha preso per anteriori e dove stanno.")]
    [SerializeField] private string wheelNameFilter = "Ruota";
    [Tooltip("Spunta solo se sterzano le ruote sbagliate: scambia anteriori e posteriori. È un sì/no, non un valore da tarare.")]
    [SerializeField] private bool invertFrontWheels;

    [Header("Rotolamento")]
    [Tooltip("Moltiplicatore sui giri delle ruote. A 1 rotolano esattamente quanto il mezzo si sposta; sopra girano più in fretta di quanto servirebbe, che su un mezzo così è quello che rende l'idea della corsa.")]
    [SerializeField] private float rollSpeed = 1.8f;

    [Header("Vibrazione motore")]
    [Tooltip("Di quanto trema la carrozzeria mentre il mezzo è in moto, in unità: tienilo piccolo, deve leggersi come un motore acceso e non come una strada sconnessa. A 0 non trema affatto.")]
    [SerializeField] private float engineShake = 0.07f;
    [Tooltip("Di quanti gradi la carrozzeria becchegga e rolla mentre vibra. Su una carrozzeria lunga si nota molto più dello spostamento: un grado alle estremità vale parecchi centimetri.")]
    [SerializeField] private float engineTilt = 0.6f;
    [Tooltip("Quanto è veloce il tremolio. Alto = vibrazione da motore; basso = dondolio da sospensioni.")]
    [SerializeField] private float engineShakeFrequency = 22f;

    [Header("Sterzata")]
    [Tooltip("Angolo massimo di sterzo delle ruote anteriori, in gradi: su un mezzo vero sono 25-35.")]
    [SerializeField] private float maxSteerAngle = 30f;
    [Tooltip("Quanti gradi al secondo di rotazione del mezzo corrispondono allo sterzo a fondo corsa. In curva piena il Camper gira sui 50 gradi al secondo (vedi RaverChase: vehicleMaxSpeed e minTurnRadius fanno il raggio, minVehicleThrottle il rallentamento), quindi a 50 le ruote arrivano a fondo corsa proprio nelle curve più strette.")]
    [SerializeField] private float maxTurnRate = 50f;
    [Tooltip("Secondi che le ruote impiegano a raggiungere l'angolo voluto. Senza questo smorzamento seguirebbero la rotazione fotogramma per fotogramma e vibrerebbero.")]
    [SerializeField] private float steerSmoothing = 0.15f;

    // Tutto quello che serve sapere di una ruota, misurato una volta sola all'avvio.
    private class Wheel
    {
        public Transform transform;
        public Quaternion restRotation; // rotazione di partenza: rotolamento e sterzo si sommano a questa
        public Vector3 restPosition;    // posizione di partenza: da qui si contrasta il tremolio della carrozzeria
        public Renderer renderer;       // la mesh della ruota: da lei si misurano assale e raggio
        public Vector3 worldAxle;       // assale in coordinate mondo, nella posa parcheggiata
        public Vector3 axle;            // lo stesso assale nello spazio del padre, col verso portato a destra del mezzo
        public Vector3 up;              // asse attorno a cui sterza, sempre nello spazio del padre
        public float radius;            // raggio misurato dal modello: serve a legare i giri allo spostamento
        public float steerCorrection; // gradi che raddrizzano la ruota: nel modello può essere già sterzata
        public bool isFront;
    }

    private readonly List<Wheel> wheels = new List<Wheel>();

    private bool isDriving;
    private float lastYaw;
    private Vector3 lastPosition;
    private float steer;      // -1 tutto a sinistra, +1 tutto a destra, 0 dritto
    private float rollAngle;  // gradi percorsi dal rotolamento, si accumulano

    // La carrozzeria: l'oggetto che porta tutto il modello, ruote comprese. È lui a tremare, e le
    // ruote si tengono ferme contrastando il suo scostamento (vedi ApplyEngineShake).
    private Transform body;
    private Vector3 bodyRestPosition;
    private Quaternion bodyRestRotation;

    // Asse lungo del mezzo ricavato dalle ruote, orizzontale e normalizzato, oppure Vector3.zero
    // se di ruote non ce ne sono. Lo chiama CamperVehicle in Awake per decidere da che parte
    // guarda il muso: gli assali dicono dov'è il fianco del mezzo molto meglio di quanto lo dica
    // il riquadro d'ingombro della carrozzeria. noseGuess serve solo a scegliere fra i due versi
    // possibili (muso o coda), e resta comunque ribaltabile con invertNose.
    public Vector3 MeasureLengthAxis(Vector3 noseGuess)
    {
        CollectWheelTransforms();
        if (wheels.Count == 0)
        {
            return Vector3.zero;
        }

        // Gli assali dei due lati puntano in direzioni opposte (le ruote sono speculari): prima di
        // sommarli vanno portati tutti dalla stessa parte, altrimenti si annullano a vicenda.
        Vector3 reference = Vector3.zero;
        Vector3 lateral = Vector3.zero;
        foreach (Wheel wheel in wheels)
        {
            if (wheel.worldAxle == Vector3.zero)
            {
                continue; // ruota senza mesh propria: non ha voce in capitolo su dov'è il fianco
            }

            if (reference == Vector3.zero)
            {
                reference = wheel.worldAxle;
            }

            lateral += Vector3.Dot(wheel.worldAxle, reference) < 0f ? -wheel.worldAxle : wheel.worldAxle;
        }

        lateral.y = 0f;
        if (lateral.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        Vector3 lengthAxis = Vector3.Cross(lateral.normalized, Vector3.up).normalized;

        // Raffinamento con le posizioni delle ruote. L'asse ricavato dagli assali è approssimativo
        // (le mesh delle ruote non sono perfettamente allineate fra loro), ma basta e avanza per
        // capire quali ruote stanno su un assale e quali sull'altro: e a quel punto la congiungente
        // fra il centro di un assale e quello dell'altro è l'asse lungo esatto, misurato su quattro
        // punti invece che sull'orientamento di quattro mesh.
        Vector3 refined = RefineWithWheelPositions(lengthAxis);
        if (refined != Vector3.zero)
        {
            lengthAxis = refined;
        }

        return Vector3.Dot(lengthAxis, noseGuess) < 0f ? -lengthAxis : lengthAxis;
    }

    // Congiungente fra i centri dei due assali, cioè l'asse lungo del mezzo misurato sulle
    // posizioni delle ruote. Torna Vector3.zero se le ruote finiscono tutte da una parte sola (con
    // due sole ruote, per esempio): in quel caso vale l'asse di partenza.
    private Vector3 RefineWithWheelPositions(Vector3 axis)
    {
        float average = 0f;
        foreach (Wheel wheel in wheels)
        {
            average += Vector3.Dot(wheel.transform.position, axis);
        }
        average /= Mathf.Max(1, wheels.Count);

        Vector3 frontCenter = Vector3.zero;
        Vector3 rearCenter = Vector3.zero;
        int frontCount = 0;
        int rearCount = 0;

        foreach (Wheel wheel in wheels)
        {
            if (Vector3.Dot(wheel.transform.position, axis) > average)
            {
                frontCenter += wheel.transform.position;
                frontCount++;
            }
            else
            {
                rearCenter += wheel.transform.position;
                rearCount++;
            }
        }

        if (frontCount == 0 || rearCount == 0)
        {
            return Vector3.zero;
        }

        Vector3 refined = frontCenter / frontCount - rearCenter / rearCount;
        refined.y = 0f;
        return refined.sqrMagnitude > 0.0001f ? refined.normalized : Vector3.zero;
    }

    // Col muso ormai deciso (CamperVehicle.ParkedNose, invertNose compreso): divide le ruote in
    // anteriori e posteriori e prepara gli assi su cui girano. Va chiamata dopo MeasureLengthAxis.
    public void SetUpWheels(Vector3 nose)
    {
        nose.y = 0f;
        nose = nose.sqrMagnitude > 0.0001f ? nose.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, nose).normalized;

        // Davanti o dietro: quanto ogni ruota è avanti lungo il muso rispetto alla media di tutte.
        float averageAlongNose = 0f;
        foreach (Wheel wheel in wheels)
        {
            averageAlongNose += Vector3.Dot(wheel.transform.position, nose);
        }
        averageAlongNose /= Mathf.Max(1, wheels.Count);

        // Raggio di ripiego per le ruote senza mesh propria: quello medio delle altre.
        float measuredRadius = 0f;
        int measuredCount = 0;
        foreach (Wheel wheel in wheels)
        {
            if (wheel.radius > 0f)
            {
                measuredRadius += wheel.radius;
                measuredCount++;
            }
        }
        float fallbackRadius = measuredCount > 0 ? measuredRadius / measuredCount : 0.5f;

        string report = "";
        foreach (Wheel wheel in wheels)
        {
            float alongNose = Vector3.Dot(wheel.transform.position, nose) - averageAlongNose;
            wheel.isFront = (alongNose > 0f) != invertFrontWheels;

            if (wheel.radius <= 0f)
            {
                wheel.radius = fallbackRadius;
            }

            // L'assale definitivo: fra i tre assi della ruota, quello che nel mondo punta più
            // vicino alla trasversale del mezzo. La misura "asse sottile" fatta sulla singola mesh
            // serve solo a capire dov'è quella trasversale (è un voto di maggioranza fra tutte le
            // ruote, vedi right qui sopra): se su una ruota quella misura sbaglia — mesh che si
            // porta dietro il parafango, ruota non simmetrica, pivot storto — qui viene comunque
            // rimessa in riga. Senza, quella ruota girerebbe attorno a un asse qualsiasi, magari
            // verticale, e a occhio sembra semplicemente che non giri.
            Vector3 axle = NearestAxis(wheel.renderer != null ? wheel.renderer.transform : wheel.transform, right);

            Transform parent = wheel.transform.parent != null ? wheel.transform.parent : wheel.transform;
            wheel.axle = parent.InverseTransformDirection(axle).normalized;
            wheel.up = parent.InverseTransformDirection(Vector3.up).normalized;

            // Quanto la ruota è già sterzata nel modello. Su questo mezzo le due anteriori sono
            // esportate girate di 22° e 30°: sommandoci sopra lo sterzo, oscillavano fra dritte e
            // tutte girate da una parte sola invece che simmetricamente attorno al dritto. Siccome
            // lo sterzo lo applichiamo attorno alla verticale, per raddrizzarle basta una costante
            // in gradi, che è proprio l'angolo fra l'assale e la trasversale del mezzo.
            Vector3 flatAxle = Vector3.ProjectOnPlane(axle, Vector3.up);
            wheel.steerCorrection = flatAxle.sqrMagnitude > 0.0001f
                ? Vector3.SignedAngle(flatAxle.normalized, right, Vector3.up)
                : 0f;

            // Riga di diagnosi per ogni ruota: dove sta (positivo = davanti / a destra), di quanto
            // era già girata nel modello, e se il suo assale è stato riscelto perché la misura sulla
            // mesh dava un asse improbabile. Con questi numeri si capisce da fuori, senza aprire il
            // modello, quale ruota ha una geometria che inganna la misura.
            float alongRight = Vector3.Dot(wheel.transform.position - transform.position, right);
            bool axleChanged = wheel.worldAxle != Vector3.zero &&
                               Vector3.Angle(axle, wheel.worldAxle) > 1f &&
                               Vector3.Angle(axle, -wheel.worldAxle) > 1f;
            report += (report.Length > 0 ? " | " : "") +
                      $"{wheel.transform.name}: {(wheel.isFront ? "ANTERIORE" : "posteriore")}, " +
                      $"muso {alongNose:0.0}, lato {alongRight:0.0}, raggio {wheel.radius:0.00}, " +
                      $"raddrizzata di {wheel.steerCorrection:0}°{(axleChanged ? ", assale RISCELTO" : "")}, " +
                      $"mesh {(wheel.renderer != null ? wheel.renderer.name : "nessuna")}";
        }

        FindBody();

        Debug.Log($"[VehicleDriveAnimation] {name}: {wheels.Count} ruote, carrozzeria che trema: " +
                  $"{(body != null ? body.name : "NESSUNA")}. {report}", this);
    }

    void Start()
    {
        lastYaw = transform.eulerAngles.y;
        lastPosition = transform.position;
    }

    // Chiamato da CamperVehicle su Mount/Dismount.
    public void SetDriving(bool driving)
    {
        isDriving = driving;

        if (!driving)
        {
            // Le ruote restano ferme dove sono, ma lo sterzo torna dritto: un mezzo parcheggiato
            // con le ruote girate sembra abbandonato in curva. E a motore spento non si trema:
            // carrozzeria e ruote tornano esattamente dove stavano.
            steer = 0f;
            ApplyWheelRotations();
            RestoreRestPositions();
            return;
        }

        lastYaw = transform.eulerAngles.y;
        lastPosition = transform.position;
    }

    void Update()
    {
        if (!isDriving || wheels.Count == 0)
        {
            return;
        }

        // Quanto si è spostato davvero il mezzo in questo fotogramma: legare i giri delle ruote
        // allo spostamento reale, invece che a una velocità dichiarata, evita l'effetto pattinata
        // quando il mezzo rallenta o resta fermo contro un ostacolo.
        Vector3 position = transform.position;
        float distance = Vector3.Distance(position, lastPosition);
        lastPosition = position;

        // Velocità di rotazione attorno all'asse verticale, in gradi al secondo: positiva a
        // destra, negativa a sinistra. DeltaAngle gestisce da sé il salto 360 -> 0.
        float yaw = transform.eulerAngles.y;
        float turnRate = Mathf.DeltaAngle(lastYaw, yaw) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastYaw = yaw;

        float target = Mathf.Clamp(turnRate / Mathf.Max(1f, maxTurnRate), -1f, 1f);
        steer = steerSmoothing > 0f
            ? Mathf.MoveTowards(steer, target, Time.deltaTime / steerSmoothing)
            : target;

        // Giri = spazio percorso diviso la circonferenza della ruota. Il raggio è quello misurato
        // sul modello, quindi le ruote girano esattamente quanto servirebbe a coprire quel tratto.
        rollAngle += distance * rollSpeed * 360f / Mathf.Max(0.01f, AverageCircumference());

        ApplyWheelRotations();
        ApplyEngineShake();
    }

    private void ApplyWheelRotations()
    {
        foreach (Wheel wheel in wheels)
        {
            if (wheel.transform == null)
            {
                continue;
            }

            // Prima il rotolamento attorno all'assale, poi lo sterzo attorno alla verticale: in
            // quest'ordine lo sterzo si porta dietro anche l'assale, come su un mezzo vero. Le
            // posteriori restano dritte.
            // steerCorrection raddrizza la posa di riposo (vedi SetUpWheels), lo sterzo vero si
            // somma a quella: così le anteriori partono dritte e girano di tanto da una parte
            // quanto dall'altra.
            float steerAngle = wheel.steerCorrection + (wheel.isFront ? steer * maxSteerAngle : 0f);
            wheel.transform.localRotation =
                Quaternion.AngleAxis(steerAngle, wheel.up)
                * Quaternion.AngleAxis(rollAngle, wheel.axle)
                * wheel.restRotation;
        }
    }

    // Il tremolio da motore acceso. Trema tutta la carrozzeria, ruote comprese perché le stanno
    // attaccate: alle ruote si toglie poi lo stesso scostamento, così restano piantate a terra
    // mentre il mezzo vibra sopra di loro — che è quello che si vede su un mezzo fermo col motore
    // acceso, e l'unico modo di farlo senza andare a cercare pezzo per pezzo cos'è carrozzeria.
    //
    // Il tremito viene dal rumore di Perlin e non da valori a caso ogni fotogramma: a caso
    // sfarfalla e cambia faccia con gli fps, mentre il rumore dà una vibrazione continua che a
    // rallentatore resta una vibrazione.
    private void ApplyEngineShake()
    {
        if (body == null)
        {
            return;
        }

        float t = Time.time * engineShakeFrequency;

        // Assi sfalsati nel rumore, altrimenti leggerebbero lo stesso valore e il mezzo
        // oscillerebbe lungo una diagonale invece di vibrare.
        Vector3 offset = new Vector3(
            (Mathf.PerlinNoise(t, 0f) - 0.5f) * 0.6f,
            (Mathf.PerlinNoise(t, 7.3f) - 0.5f),
            (Mathf.PerlinNoise(t, 19.1f) - 0.5f) * 0.6f) * (engineShake * 2f);

        body.localPosition = bodyRestPosition + body.parent.InverseTransformVector(offset);

        // Beccheggio e rollio: un grado all'estremità di una carrozzeria lunga sposta molto più di
        // quanto sposti il tremolio in sé, ed è la parte che si legge davvero come "motore acceso".
        // Alle ruote non serve contrastarlo: stando vicine al centro, un angolo così piccolo le
        // muove di frazioni di centimetro.
        body.localRotation = bodyRestRotation * Quaternion.Euler(
            (Mathf.PerlinNoise(t, 31.7f) - 0.5f) * 2f * engineTilt,
            0f,
            (Mathf.PerlinNoise(t, 43.3f) - 0.5f) * 2f * engineTilt);

        foreach (Wheel wheel in wheels)
        {
            if (wheel.transform == null || wheel.transform.parent == null)
            {
                continue;
            }

            wheel.transform.localPosition = wheel.restPosition - wheel.transform.parent.InverseTransformVector(offset);
        }
    }

    private void RestoreRestPositions()
    {
        if (body != null)
        {
            body.localPosition = bodyRestPosition;
            body.localRotation = bodyRestRotation;
        }

        foreach (Wheel wheel in wheels)
        {
            if (wheel.transform != null)
            {
                wheel.transform.localPosition = wheel.restPosition;
            }
        }
    }

    // La carrozzeria è l'oggetto sotto al Camper che contiene il modello: si trova risalendo da
    // una ruota fino al figlio diretto del Camper. Se una ruota fosse già lei quel figlio non ci
    // sarebbe niente da far tremare senza far tremare anche la ruota, e si lascia perdere.
    private void FindBody()
    {
        body = null;
        if (wheels.Count == 0)
        {
            return;
        }

        Transform candidate = wheels[0].transform;
        while (candidate.parent != null && candidate.parent != transform)
        {
            candidate = candidate.parent;
        }

        if (candidate == wheels[0].transform)
        {
            return;
        }

        body = candidate;
        bodyRestPosition = body.localPosition;
        bodyRestRotation = body.localRotation;
    }

    private float AverageCircumference()
    {
        float total = 0f;
        foreach (Wheel wheel in wheels)
        {
            total += 2f * Mathf.PI * wheel.radius;
        }

        return wheels.Count > 0 ? total / wheels.Count : 1f;
    }

    // Trova le ruote fra i figli e misura di ognuna assale e raggio. Davanti/dietro no: per quello
    // serve il muso, che si decide dopo (vedi SetUpWheels).
    private void CollectWheelTransforms()
    {
        wheels.Clear();

        if (string.IsNullOrEmpty(wheelNameFilter))
        {
            return;
        }

        string filter = wheelNameFilter.ToLowerInvariant();
        List<Transform> matches = new List<Transform>();
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != transform && child.name.ToLowerInvariant().Contains(filter))
            {
                matches.Add(child);
            }
        }

        foreach (Transform match in matches)
        {
            // Se anche un antenato ha un nome da ruota, la ruota vera è l'antenato: muovere tutti
            // e due vorrebbe dire ruotare due volte la stessa ruota, una dentro l'altra.
            if (HasAncestorIn(match, matches))
            {
                continue;
            }

            // Senza mesh propria l'assale e il raggio non si possono misurare, ma la ruota va
            // tenuta lo stesso: assale e raggio glieli diamo dalle altre (vedi SetUpWheels).
            Renderer renderer = match.GetComponentInChildren<Renderer>(true);

            wheels.Add(new Wheel
            {
                transform = match,
                restRotation = match.localRotation,
                restPosition = match.localPosition,
                renderer = renderer,
                worldAxle = renderer != null ? MeasureAxle(renderer) : Vector3.zero,
                radius = renderer != null ? MeasureRadius(renderer) : 0f,
            });
        }

        if (wheels.Count == 0)
        {
            Debug.LogWarning($"[VehicleDriveAnimation] {name}: nessuna ruota trovata col filtro \"{wheelNameFilter}\". " +
                             "Controlla come si chiamano gli oggetti dentro il modello del mezzo.", this);
        }
    }

    // Fra i tre assi di un oggetto, quello che nel mondo punta più vicino a reference, e nel suo
    // stesso verso: le ruote dei due lati sono speculari, quindi senza il verso comune girerebbero
    // una in avanti e una all'indietro.
    private static Vector3 NearestAxis(Transform source, Vector3 reference)
    {
        Vector3[] axes = { source.right, source.up, source.forward };

        Vector3 best = axes[0];
        float bestDot = 0f;
        foreach (Vector3 axis in axes)
        {
            float dot = Vector3.Dot(axis.normalized, reference);
            if (Mathf.Abs(dot) > Mathf.Abs(bestDot))
            {
                bestDot = dot;
                best = axis.normalized;
            }
        }

        return bestDot < 0f ? -best : best;
    }

    private static bool HasAncestorIn(Transform candidate, List<Transform> others)
    {
        for (Transform parent = candidate.parent; parent != null; parent = parent.parent)
        {
            if (others.Contains(parent))
            {
                return true;
            }
        }

        return false;
    }

    // L'assale di una ruota è il suo asse sottile: una ruota è un disco, quindi fra i tre lati del
    // suo ingombro quello corto è per forza lo spessore, e lo spessore sta sull'assale. È una
    // misura molto più solida che cercare "l'asse più orizzontale" o fidarsi di come è orientato
    // il modello, che può essere qualsiasi cosa.
    private static Vector3 MeasureAxle(Renderer renderer)
    {
        Vector3 size = LocalSize(renderer);

        if (size.x <= size.y && size.x <= size.z)
        {
            return renderer.transform.right.normalized;
        }

        if (size.y <= size.x && size.y <= size.z)
        {
            return renderer.transform.up.normalized;
        }

        return renderer.transform.forward.normalized;
    }

    // Raggio della ruota: il lato più lungo del suo ingombro è il diametro, visto che gli altri due
    // sono il diametro stesso e lo spessore.
    private static float MeasureRadius(Renderer renderer)
    {
        Vector3 size = LocalSize(renderer);
        return Mathf.Max(0.01f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f);
    }

    // Ingombro della mesh nei suoi assi, riportato alla scala del mondo: localBounds e non bounds,
    // perché bounds è allineato al mondo e su una ruota messa di traverso non direbbe niente sui
    // suoi lati veri.
    private static Vector3 LocalSize(Renderer renderer)
    {
        Vector3 size = renderer.localBounds.size;
        Vector3 scale = renderer.transform.lossyScale;
        return new Vector3(
            size.x * Mathf.Abs(scale.x),
            size.y * Mathf.Abs(scale.y),
            size.z * Mathf.Abs(scale.z));
    }
}
