using System;
using Supernova.Audio;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves.Creatures.Navigation;
using Supernova.UI;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    public enum CreatureBehaviorState
    {
        Idle,
        Wander,
        Pursue,
        Attack,
        Hurt,
        Dead,
        Caught,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreaturePhysicsMotor))]
    public sealed class CreatureBehaviorAgent :
        MonoBehaviour,
        IMonsterDamageable,
        ICollisionImpulseDamageReceiver
    {
        public const float DefaultSimulationDistance = 160f;
        public const float DefaultPursuitDistance = 64f;
        public const float DefaultPursuitRetentionDistance = 96f;
        public const float DeadDespawnDelay = 5f;
        public const float DefaultMinimumCollisionDamageImpulse = 3f;

        [Header("References")]
        [SerializeField] private MinecraftCaveInfiniteWorld caveWorld;
        [SerializeField] private Transform playerFoot;
        [SerializeField] private CreaturePhysicsMotor motor;

        [Header("Behavior Distances (voxels)")]
        [SerializeField, Min(1f)] private float simulationDistance = DefaultSimulationDistance;
        [SerializeField, Min(0f)] private float pursuitDistance = DefaultPursuitDistance;
        [SerializeField, Min(0f)]
        private float pursuitRetentionDistance =
            DefaultPursuitRetentionDistance;
        [SerializeField, Min(0f)] private float attackDistance = 2f;

        [Header("Combat")]
        [SerializeField, Min(1f)] private float maximumHealth = 60f;
        [SerializeField] private CharacterVitals vitals = new CharacterVitals();
        [SerializeField, Min(0f)] private float attackDamage = 10f;
        [SerializeField, Min(0.01f)] private float attackWindup = 0.2f;
        [SerializeField, Min(0.02f)] private float attackDuration = 0.55f;
        [SerializeField, Min(0.02f)] private float attackCooldown = 0.9f;
        [SerializeField, Min(0.02f)] private float hurtDuration = 0.3f;
        [SerializeField, Min(0f)] private float attackImpulse = 1.5f;

        [Header("Collision Damage")]
        [Tooltip(
            "How strongly collision impacts convert into health damage. "
            + "Configure this per monster prefab.")]
        [SerializeField, Range(0f, 1f)] private float collisionFragility = 0.5f;
        [Tooltip(
            "Minimum mass-normalized collision impulse before health damage "
            + "starts. Treasure uses 1 by default.")]
        [SerializeField, Min(0f)] private float minimumDamageImpulse =
            DefaultMinimumCollisionDamageImpulse;
        [Tooltip(
            "Fraction of maximum health lost per squared unit of damaging "
            + "specific impulse. 0.03 means 3%.")]
        [SerializeField, Min(0f)]
        private float damagePercentagePerSquaredImpulse =
            CollisionImpulseDamage.DefaultDamagePercentagePerSquaredImpulse;

        [Header("Navigation")]
        [SerializeField] private CreatureNavigationProfile navigation =
            new CreatureNavigationProfile();

        [Header("Debug")]
        [SerializeField] private bool drawDebug = true;
        [SerializeField] private CreatureBehaviorState currentState;
        [SerializeField] private int lastVisitedNodeCount;


        private IVoxelTerrain Terrain => voxelTerrain ?? caveWorld;
        private static AudioAssetReferences AudioAssets =>
            GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.Audio
                : null;

        private IVoxelTerrain voxelTerrain;
        private CharacterStateMachine<CreatureBehaviorState> stateMachine;
        private float stateSeconds;
        private float nextAttackTime;
        private bool attackApplied;
        private int attackSwingCount;
        private bool deathCleanupScheduled;
        private bool isCaught;
        private bool isPursuitEngaged;
        private VoxelPathNodeMaker nodeMaker;
        private VoxelPathfinder pathfinder;
        private CreatureNavigator navigator;
        private CreatureBodyBox bodyBox;
        private bool hasBodyBox;
        private float stuckWatchTime;
        private int stuckJumpCount;
        private int movementSoundLoopId;
        private bool movementSoundPlaying;

        public event Action<float, float> HealthChanged;
        public event Action<float, Vector3> Damaged;

        public CreatureBehaviorState CurrentState => currentState;
        /// <summary>Increments once per attack swing so presentation can replay
        /// the attack clip in step with each settlement.</summary>
        public int AttackSwingCount => attackSwingCount;
        public GameObject Owner => gameObject;
        public float CurrentHealth => vitals != null ? vitals.CurrentHealth : 0f;
        public float MaximumHealth => vitals != null ? vitals.MaximumHealth : maximumHealth;
        public bool IsAlive => vitals != null && vitals.IsAlive;
        public bool IsCaught => isCaught;
        public GameObject CollisionImpulseOwner => gameObject;
        public bool IsActuallyMoving =>
            motor != null
            && motor.NormalizedHorizontalSpeed > 0.08f;

        public IVoxelTerrain VoxelTerrain => Terrain;
        public MinecraftCaveInfiniteWorld CaveWorld => caveWorld;
        public Transform PlayerFoot => playerFoot;
        public CreatureNavigationProfile NavigationProfile => navigation;
        public VoxelPath CurrentPath => navigator?.CurrentPath;
        public int LastVisitedNodeCount => lastVisitedNodeCount;
        public float CollisionFragility => Mathf.Clamp01(collisionFragility);
        public float MinimumDamageImpulse => Mathf.Max(0f, minimumDamageImpulse);
        public float DamagePercentagePerSquaredImpulse =>
            Mathf.Max(0f, damagePercentagePerSquaredImpulse);

        public void BindWorldContext(
            MinecraftCaveInfiniteWorld world,
            Transform targetPlayerFoot)
        {
            BindWorldContext((IVoxelTerrain)world, targetPlayerFoot);
        }

        public void BindWorldContext(
            IVoxelTerrain world,
            Transform targetPlayerFoot)
        {
            voxelTerrain = world;
            caveWorld = world as MinecraftCaveInfiniteWorld;
            playerFoot = targetPlayerFoot;
        }


        private void Awake()
        {
            movementSoundLoopId = SoundEffectEvents.CreateLoopId();
            ResolveReferences();
            EnsureVitals(true);
            EnsureHealthBar();
            BuildStateMachine();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureStateMachine();
            stateMachine.Start(!vitals.IsAlive
                ? CreatureBehaviorState.Dead
                : isCaught
                    ? CreatureBehaviorState.Caught
                    : CreatureBehaviorState.Idle);
        }

        private void OnDisable()
        {
            StopMovementSound();
            stateMachine?.Stop();
            if (motor != null)
            {
                motor.Stop();
            }
        }

        private void Update()
        {
            EnsureStateMachine();
            if (!vitals.IsAlive)
            {
                SetState(CreatureBehaviorState.Dead);
                stateMachine.Tick(Time.deltaTime);
                return;
            }

            if (isCaught)
            {
                SetState(CreatureBehaviorState.Caught);
                stateMachine.Tick(Time.deltaTime);
                return;
            }

            if (currentState == CreatureBehaviorState.Hurt)
            {
                stateMachine.Tick(Time.deltaTime);
                return;
            }

            if (!EnsureReady())
            {
                SetState(CreatureBehaviorState.Idle);
                stateMachine.Tick(Time.deltaTime);
                return;
            }

            float playerDistance = GetPlayerDistance();
            CreatureBehaviorState desiredState = SelectState(playerDistance);
            if (desiredState != currentState)
            {
                SetState(desiredState);
            }

            stateMachine.Tick(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (Application.isPlaying)
            {
                UpdateMovementSound();
            }
        }

        private void UpdateMovementSound()
        {
            SoundEffectCue cue = AudioAssets != null
                ? AudioAssets.CreatureRun
                : null;
            if (cue == null
                || !ShouldPlayMovementSound(
                    currentState,
                    IsActuallyMoving,
                    IsAlive))
            {
                StopMovementSound();
                return;
            }

            if (movementSoundPlaying)
                return;

            movementSoundPlaying = SoundEffectEvents.RequestLoop(
                movementSoundLoopId,
                cue,
                transform);
        }

        private void StopMovementSound()
        {
            if (movementSoundPlaying)
            {
                SoundEffectEvents.RequestStopLoop(movementSoundLoopId);
            }
            movementSoundPlaying = false;
        }

        private static bool ShouldPlayMovementSound(
            CreatureBehaviorState state,
            bool isActuallyMoving,
            bool isAlive)
        {
            return isAlive
                && isActuallyMoving
                && (state == CreatureBehaviorState.Wander
                    || state == CreatureBehaviorState.Pursue);
        }


        private CreatureBehaviorState SelectState(float playerDistance)
        {
            if (playerDistance > simulationDistance)
            {
                isPursuitEngaged = false;
                return CreatureBehaviorState.Idle;
            }

            if (playerDistance <= pursuitDistance)
            {
                isPursuitEngaged = true;
            }
            else if (playerDistance > pursuitRetentionDistance)
            {
                isPursuitEngaged = false;
            }

            if (playerDistance <= attackDistance)
            {
                return CreatureBehaviorState.Attack;
            }

            if (isPursuitEngaged)
            {
                return CreatureBehaviorState.Pursue;
            }

            return CreatureBehaviorState.Wander;
        }

        private float GetPlayerDistance()
        {
            Vector3 creaturePosition = WorldToVoxelPosition(
                transform.position);
            Vector3 playerPosition = WorldToVoxelPosition(playerFoot.position);
            Vector3 difference = playerPosition - creaturePosition;
            return difference.magnitude;
        }

        private Vector3 WorldToVoxelPosition(Vector3 worldPosition)
        {
            IVoxelTerrain terrain = Terrain;
            if (terrain == null)
            {
                return worldPosition;
            }

            float voxelSize = Mathf.Max(0.0001f, terrain.VoxelSize);
            return terrain.TerrainTransform
                .InverseTransformPoint(worldPosition) / voxelSize;
        }

        public bool ReceiveDamage(in DamageInfo damage)
        {
            EnsureVitals(false);
            float previousHealth = vitals.CurrentHealth;
            if (!vitals.ApplyDamage(damage.Amount)) return false;
            float actualDamage = previousHealth - vitals.CurrentHealth;

            HealthChanged?.Invoke(vitals.CurrentHealth, vitals.MaximumHealth);
            Damaged?.Invoke(actualDamage, damage.Point);

            ResolveReferences();
            EnsureStateMachine();
            if (motor != null && damage.Impulse > 0f)
            {
                motor.ApplyImpulse(damage.Direction * damage.Impulse);
            }

            SetState(!vitals.IsAlive
                ? CreatureBehaviorState.Dead
                : isCaught
                    ? CreatureBehaviorState.Caught
                    : CreatureBehaviorState.Hurt);
            return true;
        }

        public void SetCaught(bool value)
        {
            if (isCaught == value
                && (!value || currentState == CreatureBehaviorState.Caught))
            {
                return;
            }

            isCaught = value;
            if (value)
            {
                isPursuitEngaged = false;
            }
            ResolveReferences();
            EnsureStateMachine();
            SetState(!vitals.IsAlive
                ? CreatureBehaviorState.Dead
                : value
                    ? CreatureBehaviorState.Caught
                    : CreatureBehaviorState.Idle);
        }

        public float ApplyCollisionImpulse(float impulseMagnitude)
        {
            return ApplyCollisionImpulse(impulseMagnitude, transform.position);
        }

        public float ApplyCollisionImpulse(
            float impulseMagnitude,
            Vector3 collisionPoint)
        {
            EnsureVitals(false);
            if (!vitals.IsAlive)
            {
                return 0f;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            float mass = body != null ? Mathf.Max(0.0001f, body.mass) : 1f;
            float damage = CollisionImpulseDamage.CalculateDamage(
                vitals.MaximumHealth,
                impulseMagnitude,
                CollisionFragility,
                MinimumDamageImpulse,
                DamagePercentagePerSquaredImpulse,
                mass);
            if (damage <= 0f)
            {
                return 0f;
            }

            float previousHealth = vitals.CurrentHealth;
            var collisionDamage = new DamageInfo(
                damage,
                null,
                collisionPoint,
                Vector3.zero);
            return ReceiveDamage(collisionDamage)
                ? previousHealth - vitals.CurrentHealth
                : 0f;
        }

        bool ICollisionImpulseDamageReceiver.ApplyCollisionImpulseDamage(
            float impulseMagnitude,
            Vector3 collisionPoint)
        {
            return ApplyCollisionImpulse(impulseMagnitude, collisionPoint) > 0f;
        }

        public void RestoreFullHealth()
        {
            EnsureVitals(false);
            vitals.RestoreFullHealth();
            HealthChanged?.Invoke(vitals.CurrentHealth, vitals.MaximumHealth);
            EnsureStateMachine();
            SetState(isCaught
                ? CreatureBehaviorState.Caught
                : CreatureBehaviorState.Idle);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null)
            {
                return;
            }

            // Projectiles already submit their explicit weapon damage through
            // IMonsterDamageable. Do not count the same impact a second time.
            if (collision.collider != null
                && collision.collider.GetComponentInParent<BallisticProjectile>()
                    != null)
            {
                return;
            }

            Vector3 collisionPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;
            ApplyCollisionImpulse(collision.impulse.magnitude, collisionPoint);
        }

        private void TickIdle(float deltaTime)
        {
            motor?.Stop();
        }

        private void TickCaught(float deltaTime)
        {
            motor?.Stop();
        }

        private void TickWander(float deltaTime)
        {
            if (!EnsureNavigationReady())
            {
                motor?.Stop();
                return;
            }

            // Only pick a new leg once the previous one is done, and only when the
            // navigator's own cooldown allows another search.
            if (!navigator.HasActivePath
                && motor.IsGrounded
                && navigator.ShouldReplan(Vector3Int.zero, Time.time)
                && TryResolveStartNode(out Vector3Int start)
                && navigator.TrySampleWanderTarget(
                    start,
                    bodyBox,
                    Time.time,
                    out Vector3Int wanderTarget))
            {
                PlanPath(start, wanderTarget);
            }

            FollowPath(deltaTime);
        }

        private void TickPursue(float deltaTime)
        {
            if (!EnsureNavigationReady() || playerFoot == null)
            {
                motor?.Stop();
                return;
            }

            Vector3Int target = FootNodeFromWorld(playerFoot.position);

            // Replanning while airborne would resample the foot node mid-jump and
            // discard a route the creature is still committed to.
            bool canReplan = motor.IsGrounded;
            if (canReplan
                && navigator.ShouldReplan(target, Time.time)
                && TryResolveStartNode(out Vector3Int start))
            {
                PlanPath(start, target);
            }

            FollowPath(deltaTime);
        }

        private void PlanPath(Vector3Int start, Vector3Int target)
        {
            navigator.MoveTo(start, target, bodyBox, Time.time);
            lastVisitedNodeCount = navigator.LastVisitedNodeCount;
            ResetStuckWatch();
        }

        /// <summary>
        /// Steers along the planned route. Height differences the whole-cube voxel
        /// graph reports become jump requests, and a node the creature fails to
        /// approach triggers a recovery jump for the small obstacles that graph
        /// cannot see, such as an interpolated ledge between two same-layer nodes.
        /// </summary>
        private void FollowPath(float deltaTime)
        {
            Vector3 footVoxel = WorldToVoxelPosition(transform.position);
            // Stay under one cell so a corner node is never skipped, yet allow a
            // wide body that cannot centre itself on a node to still advance.
            float tolerance = Mathf.Clamp(
                bodyBox.WidthInVoxels * 0.5f,
                0.6f,
                0.95f);
            if (!navigator.TryGetSteering(
                footVoxel,
                tolerance,
                out Vector3Int nextNode,
                out int riseInLayers))
            {
                motor.Stop();
                return;
            }

            Vector3 up = Terrain != null
                ? Terrain.TerrainTransform.up
                : Vector3.up;
            Vector3 targetPosition = WorldFromFootNode(nextNode);
            Vector3 direction = Vector3.ProjectOnPlane(
                targetPosition - transform.position,
                up);
            motor.MoveTowards(
                direction,
                up,
                navigation.MoveSpeed * VoxelScale,
                navigation.Acceleration * VoxelScale);

            // A rise the creature can simply step over must not become a jump. The
            // interpolated terrain quantises into alternating voxel layers, so a
            // visually flat floor still yields plenty of single-layer edges.
            if (riseInLayers > navigation.StepUpHeight)
            {
                motor.RequestJump(
                    riseInLayers * VoxelScale,
                    ClimbCommandId(nextNode));
            }

            UpdateStuckWatch(deltaTime);
        }

        /// <summary>
        /// Stable jump identifier for a climb onto a node. Distinct from recovery
        /// jump identifiers so the two never cancel one another.
        /// </summary>
        private static int ClimbCommandId(Vector3Int node)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + node.x;
                hash = hash * 31 + node.y;
                hash = hash * 31 + node.z;
                return hash * 2;
            }
        }

        /// <summary>
        /// Detects a creature that is being told to move but is not moving, which
        /// means terrain blocks a step the voxel graph accepted.
        /// <para>
        /// Sampled on a long interval and measured as a fraction of the commanded
        /// speed. Comparing against the animation reference speed would mix two
        /// unrelated metrics, and a short interval mistakes ordinary turning, crowd
        /// contact and slope friction for being blocked.
        /// </para>
        /// </summary>
        private void UpdateStuckWatch(float deltaTime)
        {
            // Airborne time is never evidence of being stuck.
            if (!motor.IsGrounded)
            {
                stuckWatchTime = 0f;
                return;
            }

            if (motor.CommandedSpeedFraction >= navigation.StuckSpeedFraction)
            {
                stuckWatchTime = 0f;
                stuckJumpCount = 0;
                return;
            }

            stuckWatchTime += deltaTime;
            if (stuckWatchTime < navigation.StuckCheckInterval)
            {
                return;
            }

            stuckWatchTime = 0f;
            if (stuckJumpCount >= navigation.StuckJumpAttempts)
            {
                navigator.Clear();
                ResetStuckWatch();
                return;
            }

            stuckJumpCount++;
            // Attempt-numbered identifier so each recovery is one distinct jump that
            // never collides with a climb identifier.
            motor.RequestJump(
                Mathf.Max(1, navigation.MaximumJumpHeight) * VoxelScale,
                stuckJumpCount * 2 - 1);
        }

        private void ResetStuckWatch()
        {
            stuckWatchTime = 0f;
            stuckJumpCount = 0;
        }

        private float VoxelScale => Terrain != null ? Terrain.VoxelSize : 1f;

        private bool EnsureNavigationReady()
        {
            // The tick may be reached before Awake has resolved references, so
            // recover them here the same way EnsureReady does.
            ResolveReferences();
            if (Terrain == null || motor == null)
            {
                return false;
            }

            if (navigation == null)
            {
                navigation = new CreatureNavigationProfile();
            }

            if (!hasBodyBox)
            {
                bodyBox = ResolveBodyBox();
                hasBodyBox = true;
            }

            if (navigator == null)
            {
                nodeMaker = new VoxelPathNodeMaker(
                    new VoxelTerrainSolidityQuery(Terrain));
                pathfinder = new VoxelPathfinder(nodeMaker);
                navigator = new CreatureNavigator(
                    nodeMaker,
                    pathfinder,
                    navigation,
                    new System.Random(GetInstanceID()));
                ResetStuckWatch();
            }

            return true;
        }

        /// <summary>
        /// Sizes the navigation body from the authored body collider, skipping the
        /// smaller crowd collider that only separates creatures from each other.
        /// Dimensions are read from the collider itself rather than its world
        /// bounds, so a creature knocked onto its side keeps its planning size.
        /// </summary>
        private CreatureBodyBox ResolveBodyBox()
        {
            Collider[] colliders = GetComponents<Collider>();
            float width = 0f;
            float height = 0f;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider candidate = colliders[i];
                if (candidate == null
                    || candidate.isTrigger
                    || (motor != null && candidate == motor.CrowdCollider))
                {
                    continue;
                }

                if (!TryMeasureCollider(
                    candidate,
                    out float candidateWidth,
                    out float candidateHeight))
                {
                    continue;
                }

                width = Mathf.Max(width, candidateWidth);
                height = Mathf.Max(height, candidateHeight);
            }

            if (width <= 0f || height <= 0f)
            {
                return new CreatureBodyBox(1, 2);
            }

            return CreatureBodyBox.FromMetricSize(width, height, VoxelScale);
        }

        private static bool TryMeasureCollider(
            Collider collider,
            out float width,
            out float height)
        {
            Vector3 scale = collider.transform.lossyScale;
            float horizontalScale = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.z));
            float verticalScale = Mathf.Abs(scale.y);
            switch (collider)
            {
                case CapsuleCollider capsule:
                    width = capsule.radius * 2f * horizontalScale;
                    // Unity clamps a capsule's height to its diameter.
                    height = Mathf.Max(capsule.height, capsule.radius * 2f)
                        * verticalScale;
                    return true;
                case BoxCollider box:
                    width = Mathf.Max(box.size.x, box.size.z) * horizontalScale;
                    height = box.size.y * verticalScale;
                    return true;
                case SphereCollider sphere:
                    width = sphere.radius * 2f * horizontalScale;
                    height = sphere.radius * 2f * verticalScale;
                    return true;
                default:
                    // Meshes and other shapes have no authored extents to read, so
                    // fall back to local bounds, which ignore world rotation.
                    Bounds local = collider is MeshCollider mesh
                        && mesh.sharedMesh != null
                            ? mesh.sharedMesh.bounds
                            : default;
                    width = Mathf.Max(local.size.x, local.size.z) * horizontalScale;
                    height = local.size.y * verticalScale;
                    return width > 0f && height > 0f;
            }
        }

        /// <summary>
        /// Finds the foot node the creature currently occupies. Physics owns the
        /// real placement, so a sampled position that the whole-cube graph rejects
        /// is snapped to a nearby standable node instead of failing. The tolerance
        /// stays here and never leaks into successor generation.
        /// </summary>
        private bool TryResolveStartNode(out Vector3Int start)
        {
            start = FootNodeFromWorld(transform.position);
            nodeMaker.BeginSearch(bodyBox, navigation);
            if (nodeMaker.TryClassify(start, out PathNodeType type)
                && type == PathNodeType.Walkable)
            {
                return true;
            }

            for (int verticalOffset = 0; verticalOffset <= 2; verticalOffset++)
            {
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    var candidate = new Vector3Int(
                        start.x,
                        start.y + verticalOffset * sign,
                        start.z);
                    if (nodeMaker.TryClassify(candidate, out PathNodeType candidateType)
                        && candidateType == PathNodeType.Walkable)
                    {
                        start = candidate;
                        return true;
                    }

                    if (verticalOffset == 0)
                    {
                        break;
                    }
                }
            }

            return false;
        }

        private Vector3Int FootNodeFromWorld(Vector3 worldPosition)
        {
            Vector3 voxel = WorldToVoxelPosition(worldPosition);
            return new Vector3Int(
                Mathf.RoundToInt(voxel.x),
                Mathf.FloorToInt(voxel.y + 0.5f),
                Mathf.RoundToInt(voxel.z));
        }

        private Vector3 WorldFromFootNode(Vector3Int node)
        {
            IVoxelTerrain terrain = Terrain;
            if (terrain == null)
            {
                return node;
            }

            return terrain.TerrainTransform.TransformPoint(
                (Vector3)node * terrain.VoxelSize);
        }

        private void EnterAttack()
        {
            stateSeconds = 0f;
            attackApplied = false;
            nextAttackTime = Time.time + attackCooldown;
            // One increment per swing, including the first. The animation bridge
            // watches this so every settlement replays the attack clip instead of
            // the clip playing once while the attack keeps cycling silently.
            attackSwingCount++;
            motor?.Stop();
            SoundEffectEvents.RequestPlay(
                AudioAssets != null ? AudioAssets.CreatureAttack : null,
                transform.position);
        }

        private void TickAttack(float deltaTime)
        {
            motor?.Stop();
            if (playerFoot == null)
            {
                SetState(CreatureBehaviorState.Idle);
                return;
            }

            Vector3 direction = playerFoot.position - transform.position;
            Vector3 up = Terrain != null
                ? Terrain.TerrainTransform.up
                : Vector3.up;
            motor?.Face(direction, up);
            stateSeconds += deltaTime;
            if (!attackApplied && stateSeconds >= attackWindup)
            {
                attackApplied = true;
                ApplyAttack(direction);
            }

            if (stateSeconds >= attackDuration && Time.time >= nextAttackTime)
            {
                EnterAttack();
            }
        }

        private void ApplyAttack(Vector3 direction)
        {
            float voxelSize = Terrain != null ? Terrain.VoxelSize : 1f;
            float allowedDistance = attackDistance * voxelSize + 0.5f;
            if (direction.sqrMagnitude > allowedDistance * allowedDistance) return;
            if (!MeleeCombat.TryFindDamageable(playerFoot, out IDamageable target)) return;
            if (!target.IsAlive || target.Owner == gameObject) return;

            var damage = new DamageInfo(
                attackDamage,
                gameObject,
                playerFoot.position,
                direction,
                attackImpulse);
            if (target.ReceiveDamage(damage))
            {
                Vector3 hitPosition = target.Owner != null
                    ? target.Owner.transform.position
                    : playerFoot.position;
                SoundEffectEvents.RequestPlay(
                    AudioAssets != null
                        ? AudioAssets.CreatureHitPlayer
                        : null,
                    hitPosition);
            }
        }

        private void EnterHurt()
        {
            stateSeconds = 0f;
            motor?.Stop();
        }

        private void TickHurt(float deltaTime)
        {
            motor?.Stop();
            stateSeconds += deltaTime;
            if (stateSeconds < hurtDuration) return;

            if (!EnsureReady())
            {
                SetState(CreatureBehaviorState.Idle);
                return;
            }

            float playerDistance = GetPlayerDistance();
            SetState(SelectState(playerDistance));
        }

        private void EnterDead()
        {
            isPursuitEngaged = false;
            motor?.Stop();
            if (deathCleanupScheduled)
            {
                return;
            }

            deathCleanupScheduled = true;
            RemoveDeadBodyPhysics();
            if (Application.isPlaying)
            {
                Destroy(gameObject, DeadDespawnDelay);
            }
        }

        private void TickDead(float deltaTime)
        {
            motor?.Stop();
        }

        private void RemoveDeadBodyPhysics()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider deadCollider = colliders[i];
                deadCollider.enabled = false;
                DestroyPhysicsComponent(deadCollider);
            }

            Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody deadBody = bodies[i];
                deadBody.detectCollisions = false;
                deadBody.useGravity = false;
                deadBody.velocity = Vector3.zero;
                deadBody.angularVelocity = Vector3.zero;
                deadBody.isKinematic = true;
                DestroyPhysicsComponent(deadBody);
            }

            if (motor != null)
            {
                motor.enabled = false;
            }
        }

        private static void DestroyPhysicsComponent(Component component)
        {
            if (Application.isPlaying)
            {
                Destroy(component);
            }
            else
            {
                DestroyImmediate(component);
            }
        }

        private bool EnsureReady()
        {
            ResolveReferences();
            return playerFoot != null && motor != null;
        }

        private void ResolveReferences()
        {
            if (voxelTerrain == null)
            {
                if (caveWorld != null)
                {
                    voxelTerrain = caveWorld;
                }
                else
                {
                    MonoBehaviour[] candidates =
                        FindObjectsOfType<MonoBehaviour>();
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (candidates[i] is IVoxelTerrain candidate)
                        {
                            voxelTerrain = candidate;
                            caveWorld =
                                candidate as MinecraftCaveInfiniteWorld;
                            break;
                        }
                    }
                }
            }

            if (playerFoot == null)
            {
                VoxelPlayerController player =
                    FindObjectOfType<VoxelPlayerController>();
                if (player != null)
                {
                    playerFoot = player.transform;
                }
            }

            if (motor == null)
            {
                motor = GetComponent<CreaturePhysicsMotor>();
            }
        }

        private void SetState(CreatureBehaviorState value)
        {
            EnsureStateMachine();
            stateMachine.Change(value);
        }

        private void BuildStateMachine()
        {
            stateMachine = new CharacterStateMachine<CreatureBehaviorState>();
            stateMachine.Add(new CreatureState(this, CreatureBehaviorState.Idle, TickIdle));
            stateMachine.Add(new CreatureState(this, CreatureBehaviorState.Wander, TickWander));
            stateMachine.Add(new CreatureState(this, CreatureBehaviorState.Pursue, TickPursue));
            stateMachine.Add(new CreatureState(
                this, CreatureBehaviorState.Attack, TickAttack, EnterAttack));
            stateMachine.Add(new CreatureState(
                this, CreatureBehaviorState.Hurt, TickHurt, EnterHurt));
            stateMachine.Add(new CreatureState(
                this, CreatureBehaviorState.Dead, TickDead, EnterDead));
            stateMachine.Add(new CreatureState(
                this, CreatureBehaviorState.Caught, TickCaught));
        }

        private void EnsureStateMachine()
        {
            if (stateMachine == null) BuildStateMachine();
            if (!stateMachine.IsRunning)
            {
                stateMachine.Start(!vitals.IsAlive
                    ? CreatureBehaviorState.Dead
                    : isCaught
                        ? CreatureBehaviorState.Caught
                        : CreatureBehaviorState.Idle);
            }
        }

        private void EnsureVitals(bool refill)
        {
            if (vitals == null)
            {
                vitals = new CharacterVitals();
                refill = true;
            }
            vitals.Initialize(maximumHealth, refill);
        }

        private void EnsureHealthBar()
        {
            MonsterHealthBar healthBar = GetComponent<MonsterHealthBar>();
            if (healthBar == null)
            {
                healthBar = gameObject.AddComponent<MonsterHealthBar>();
            }

            healthBar.Bind(this);
        }

        private void EnterState(CreatureBehaviorState value)
        {
            currentState = value;
            motor?.Stop();

            // Only Wander and Pursue follow a route. Every other state drops the
            // plan so returning to movement replans against current terrain.
            if (value != CreatureBehaviorState.Wander
                && value != CreatureBehaviorState.Pursue)
            {
                navigator?.Clear();
                ResetStuckWatch();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebug)
            {
                return;
            }

            float scale = Terrain != null ? Terrain.VoxelSize : 1f;
            Gizmos.color = currentState switch
            {
                CreatureBehaviorState.Idle => Color.gray,
                CreatureBehaviorState.Wander => Color.yellow,
                CreatureBehaviorState.Pursue => new Color(1f, 0.35f, 0.1f),
                CreatureBehaviorState.Attack => Color.red,
                CreatureBehaviorState.Hurt => Color.magenta,
                CreatureBehaviorState.Dead => Color.black,
                CreatureBehaviorState.Caught => Color.cyan,
                _ => Color.white,
            };
            Gizmos.DrawWireSphere(transform.position, pursuitDistance * scale);
            DrawPathGizmo(scale);
        }

        private void DrawPathGizmo(float scale)
        {
            VoxelPath path = navigator?.CurrentPath;
            if (path == null || path.NodeCount == 0)
            {
                return;
            }

            Gizmos.color = path.ReachesTarget
                ? Color.green
                : new Color(1f, 0.6f, 0f);
            Vector3 previous = WorldFromFootNode(path.Nodes[0]);
            for (int i = 1; i < path.NodeCount; i++)
            {
                Vector3 current = WorldFromFootNode(path.Nodes[i]);
                Gizmos.DrawLine(previous, current);
                Gizmos.DrawWireCube(current, Vector3.one * (scale * 0.3f));
                previous = current;
            }

            if (!path.IsFinished)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(
                    WorldFromFootNode(path.CurrentNode),
                    scale * 0.5f);
            }
        }

        private void OnValidate()
        {
            maximumHealth = Mathf.Max(1f, maximumHealth);
            simulationDistance = Mathf.Max(1f, simulationDistance);
            pursuitDistance = Mathf.Max(0f, pursuitDistance);
            pursuitRetentionDistance = Mathf.Max(
                pursuitDistance,
                pursuitRetentionDistance);
            attackDistance = Mathf.Max(0f, attackDistance);
            collisionFragility = Mathf.Clamp01(collisionFragility);
            minimumDamageImpulse = Mathf.Max(0f, minimumDamageImpulse);
            damagePercentagePerSquaredImpulse =
                Mathf.Max(0f, damagePercentagePerSquaredImpulse);
        }

        private sealed class CreatureState : ICharacterState<CreatureBehaviorState>
        {
            private readonly CreatureBehaviorAgent owner;
            private readonly Action<float> tick;
            private readonly Action enter;

            public CreatureState(
                CreatureBehaviorAgent owner,
                CreatureBehaviorState id,
                Action<float> tick,
                Action enter = null)
            {
                this.owner = owner;
                Id = id;
                this.tick = tick;
                this.enter = enter;
            }

            public CreatureBehaviorState Id { get; }

            public void Enter()
            {
                owner.EnterState(Id);
                enter?.Invoke();
            }

            public void Tick(float deltaTime)
            {
                tick(deltaTime);
            }

            public void Exit()
            {
            }
        }
    }
}
