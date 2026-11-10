using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TwinStick
{
    /// <summary>
    /// Anima il modello del Player (PLAYER.fbx, montato da Tools/Player/Monta il modello animato,
    /// vedi PlayerModelSetup) con UNA sola corsa in avanti e l'Idle:
    ///  - le gambe (il modello) si girano dove il Player si sta spostando davvero;
    ///  - se si corre "contro" la mira (oltre ~105°) le gambe si girano dall'altra parte e la
    ///    corsa viene riprodotta al contrario (corsa all'indietro);
    ///  - il busto (Spine/Spine1/Spine2) ruota verso la mira: le casse sono attaccate alla testa,
    ///    quindi guardano sempre dove partono le onde (PlayerShooting spara da Muzzle_L/Muzzle_R);
    ///  - da fermo il personaggio si gira verso la mira e suona l'Idle.
    /// Movimento e mira restano di PlayerMovement: la mira è il +Z del Player (PlayerMovement lo
    /// gira verso lo stick destro), il movimento è lo spostamento misurato, non lo stick, così
    /// piantato sul posto (Hoffman) o contro un muro il modello sta fermo invece di correre a vuoto.
    /// Le animazioni sono gestite con i Playables: non serve un Animator Controller.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class TwinStickLocomotion : MonoBehaviour
    {
        [Header("Riferimenti")]
        [Tooltip("Animator del modello (figlio). Non serve un controller.")]
        public Animator animator;
        [Tooltip("Transform del modello che viene ruotato. Vuoto = transform dell'Animator.")]
        public Transform model;
        public AnimationClip runClip;
        public AnimationClip idleClip;

        [Header("Animazione")]
        [Tooltip("Velocità a cui la corsa viene riprodotta a 1x, in altezze delle anche al secondo (così vale per qualunque taglia del modello). " +
                 "5 = i piedi non pattinano: in Blender il piede d'appoggio arretra a 3.35 m/s con le anche alte 0.67 m. Più basso = gambe più veloci.")]
        public float runSpeedPerHipHeight = 5f;
        public Vector2 playbackSpeedRange = new Vector2(0.75f, 1.9f);
        [Tooltip("Sotto questa velocità si sfuma verso l'Idle.")]
        public float fullRunAtSpeed = 1.5f;
        public float blendTime = 0.15f;
        [Tooltip("Smussatura della velocità misurata (secondi): assorbe i salti dello scatto.")]
        public float velocitySmoothing = 0.06f;

        [Header("Orientamento gambe")]
        public float legsTurnSpeed = 720f;
        [Tooltip("Angolo tra movimento e mira oltre il quale si corre all'indietro.")]
        public float backpedalAngle = 105f;
        public float backpedalHysteresis = 15f;
        [Tooltip("Di quanti gradi le gambe si inclinano verso la mira mentre corre.")]
        public float legsLeanTowardAim = 25f;
        [Tooltip("Da fermo, le gambe si girano solo se il busto supera questo angolo.")]
        public float idleTurnThreshold = 45f;

        [Header("Busto")]
        public float maxTorsoTwist = 100f;
        public float torsoTurnSpeed = 1080f;
        [Tooltip("Ossa del busto ruotate verso la mira. Vuoto = Spine, Spine1, Spine2 trovate in automatico.")]
        public Transform[] torsoBones;
        public float[] torsoWeights = { 0.25f, 0.35f, 0.4f };

        public bool IsBackpedaling { get; private set; }

        PlayerMovement movement;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable runPlayable, idlePlayable;
        float runTime, idleTime, runWeight, legsYaw, torsoTwist;
        float runClipSpeed; // m/s a cui la corsa va a 1x, da runSpeedPerHipHeight
        Quaternion modelFront; // rotazione locale del modello che lo fa guardare sul +Z del Player
        Vector3 velocity, velocityRef;
        bool idleTurning;

        Vector3 AimDirection
        {
            get
            {
                Vector3 f = transform.forward; f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            }
        }

        void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!model && animator) model = animator.transform;
        }

        void Start()
        {
            if (!animator || !runClip)
            {
                Debug.LogError("[TwinStickLocomotion] Assegna Animator e Run Clip (Tools/Player/Monta il modello animato).", this);
                enabled = false;
                return;
            }

            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false; // il Player lo muove PlayerMovement, le clip sono sul posto
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            modelFront = model.localRotation;
            if (torsoBones == null || torsoBones.Length == 0) FindTorsoBones();

            // Prima che l'Animator parta lo scheletro è in posa di legatura: le anche sono alla
            // loro altezza da fermo sopra i piedi (il modello ha l'origine fra i piedi).
            Transform hips = FindBone(model, "Hips");
            float hipHeight = hips ? hips.position.y - model.position.y : 0f;
            if (hipHeight <= 0.01f) hipHeight = 1f;
            runClipSpeed = runSpeedPerHipHeight * hipHeight;

            graph = PlayableGraph.Create(name + "_Locomotion");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Locomotion", animator);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            output.SetSourcePlayable(mixer);

            runPlayable = AnimationClipPlayable.Create(graph, runClip);
            runPlayable.SetApplyFootIK(false);
            runPlayable.SetSpeed(0); // il tempo lo guidiamo noi (avanti e indietro)
            graph.Connect(runPlayable, 0, mixer, 0);

            if (idleClip)
            {
                idlePlayable = AnimationClipPlayable.Create(graph, idleClip);
                idlePlayable.SetApplyFootIK(false);
                idlePlayable.SetSpeed(0);
                graph.Connect(idlePlayable, 0, mixer, 1);
            }

            legsYaw = Yaw(AimDirection);
            ApplyWeights();
            graph.Play();
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        void Update()
        {
            if (!graph.IsValid()) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            velocity = Vector3.SmoothDamp(velocity, movement.Velocity, ref velocityRef, velocitySmoothing);

            float speed = velocity.magnitude;
            Vector3 aim = AimDirection;
            float targetYaw;
            float direction = 1f;

            if (speed > 0.3f)
            {
                idleTurning = false;
                Vector3 move = velocity / speed;
                float angle = Vector3.Angle(move, aim);
                if (IsBackpedaling) IsBackpedaling = angle > backpedalAngle - backpedalHysteresis;
                else IsBackpedaling = angle > backpedalAngle + backpedalHysteresis;

                Vector3 legsDir = IsBackpedaling ? -move : move;
                float lean = Mathf.Clamp(Vector3.SignedAngle(legsDir, aim, Vector3.up), -legsLeanTowardAim, legsLeanTowardAim);
                targetYaw = Yaw(legsDir) + lean;
                direction = IsBackpedaling ? -1f : 1f;
            }
            else
            {
                IsBackpedaling = false;
                float diff = Mathf.DeltaAngle(legsYaw, Yaw(aim));
                if (Mathf.Abs(diff) > idleTurnThreshold) idleTurning = true;
                if (idleTurning && Mathf.Abs(diff) < 2f) idleTurning = false;
                targetYaw = idleTurning ? Yaw(aim) : legsYaw;
            }

            legsYaw = Mathf.MoveTowardsAngle(legsYaw, targetYaw, legsTurnSpeed * dt);

            // Tempo delle animazioni
            float rate = Mathf.Clamp(speed / Mathf.Max(0.01f, runClipSpeed), playbackSpeedRange.x, playbackSpeedRange.y);
            runTime = Mathf.Repeat(runTime + dt * rate * direction, runClip.length);
            runPlayable.SetTime(runTime);
            if (idleClip)
            {
                idleTime = Mathf.Repeat(idleTime + dt, idleClip.length);
                idlePlayable.SetTime(idleTime);
            }

            float targetWeight = idleClip ? Mathf.Clamp01(speed / Mathf.Max(0.01f, fullRunAtSpeed)) : 1f;
            runWeight = Mathf.MoveTowards(runWeight, targetWeight, dt / Mathf.Max(0.01f, blendTime));
            ApplyWeights();

            // Torsione del busto verso la mira
            float twistTarget = Mathf.Clamp(Mathf.DeltaAngle(legsYaw, Yaw(aim)), -maxTorsoTwist, maxTorsoTwist);
            torsoTwist = Mathf.MoveTowardsAngle(torsoTwist, twistTarget, torsoTurnSpeed * dt);
        }

        void LateUpdate()
        {
            if (!graph.IsValid()) return;

            // Il modello è figlio del Player, che gira con la mira: qui gli si dà la rotazione
            // delle gambe in coordinate mondo. In LateUpdate, dopo l'interpolazione del Rigidbody.
            model.rotation = Quaternion.Euler(0f, legsYaw, 0f) * modelFront;

            // Dopo l'Animator: ruota il busto attorno all'asse verticale del mondo.
            if (torsoBones == null) return;
            for (int i = 0; i < torsoBones.Length; i++)
            {
                if (!torsoBones[i]) continue;
                float w = i < torsoWeights.Length ? torsoWeights[i] : 0f;
                torsoBones[i].rotation = Quaternion.AngleAxis(torsoTwist * w, Vector3.up) * torsoBones[i].rotation;
            }
        }

        void ApplyWeights()
        {
            if (!idleClip) { mixer.SetInputWeight(0, 1f); return; }
            mixer.SetInputWeight(0, runWeight);
            mixer.SetInputWeight(1, 1f - runWeight);
        }

        static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        void FindTorsoBones()
        {
            if (animator.isHuman)
            {
                torsoBones = new[]
                {
                    animator.GetBoneTransform(HumanBodyBones.Spine),
                    animator.GetBoneTransform(HumanBodyBones.Chest),
                    animator.GetBoneTransform(HumanBodyBones.UpperChest),
                };
            }
            else
            {
                torsoBones = new[] { FindBone(model, "Spine"), FindBone(model, "Spine1"), FindBone(model, "Spine2") };
            }
        }

        /// <summary>Trova un osso per nome, ignorando il prefisso (es. "mixamorig:Spine").</summary>
        public static Transform FindBone(Transform root, string boneName)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                int c = n.LastIndexOf(':');
                if (c >= 0) n = n.Substring(c + 1);
                if (n == boneName) return t;
            }
            return null;
        }
    }
}
