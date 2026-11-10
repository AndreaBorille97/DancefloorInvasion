using UnityEngine;

// Le animazioni del modello dell'IdroSbirro (idrosbirro.fbx): ruote che rotolano, ruote davanti
// che sterzano a destra e sinistra, idrante sulla torretta che gira. Non usa le take esportate
// da Blender ma muove gli stessi pezzi da codice, perché devono seguire il mezzo: le ruote
// rotolano quanto avanza davvero, lo sterzo va dove gira il volante (IdroSbirro.SteerInput),
// l'idrante punta il Raver che sta bagnando. Le take sarebbero cicli fissi, scollegati da tutto
// questo, e in più l'export di Blender mette ogni azione su ogni oggetto (vedi il prop del sound
// system).
//
// I pezzi si cercano per nome nel modello: Ruota_AntDx/AntSx/PostDx/PostSx, Sterzo_AntDx/AntSx,
// Torretta (l'osso dell'idrante) e Torretta_end (la sua punta, da cui esce il getto). Gli assi
// non si leggono dal file, dove possono essere qualsiasi cosa: si ricavano dal mezzo stesso
// (asse delle ruote = fianco del mezzo, sterzo e torretta = verticale), presi una volta sola
// nella posa di partenza. Così il verso di rotolamento è sempre quello giusto.
public class IdroSbirroRig : MonoBehaviour
{
    private struct Wheel
    {
        public Transform transform;
        public Quaternion baseRotation;
        public Vector3 axle;     // asse di rotolamento, nel riferimento della ruota
        public Vector3 up;       // verticale del mezzo, nel riferimento della ruota
        public bool steersItself; // ruota davanti senza perno di sterzo sopra: sterza lei
    }

    private struct Pivot
    {
        public Transform transform;
        public Quaternion baseRotation;
        public Vector3 up;
    }

    [Tooltip("Gradi di sterzata delle ruote davanti col volante a fondo corsa.")]
    [SerializeField] private float maxSteerAngle = 30f;

    private Wheel[] wheels = new Wheel[0];
    private Pivot[] steerPivots = new Pivot[0];
    private Pivot turret;
    private bool hasTurret;
    private float wheelRadius = 0.4f;

    private float speed;
    private float steer;
    private float turretYaw;
    private float spinDegrees;

    // La punta dell'idrante, da cui parte il getto (vedi IdroSbirro). Se il modello non ce l'ha,
    // il punto più alto del mezzo.
    public Transform Muzzle { get; private set; }

    // Il perno attorno a cui gira l'idrante: la mira va presa da qui, non dal centro del mezzo,
    // altrimenti da vicino l'idrante punta di fianco al Raver.
    public Transform TurretPivot { get; private set; }

    // Di quanti gradi è girato l'ugello, nella posa di partenza, rispetto al davanti del mezzo
    // (0 = guarda avanti). È il centro dei ±45° dell'idrante.
    public float NozzleBaseYaw { get; private set; }

    void Awake()
    {
        var wheelList = new System.Collections.Generic.List<Wheel>();
        var pivotList = new System.Collections.Generic.List<Pivot>();

        foreach (string pivotName in new[] { "Sterzo_AntDx", "Sterzo_AntSx" })
        {
            Transform pivot = FindDeep(transform, pivotName);
            if (pivot != null)
            {
                pivotList.Add(MakePivot(pivot));
            }
        }

        foreach (string wheelName in new[] { "Ruota_AntDx", "Ruota_AntSx", "Ruota_PostDx", "Ruota_PostSx" })
        {
            Transform wheel = FindDeep(transform, wheelName);
            if (wheel == null)
            {
                continue;
            }

            bool front = wheelName.Contains("Ant");
            bool underPivot = pivotList.Exists(p => wheel.IsChildOf(p.transform));
            wheelList.Add(new Wheel
            {
                transform = wheel,
                baseRotation = wheel.localRotation,
                axle = Quaternion.Inverse(wheel.rotation) * transform.right,
                up = Quaternion.Inverse(wheel.rotation) * transform.up,
                steersItself = front && !underPivot,
            });

            Renderer wheelRenderer = wheel.GetComponent<Renderer>();
            if (wheelRenderer != null)
            {
                wheelRadius = Mathf.Max(0.05f, wheelRenderer.bounds.extents.y);
            }
        }

        wheels = wheelList.ToArray();
        steerPivots = pivotList.ToArray();

        Transform turretBone = FindDeep(transform, "Torretta");
        if (turretBone != null)
        {
            turret = MakePivot(turretBone);
            hasTurret = true;
        }

        TurretPivot = turretBone != null ? turretBone : transform;

        // La punta dell'ugello si cerca nella mesh, non nell'osso: l'osso Torretta -> Torretta_end
        // non va per forza dove va il tubo (era il motivo per cui il getto non usciva dall'idrante).
        // È il punto della torretta più lontano in pianta dal perno: lì si mette un figlio
        // dell'osso, che quindi gira con l'idrante, e da lì parte il getto.
        if (turretBone != null && TryFindNozzleTip(turretBone, out Vector3 tip))
        {
            var nozzle = new GameObject("Ugello").transform;
            nozzle.SetParent(turretBone, false);
            nozzle.position = tip;
            Muzzle = nozzle;
            NozzleBaseYaw = Vector3.SignedAngle(transform.forward, Vector3.ProjectOnPlane(tip - turretBone.position, transform.up), transform.up);
        }
        else
        {
            Muzzle = FindDeep(transform, "Torretta_end");
            if (Muzzle == null)
            {
                Muzzle = turretBone;
            }
        }

        LogSetup(turretBone);
    }

    // Una riga nel log con quello che ha trovato nel modello: se l'idrante non si muove, qui si
    // vede se manca l'osso o se la mesh non ci è agganciata.
    private void LogSetup(Transform turretBone)
    {
        string skins = "";
        foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            bool usesTurret = turretBone != null && System.Array.IndexOf(skin.bones, turretBone) >= 0;
            skins += $" {skin.name}(ossa {skin.bones.Length}, torretta {(usesTurret ? "sì" : "no")}, leggibile {(skin.sharedMesh != null && skin.sharedMesh.isReadable ? "sì" : "no")})";
        }
        Debug.Log($"[IdroSbirroRig] {name}: torretta {(turretBone != null ? turretBone.name : "NON TROVATA")}, ugello {(Muzzle != null ? Muzzle.name : "nessuno")} a {NozzleBaseYaw:0}°, ruote {wheels.Length}, perni sterzo {steerPivots.Length}, mesh skinnate:{(skins == "" ? " nessuna" : skins)}", this);
    }

    // Fra i vertici mossi (per più di metà) dall'osso della torretta, quello più lontano in
    // pianta dal perno, nella posa di partenza. La posizione si ricava dalla bindpose, quindi
    // non serve far girare la mesh skinnata.
    private bool TryFindNozzleTip(Transform turretBone, out Vector3 tip)
    {
        tip = Vector3.zero;
        float best = -1f;
        foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh mesh = skin.sharedMesh;
            int boneIndex = System.Array.IndexOf(skin.bones, turretBone);
            if (mesh == null || !mesh.isReadable || boneIndex < 0 || boneIndex >= mesh.bindposes.Length)
            {
                continue;
            }

            Matrix4x4 toWorld = turretBone.localToWorldMatrix * mesh.bindposes[boneIndex];
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < vertices.Length && i < weights.Length; i++)
            {
                if (WeightOf(weights[i], boneIndex) < 0.5f)
                {
                    continue;
                }

                Vector3 world = toWorld.MultiplyPoint3x4(vertices[i]);
                float reach = Vector3.ProjectOnPlane(world - turretBone.position, transform.up).sqrMagnitude;
                if (reach > best)
                {
                    best = reach;
                    tip = world;
                }
            }
        }
        return best > 0f;
    }

    private static float WeightOf(BoneWeight weight, int boneIndex)
    {
        float total = 0f;
        if (weight.boneIndex0 == boneIndex) total += weight.weight0;
        if (weight.boneIndex1 == boneIndex) total += weight.weight1;
        if (weight.boneIndex2 == boneIndex) total += weight.weight2;
        if (weight.boneIndex3 == boneIndex) total += weight.weight3;
        return total;
    }

    // Chiamati da IdroSbirro a ogni passo: velocità di marcia (unità al secondo), volante
    // (-1 tutto a sinistra, 1 tutto a destra) e di quanto è girato l'idrante rispetto alla posa
    // di partenza, in gradi (positivo = verso destra).
    public void SetMotion(float speed, float steer)
    {
        this.speed = speed;
        this.steer = steer;
    }

    public void SetTurretYaw(float degrees)
    {
        turretYaw = degrees;
    }

    void LateUpdate()
    {
        spinDegrees = Mathf.Repeat(spinDegrees + speed * Time.deltaTime / wheelRadius * Mathf.Rad2Deg, 360f);
        float steerDegrees = steer * maxSteerAngle;

        foreach (Pivot pivot in steerPivots)
        {
            pivot.transform.localRotation = pivot.baseRotation * Quaternion.AngleAxis(steerDegrees, pivot.up);
        }

        foreach (Wheel wheel in wheels)
        {
            Quaternion steering = wheel.steersItself ? Quaternion.AngleAxis(steerDegrees, wheel.up) : Quaternion.identity;
            wheel.transform.localRotation = wheel.baseRotation * steering * Quaternion.AngleAxis(spinDegrees, wheel.axle);
        }

        if (hasTurret)
        {
            turret.transform.localRotation = turret.baseRotation * Quaternion.AngleAxis(turretYaw, turret.up);
        }
    }

    private Pivot MakePivot(Transform target)
    {
        return new Pivot
        {
            transform = target,
            baseRotation = target.localRotation,
            up = Quaternion.Inverse(target.rotation) * transform.up,
        };
    }

    private static Transform FindDeep(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child;
            }
        }
        return null;
    }
}
