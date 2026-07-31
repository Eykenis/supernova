using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Owns the runtime grab-hook projectile and rope. The projectile is swept
    /// through physics space, but only a terrain-owned MeshCollider can stop it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GrabHookController : MonoBehaviour
    {
        public enum GrabHookState
        {
            Retracted = 0,
            Aiming = 1,
            Flying = 2,
            Attached = 3,
            Returning = 4,
        }

        private const float EndpointTolerance = 0.08f;
        private const float ReturnCompletionDistance = 0.08f;

        [SerializeField] private bool deviceEnabled;
        [SerializeField] private Transform view;
        [SerializeField] private VoxelPlayerController playerController;
        [SerializeField] private PlayerToolController toolController;
        [SerializeField] private LayerMask collisionLayers = ~0;

        private PlayerToolDefinition activeDefinition;
        private Transform hookTransform;
        private Collider attachedCollider;
        private Rigidbody hookBody;
        private SphereCollider hookCollider;
        private LineRenderer rope;
        private Material ropeMaterial;
        private LineRenderer aimLine;
        private Material aimMaterial;
        private Texture2D aimDashTexture;
        private Vector3 previousHookPosition;
        private Vector3 expectedLandingPoint;
        private Vector3 attachedPoint;
        private GrabHookState state;

        public bool DeviceEnabled => deviceEnabled;
        public GrabHookState State => state;
        public bool IsDeployed => state != GrabHookState.Retracted;
        public bool IsAiming => state == GrabHookState.Aiming;
        public bool IsAttached => state == GrabHookState.Attached;
        public bool LocksPlayerMovement =>
            state == GrabHookState.Flying
            || state == GrabHookState.Attached;
        public Vector3 HookPosition => hookTransform != null
            ? hookTransform.position
            : GetTetherOrigin();
        public Vector3 AttachedPoint => attachedPoint;
        public Vector3 ExpectedLandingPoint => expectedLandingPoint;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            ClearHook();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            ResolveReferences();
            float deltaTime = Time.deltaTime;
            switch (state)
            {
                case GrabHookState.Aiming:
                    TickAiming();
                    break;
                case GrabHookState.Flying:
                    TickFlying();
                    break;
                case GrabHookState.Attached:
                    TickAttached(deltaTime);
                    break;
                case GrabHookState.Returning:
                    TickReturning(deltaTime);
                    break;
            }

            UpdateRope();
        }

        private void FixedUpdate()
        {
            if (state != GrabHookState.Flying
                || hookBody == null
                || hookBody.velocity.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            hookBody.MoveRotation(
                CalculateHookRotation(hookBody.velocity));
        }

        public void SetDeviceEnabled(bool value)
        {
            deviceEnabled = value;
            if (deviceEnabled || !IsDeployed) return;

            if (IsAiming)
                ClearHook();
            else
                BeginReturn();
        }

        public bool CanBeginAim(PlayerToolDefinition definition)
        {
            return deviceEnabled
                && state == GrabHookState.Retracted
                && definition != null
                && definition.IsGrabHook
                && definition.GrabHookProjectileModelPrefab != null;
        }

        public bool CanUsePrimaryAction(PlayerToolDefinition definition)
        {
            return CanBeginAim(definition)
                || (IsAiming && activeDefinition == definition);
        }

        public bool BeginAim(PlayerToolDefinition definition)
        {
            ResolveReferences();
            if (!CanBeginAim(definition)) return false;

            ClearHook();
            activeDefinition = definition;
            state = GrabHookState.Aiming;
            EnsureAimLine();
            TickAiming();
            return true;
        }

        public bool ReleaseThrow()
        {
            ResolveReferences();
            if (!IsAiming
                || activeDefinition == null
                || !deviceEnabled)
            {
                return false;
            }

            Transform aim = view != null ? view : transform;
            Vector3 direction = aim.forward.sqrMagnitude > 0.0001f
                ? aim.forward.normalized
                : transform.forward;
            Vector3 origin = GetTetherOrigin();
            DestroyAimLine();

            GameObject hookObject = new GameObject("Active Grab Hook");
            hookTransform = hookObject.transform;
            hookTransform.SetPositionAndRotation(
                origin,
                CalculateHookRotation(direction));
            GameObject visual = Instantiate(
                activeDefinition.GrabHookProjectileModelPrefab,
                hookTransform,
                false);
            visual.name = activeDefinition.GrabHookProjectileModelPrefab.name;

            hookCollider = hookObject.AddComponent<SphereCollider>();
            hookCollider.radius = activeDefinition.GrabHookCollisionRadius;
            hookCollider.isTrigger = true;
            hookBody = hookObject.AddComponent<Rigidbody>();
            hookBody.useGravity = true;
            hookBody.mass = 1f;
            hookBody.drag = 0f;
            hookBody.angularDrag = 0.05f;
            hookBody.interpolation = RigidbodyInterpolation.Interpolate;
            hookBody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            hookBody.velocity =
                direction * activeDefinition.GrabHookLaunchSpeed;
            previousHookPosition = origin;
            state = GrabHookState.Flying;
            EnsureRope();
            UpdateRope();
            return true;
        }

        public void CancelAim()
        {
            if (IsAiming) ClearHook();
        }

        public void Retract()
        {
            if (!IsDeployed) return;
            if (IsAiming)
                ClearHook();
            else
                BeginReturn();
        }

        public static bool IsTerrainMeshCollider(Collider candidate)
        {
            if (!(candidate is MeshCollider)) return false;

            Transform current = candidate.transform;
            while (current != null)
            {
                MonoBehaviour[] behaviours =
                    current.GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IVoxelTerrain)
                        return true;
                }
                current = current.parent;
            }

            return false;
        }

        public static bool HasBlockingMesh(
            Vector3 start,
            Vector3 end,
            Transform ownerRoot,
            Collider endpointCollider)
        {
            Vector3 displacement = end - start;
            float distance = displacement.magnitude;
            if (distance <= EndpointTolerance) return false;

            RaycastHit[] hits = Physics.RaycastAll(
                start,
                displacement / distance,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider candidate = hits[i].collider;
                if (!(candidate is MeshCollider)) continue;
                if (ownerRoot != null
                    && candidate.transform.IsChildOf(ownerRoot))
                {
                    continue;
                }

                bool endpointHit = candidate == endpointCollider
                    && hits[i].distance >= distance - EndpointTolerance;
                if (!endpointHit) return true;
            }

            return false;
        }

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

        public static Quaternion CalculateHookRotation(Vector3 velocity)
        {
            if (velocity.sqrMagnitude <= 0.0001f)
                return Quaternion.identity;

            Vector3 direction = velocity.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.98f
                ? Vector3.forward
                : Vector3.up;
            return Quaternion.LookRotation(direction, up);
        }

        private void TickAiming()
        {
            if (activeDefinition == null
                || !deviceEnabled
                || playerController == null
                || !playerController.IsAlive
                || playerController.DebugFlyMode)
            {
                ClearHook();
                return;
            }

            Transform aim = view != null ? view : transform;
            Vector3 direction = aim.forward.sqrMagnitude > 0.0001f
                ? aim.forward.normalized
                : transform.forward;
            Vector3 origin = GetTetherOrigin();
            Vector3 initialVelocity =
                direction * activeDefinition.GrabHookLaunchSpeed;
            expectedLandingPoint = PredictLandingPoint(
                origin,
                initialVelocity,
                activeDefinition);
            UpdateAimLine(origin, expectedLandingPoint);
        }

        private Vector3 PredictLandingPoint(
            Vector3 origin,
            Vector3 initialVelocity,
            PlayerToolDefinition definition)
        {
            Vector3 previous = origin;
            float duration = definition.GrabHookAimPredictionDuration;
            float step = definition.GrabHookAimPredictionStep;
            for (float time = step; time <= duration + 0.0001f; time += step)
            {
                Vector3 next = CalculateBallisticPosition(
                    origin,
                    initialVelocity,
                    Physics.gravity,
                    Mathf.Min(time, duration));
                Vector3 fromOrigin = next - origin;
                bool reachedMaximumLength =
                    fromOrigin.sqrMagnitude
                    >= definition.GrabHookMaximumLength
                        * definition.GrabHookMaximumLength;
                if (reachedMaximumLength)
                {
                    next = origin
                        + fromOrigin.normalized
                            * definition.GrabHookMaximumLength;
                }

                Vector3 segment = next - previous;
                float segmentDistance = segment.magnitude;
                if (segmentDistance > 0.0001f
                    && TryFindTerrainHit(
                        previous,
                        segment / segmentDistance,
                        segmentDistance,
                        definition.GrabHookCollisionRadius,
                        out RaycastHit terrainHit))
                {
                    return terrainHit.point;
                }

                previous = next;
                if (reachedMaximumLength) break;
            }

            return previous;
        }

        private void TickFlying()
        {
            if (hookTransform == null
                || hookBody == null
                || activeDefinition == null)
            {
                ClearHook();
                return;
            }
            if (playerController == null
                || !playerController.IsAlive
                || playerController.DebugFlyMode)
            {
                BeginReturn();
                return;
            }

            Vector3 currentPosition = hookBody.position;
            Vector3 displacement =
                currentPosition - previousHookPosition;
            float travelDistance = displacement.magnitude;
            if (travelDistance > 0.0001f
                && TryFindTerrainHit(
                    previousHookPosition,
                    displacement / travelDistance,
                    travelDistance,
                    activeDefinition.GrabHookCollisionRadius,
                    out RaycastHit terrainHit))
            {
                Attach(terrainHit);
                return;
            }

            previousHookPosition = currentPosition;
            if (Vector3.Distance(GetTetherOrigin(), currentPosition)
                > activeDefinition.GrabHookMaximumLength)
            {
                BeginReturn();
            }
        }

        private void TickAttached(float deltaTime)
        {
            if (hookTransform == null
                || activeDefinition == null
                || attachedCollider == null)
            {
                BeginReturn();
                return;
            }

            Vector3 playerPosition = GetPlayerPullOrigin();
            float distance = Vector3.Distance(playerPosition, attachedPoint);
            if (distance <= activeDefinition.GrabHookArrivalDistance)
            {
                BeginReturn();
                return;
            }
            if (distance > activeDefinition.GrabHookMaximumLength
                || HasBlockingMesh(
                    playerPosition,
                    attachedPoint,
                    transform,
                    attachedCollider))
            {
                BeginReturn();
                return;
            }

            if (playerController == null
                || !playerController.IsAlive
                || playerController.DebugFlyMode)
            {
                return;
            }

            Vector3 pull = attachedPoint - playerPosition;
            if (pull.sqrMagnitude <= 0.0001f) return;
            playerController.AddExternalAcceleration(
                pull.normalized
                    * activeDefinition.GrabHookPullAcceleration,
                deltaTime,
                activeDefinition.GrabHookMaximumPullSpeed);
        }

        private void TickReturning(float deltaTime)
        {
            if (hookTransform == null || activeDefinition == null)
            {
                ClearHook();
                return;
            }

            Vector3 destination = GetTetherOrigin();
            hookTransform.position = Vector3.MoveTowards(
                hookTransform.position,
                destination,
                activeDefinition.GrabHookRetractSpeed * deltaTime);
            Vector3 returnDirection = destination - hookTransform.position;
            if (returnDirection.sqrMagnitude > 0.0001f)
            {
                hookTransform.rotation = Quaternion.LookRotation(
                    returnDirection.normalized,
                    Vector3.up);
            }

            if (Vector3.Distance(hookTransform.position, destination)
                <= ReturnCompletionDistance)
            {
                ClearHook();
            }
        }

        private bool TryFindTerrainHit(
            Vector3 origin,
            Vector3 direction,
            float distance,
            float radius,
            out RaycastHit nearestHit)
        {
            nearestHit = default;
            if (distance <= 0f) return false;

            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                radius,
                direction,
                distance,
                collisionLayers,
                QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsTerrainMeshCollider(hit.collider)
                    || hit.distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = hit.distance;
                nearestHit = hit;
                found = true;
            }

            return found;
        }

        private void Attach(RaycastHit hit)
        {
            attachedCollider = hit.collider;
            attachedPoint = hit.point;
            playerController?.ClearExternalVelocity();
            playerController?.ClearVerticalVelocity();
            StopHookPhysics();
            hookTransform.position = attachedPoint;
            Vector3 forward = -hit.normal;
            if (forward.sqrMagnitude > 0.0001f)
            {
                hookTransform.rotation = CalculateHookRotation(forward);
            }
            state = GrabHookState.Attached;
        }

        private void BeginReturn()
        {
            if (state == GrabHookState.Attached)
                playerController?.ClearExternalVelocity();
            attachedCollider = null;
            StopHookPhysics();
            state = hookTransform != null
                ? GrabHookState.Returning
                : GrabHookState.Retracted;
        }

        private void StopHookPhysics()
        {
            if (hookBody != null)
            {
                hookBody.velocity = Vector3.zero;
                hookBody.angularVelocity = Vector3.zero;
                hookBody.useGravity = false;
                hookBody.isKinematic = true;
                hookBody.interpolation = RigidbodyInterpolation.None;
            }
            if (hookCollider != null)
                hookCollider.enabled = false;
        }

        private void EnsureRope()
        {
            if (rope != null) return;

            GameObject ropeObject = new GameObject("Grab Hook Rope");
            ropeObject.transform.SetParent(transform, false);
            rope = ropeObject.AddComponent<LineRenderer>();
            rope.useWorldSpace = true;
            rope.positionCount = 2;
            rope.numCapVertices = 2;
            rope.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            rope.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                ropeMaterial = new Material(shader)
                {
                    name = "Grab Hook Rope (Runtime)",
                    hideFlags = HideFlags.DontSave,
                };
                ropeMaterial.color = new Color(0.16f, 0.2f, 0.23f, 1f);
                rope.sharedMaterial = ropeMaterial;
            }
        }

        private void EnsureAimLine()
        {
            if (aimLine != null) return;

            GameObject lineObject = new GameObject(
                "Grab Hook Landing Preview");
            lineObject.transform.SetParent(transform, false);
            aimLine = lineObject.AddComponent<LineRenderer>();
            aimLine.useWorldSpace = true;
            aimLine.positionCount = 2;
            aimLine.textureMode = LineTextureMode.Tile;
            aimLine.numCapVertices = 0;
            aimLine.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            aimLine.receiveShadows = false;

            aimDashTexture = new Texture2D(
                16,
                1,
                TextureFormat.RGBA32,
                false)
            {
                name = "Grab Hook Aim Dashes (Runtime)",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave,
            };
            for (int x = 0; x < aimDashTexture.width; x++)
            {
                float alpha = x < aimDashTexture.width / 2 ? 1f : 0f;
                aimDashTexture.SetPixel(
                    x,
                    0,
                    new Color(1f, 1f, 1f, alpha));
            }
            aimDashTexture.Apply(false, true);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;

            aimMaterial = new Material(shader)
            {
                name = "Grab Hook Aim Line (Runtime)",
                hideFlags = HideFlags.DontSave,
                color = new Color(0.4f, 0.9f, 1f, 0.8f),
                mainTexture = aimDashTexture,
            };
            aimLine.sharedMaterial = aimMaterial;
        }

        private void UpdateAimLine(Vector3 start, Vector3 end)
        {
            EnsureAimLine();
            if (aimLine == null) return;

            float width = activeDefinition != null
                ? activeDefinition.GrabHookRopeWidth
                : 0.035f;
            aimLine.startWidth = width;
            aimLine.endWidth = width;
            aimLine.SetPosition(0, start);
            aimLine.SetPosition(1, end);
            if (aimMaterial != null)
            {
                float repeats = Mathf.Max(
                    1f,
                    Vector3.Distance(start, end) / 0.5f);
                aimMaterial.mainTextureScale =
                    new Vector2(repeats, 1f);
            }
        }

        private void UpdateRope()
        {
            if (rope == null) return;
            bool visible = hookTransform != null
                && state != GrabHookState.Retracted;
            rope.enabled = visible;
            if (!visible) return;

            float width = activeDefinition != null
                ? activeDefinition.GrabHookRopeWidth
                : 0.035f;
            rope.startWidth = width;
            rope.endWidth = width;
            rope.SetPosition(0, GetTetherOrigin());
            rope.SetPosition(1, hookTransform.position);
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
            if (aimDashTexture != null)
            {
                if (Application.isPlaying)
                    Destroy(aimDashTexture);
                else
                    DestroyImmediate(aimDashTexture);
            }

            aimLine = null;
            aimMaterial = null;
            aimDashTexture = null;
        }

        private Vector3 GetTetherOrigin()
        {
            if (toolController != null
                && toolController.EquippedToolModel != null)
            {
                return toolController.EquippedToolModel.transform.position;
            }
            if (view != null) return view.position;
            return transform.position + Vector3.up;
        }

        private Vector3 GetPlayerPullOrigin()
        {
            CharacterController character =
                playerController != null
                    ? playerController.GetComponent<CharacterController>()
                    : null;
            return character != null
                ? character.bounds.center
                : transform.position;
        }

        private void ClearHook()
        {
            if (state == GrabHookState.Attached)
                playerController?.ClearExternalVelocity();
            DestroyAimLine();
            if (hookTransform != null)
            {
                hookTransform.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(hookTransform.gameObject);
                else
                    DestroyImmediate(hookTransform.gameObject);
            }
            if (rope != null)
            {
                rope.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(rope.gameObject);
                else
                    DestroyImmediate(rope.gameObject);
            }
            if (ropeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(ropeMaterial);
                else
                    DestroyImmediate(ropeMaterial);
            }

            activeDefinition = null;
            hookTransform = null;
            attachedCollider = null;
            hookBody = null;
            hookCollider = null;
            previousHookPosition = Vector3.zero;
            expectedLandingPoint = Vector3.zero;
            rope = null;
            ropeMaterial = null;
            state = GrabHookState.Retracted;
        }

        private void ResolveReferences()
        {
            if (playerController == null)
                playerController = GetComponent<VoxelPlayerController>();
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
