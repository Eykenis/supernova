using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Owns the pickaxe's right-click throw: hold to aim with a ballistic preview,
    /// release to launch. The thrown pickaxe leaves the player's inventory until it
    /// is recovered, so only one throw can be in the air at a time.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PickaxeThrowController : MonoBehaviour
    {
        private const float AimPredictionDuration = 2.5f;
        private const float AimPredictionStep = 0.05f;
        private const int AimLinePositionCount = 24;
        private const float ThrowForwardOffset = 0.6f;

        [SerializeField] private Transform view;
        [SerializeField] private PlayerToolController toolController;
        [SerializeField] private LayerMask collisionLayers = ~0;

        private PlayerToolDefinition aimingDefinition;
        private ThrownPickaxe activeThrow;
        private LineRenderer aimLine;
        private Material aimMaterial;

        public bool IsAiming => aimingDefinition != null;
        public ThrownPickaxe ActiveThrow => activeThrow;
        public bool HasThrowInFlight => activeThrow != null;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            CancelAim();
        }

        public bool CanBeginAim(PlayerToolDefinition definition)
        {
            return definition != null
                && definition.CanThrowPickaxe
                && activeThrow == null
                && !IsAiming;
        }

        public bool BeginAim(PlayerToolDefinition definition)
        {
            ResolveReferences();
            if (!CanBeginAim(definition)) return false;

            aimingDefinition = definition;
            UpdateAim();
            return true;
        }

        public void UpdateAim()
        {
            if (aimingDefinition == null) return;
            DrawAimPreview(
                GetThrowOrigin(),
                GetThrowVelocity(aimingDefinition));
        }

        public void CancelAim()
        {
            aimingDefinition = null;
            DestroyAimLine();
        }

        /// <summary>
        /// Launches the aimed pickaxe and removes it from the hotbar. Returns the
        /// live projectile, or null when the throw could not be made.
        /// </summary>
        public ThrownPickaxe ReleaseThrow()
        {
            ResolveReferences();
            PlayerToolDefinition definition = aimingDefinition;
            CancelAim();
            if (definition == null
                || !definition.CanThrowPickaxe
                || activeThrow != null)
            {
                return null;
            }

            Vector3 velocity = GetThrowVelocity(definition);
            Vector3 origin = GetThrowOrigin();
            ThrownPickaxe pickaxe = Instantiate(
                definition.ThrownPickaxePrefab,
                origin,
                // Use the prefab's own spike axis; Launch re-applies the same pose.
                ThrownPickaxe.CalculateFlightRotation(
                    velocity,
                    definition.ThrownPickaxePrefab.HeadTipLocalDirection));
            pickaxe.name = definition.ThrownPickaxePrefab.name;
            IgnoreOwnerCollisions(pickaxe);
            pickaxe.Launch(
                velocity,
                toolController,
                definition.Item,
                definition.PickaxeSpinRevolutions,
                definition.PickaxePickupDistance);

            activeThrow = pickaxe;
            toolController?.SuspendItem(definition.Item);
            return pickaxe;
        }

        /// <summary>
        /// Calls the thrown pickaxe home. It pulls free of whatever it is embedded in
        /// and flies back to the player, who receives it on arrival.
        /// </summary>
        public bool RecallThrow()
        {
            if (activeThrow == null) return false;
            if (!activeThrow.CanBeRecovered)
            {
                activeThrow = null;
                return false;
            }

            return activeThrow.BeginRecall();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            // The projectile destroys itself once recovered, which clears the slot
            // for the next throw.
            if (activeThrow != null && !activeThrow.CanBeRecovered)
                activeThrow = null;
            if (IsAiming) UpdateAim();
        }

        private Vector3 GetThrowOrigin()
        {
            Transform aim = view != null ? view : transform;
            return aim.position + GetAimDirection() * ThrowForwardOffset;
        }

        private Vector3 GetAimDirection()
        {
            Transform aim = view != null ? view : transform;
            return aim.forward.sqrMagnitude > 0.0001f
                ? aim.forward.normalized
                : transform.forward;
        }

        private Vector3 GetThrowVelocity(PlayerToolDefinition definition)
        {
            return GetAimDirection() * definition.PickaxeThrowSpeed;
        }

        private void DrawAimPreview(Vector3 origin, Vector3 velocity)
        {
            EnsureAimLine();
            if (aimLine == null) return;

            Vector3 previous = origin;
            aimLine.positionCount = AimLinePositionCount;
            aimLine.SetPosition(0, origin);
            int written = 1;
            for (int i = 1; i < AimLinePositionCount; i++)
            {
                float time = i * AimPredictionStep;
                Vector3 next = CalculateBallisticPosition(
                    origin,
                    velocity,
                    Physics.gravity,
                    Mathf.Min(time, AimPredictionDuration));
                Vector3 segment = next - previous;
                float distance = segment.magnitude;
                if (distance > 0.0001f
                    && Physics.Raycast(
                        previous,
                        segment / distance,
                        out RaycastHit hit,
                        distance,
                        collisionLayers,
                        QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(transform))
                {
                    aimLine.SetPosition(written++, hit.point);
                    break;
                }

                aimLine.SetPosition(written++, next);
                previous = next;
            }

            aimLine.positionCount = written;
        }

        /// <summary>
        /// Position of a projectile launched from <paramref name="origin"/> after
        /// <paramref name="time"/> seconds under constant acceleration.
        /// </summary>
        public static Vector3 CalculateBallisticPosition(
            Vector3 origin,
            Vector3 initialVelocity,
            Vector3 gravity,
            float time)
        {
            float clampedTime = Mathf.Max(0f, time);
            return origin
                + initialVelocity * clampedTime
                + gravity * (0.5f * clampedTime * clampedTime);
        }

        private void EnsureAimLine()
        {
            if (aimLine != null) return;

            GameObject lineObject = new GameObject("Pickaxe Throw Preview");
            lineObject.transform.SetParent(transform, false);
            aimLine = lineObject.AddComponent<LineRenderer>();
            aimLine.useWorldSpace = true;
            aimLine.numCapVertices = 2;
            aimLine.numCornerVertices = 2;
            aimLine.startWidth = 0.03f;
            aimLine.endWidth = 0.01f;
            aimLine.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            aimLine.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;

            aimMaterial = new Material(shader)
            {
                name = "Pickaxe Throw Preview (Runtime)",
                hideFlags = HideFlags.DontSave,
                color = new Color(1f, 0.82f, 0.35f, 0.75f),
            };
            aimLine.sharedMaterial = aimMaterial;
        }

        private void DestroyAimLine()
        {
            if (aimLine != null)
            {
                aimLine.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(aimLine.gameObject);
                else
                    DestroyImmediate(aimLine.gameObject);
            }
            if (aimMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(aimMaterial);
                else
                    DestroyImmediate(aimMaterial);
            }

            aimLine = null;
            aimMaterial = null;
        }

        private void IgnoreOwnerCollisions(Component projectile)
        {
            if (projectile == null) return;

            Collider[] ownerColliders = GetComponentsInChildren<Collider>(true);
            Collider[] projectileColliders =
                projectile.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < ownerColliders.Length; i++)
            {
                if (ownerColliders[i] == null) continue;
                for (int j = 0; j < projectileColliders.Length; j++)
                {
                    if (projectileColliders[j] == null) continue;
                    Physics.IgnoreCollision(
                        ownerColliders[i],
                        projectileColliders[j],
                        true);
                }
            }
        }

        private void ResolveReferences()
        {
            if (toolController == null)
                toolController = GetComponent<PlayerToolController>();
            if (view == null)
            {
                Camera childCamera = GetComponentInChildren<Camera>(true);
                if (childCamera != null) view = childCamera.transform;
            }
        }
    }
}
