using System;
using System.Collections.Generic;
using Supernova.Gameplay;
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
        public const float DefaultWanderRadius = 32f;
        public const float DefaultWanderLegRadius = 8f;
        public const float PursuitPathRefreshInterval = 1f;
        public const float DeadDespawnDelay = 5f;
        public const float DefaultMinimumCollisionDamageImpulse = 3f;
        private const int MaximumNavigationDistanceInChunks = 4;
        private const int WanderCandidateAttempts = 4;
        private const float WanderRetryJitterFraction = 0.35f;
        private const float MaximumInitialWanderDelay = 2f;
        private const float MaximumInitialPursuitDelay =
            PursuitPathRefreshInterval;

        [Header("References")]
        [SerializeField] private MinecraftCaveInfiniteWorld caveWorld;
        [SerializeField] private Transform playerFoot;
        [SerializeField] private CreatureVoxelShapeAuthoring shapeAuthoring;
        [SerializeField] private CreaturePhysicsMotor motor;

        [Header("Behavior Distances (voxels)")]
        [SerializeField, Min(1f)] private float simulationDistance = DefaultSimulationDistance;
        [SerializeField, Min(0f)] private float pursuitDistance = DefaultPursuitDistance;
        [SerializeField, Min(0f)]
        private float pursuitRetentionDistance =
            DefaultPursuitRetentionDistance;
        [SerializeField, Min(0f)] private float attackDistance = 2f;
        [SerializeField, Min(1f)] private float wanderRadius = DefaultWanderRadius;
        [SerializeField, Min(1f)]
        private float wanderLegRadius = DefaultWanderLegRadius;

        [Header("Navigation")]
        [SerializeField] private CreatureNavigationSettings navigation = new CreatureNavigationSettings();
        [SerializeField, Min(0.05f)] private float wanderRetryInterval = 1.5f;
        [SerializeField, Range(1, 32)] private int wanderVerticalSearch = 8;
        [SerializeField] private float solidDensityThreshold;
        [SerializeField, Range(0.5f, 1f)]
        private float arrivalToleranceInVoxels = 0.999f;
        [SerializeField, Range(0.01f, 0.5f)]
        private float actualMovementThreshold = 0.08f;

        [Header("Navigation Recovery")]
        [SerializeField, Min(0.1f)] private float stuckSampleInterval = 0.5f;
        [SerializeField, Range(0.01f, 1f)]
        private float stuckMinimumProgressInVoxels = 0.15f;
        [SerializeField, Range(1, 6)] private int stuckSamplesBeforeRecovery = 2;
        [SerializeField, Min(0.1f)] private float stuckRecoveryDuration = 2f;
        [SerializeField, Range(0f, 60f)] private float stuckSteeringAngle = 28f;

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

        [Header("Debug")]
        [SerializeField] private bool drawDebug = true;
        [SerializeField] private CreatureBehaviorState currentState;
        [SerializeField] private int lastExpandedNodeCount;
        [SerializeField] private Vector3Int observedSupport;
        [SerializeField] private int pathIndex;


        private IVoxelTerrain Terrain => voxelTerrain ?? caveWorld;
        private readonly List<Vector3Int> path = new List<Vector3Int>();
        private readonly List<Vector3Int> pathSearchBuffer =
            new List<Vector3Int>();
        private System.Random random;

        private IVoxelTerrain voxelTerrain;
        private MinecraftCaveVoxelQuery query;
        private CreatureVoxelShape shape;
        private Vector3Int currentSupport;
        private Vector3Int currentTarget;
        private float nextPursuitPathRefreshTime;
        private float pursuitRefreshPhase;
        private float nextWanderAttemptTime;
        private float navigationIntervalMultiplier = 1f;
        private float forcedIdleUntil;
        private bool configurationErrorLogged;
        private int movementCommandId;
        private int navigationRevision = int.MinValue;
        private CharacterStateMachine<CreatureBehaviorState> stateMachine;
        private float stateSeconds;
        private float nextAttackTime;
        private bool attackApplied;
        private bool deathCleanupScheduled;
        private Vector3 movementSamplePosition;
        private float nextMovementSampleTime;
        private float recoverySteeringUntil;
        private int stagnantMovementSamples;
        private int recoveryAttempt;
        private bool isCaught;
        private bool isPursuitEngaged;

        public event Action<float, float> HealthChanged;
        public event Action<float, Vector3> Damaged;

        public CreatureBehaviorState CurrentState => currentState;
        public IReadOnlyList<Vector3Int> CurrentPath => path;
        public Vector3Int CurrentSupport => currentSupport;
        public Vector3Int ObservedSupport => observedSupport;
        public Vector3Int CurrentTarget => currentTarget;
        public int CurrentPathIndex => pathIndex;
        public int LastExpandedNodeCount => lastExpandedNodeCount;
        public GameObject Owner => gameObject;
        public float CurrentHealth => vitals != null ? vitals.CurrentHealth : 0f;
        public float MaximumHealth => vitals != null ? vitals.MaximumHealth : maximumHealth;
        public bool IsAlive => vitals != null && vitals.IsAlive;
        public bool IsCaught => isCaught;
        public GameObject CollisionImpulseOwner => gameObject;
        public bool IsActuallyMoving =>
            motor != null
            && motor.NormalizedHorizontalSpeed >= actualMovementThreshold;

        public IVoxelTerrain VoxelTerrain => Terrain;
        public MinecraftCaveInfiniteWorld CaveWorld => caveWorld;
        public Transform PlayerFoot => playerFoot;
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
            query = null;
            navigationRevision = int.MinValue;
            configurationErrorLogged = false;
            ResolveReferences();
            if (Terrain != null)
            {
                SynchronizeLogicalPosition();
            }
        }


        private void Awake()
        {
            random = new System.Random(unchecked(GetInstanceID() * 486187739));
            pursuitRefreshPhase = (float)random.NextDouble();
            ResolveReferences();
            EnsureVitals(true);
            EnsureHealthBar();
            BuildStateMachine();
        }

        private void OnEnable()
        {
            ClearNavigation();
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

            observedSupport = WorldFootPositionToSupport(transform.position);
            AdvanceLogicalPath();
            UpdateMovementProgress();
            if (pathIndex >= path.Count)
            {
                currentSupport = observedSupport;
            }

            Vector3Int playerSupport = WorldFootPositionToSupport(playerFoot.position);
            GetPlayerDistances(
                observedSupport,
                playerSupport,
                out float playerHorizontalDistance,
                out float playerSpatialDistance);
            navigationIntervalMultiplier =
                GetNavigationIntervalMultiplier(playerHorizontalDistance);
            if (navigationIntervalMultiplier <= 0f)
            {
                isPursuitEngaged = false;
                if (currentState != CreatureBehaviorState.Idle)
                {
                    SetState(CreatureBehaviorState.Idle);
                }
                else
                {
                    motor.Stop();
                }
                return;
            }

            CreatureBehaviorState desiredState = SelectState(
                playerSpatialDistance);
            if (desiredState != currentState)
            {
                SetState(desiredState);
            }

            stateMachine.Tick(Time.deltaTime);
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

            return Time.time < forcedIdleUntil
                ? CreatureBehaviorState.Idle
                : CreatureBehaviorState.Wander;
        }

        private static void GetPlayerDistances(
            Vector3Int creatureSupport,
            Vector3Int playerSupport,
            out float horizontalDistance,
            out float spatialDistance)
        {
            Vector3Int difference = playerSupport - creatureSupport;
            horizontalDistance = new Vector2(
                difference.x,
                difference.z).magnitude;
            spatialDistance = ((Vector3)difference).magnitude;
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
            UpdateWander();
            SubmitCurrentStep();
        }

        private void TickPursue(float deltaTime)
        {
            Vector3Int playerSupport = WorldFootPositionToSupport(playerFoot.position);
            UpdatePursue(playerSupport);
            SubmitCurrentStep();
        }

        private void EnterAttack()
        {
            stateSeconds = 0f;
            attackApplied = false;
            nextAttackTime = Time.time + attackCooldown;
            ClearNavigation();
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
            target.ReceiveDamage(damage);
        }

        private void EnterHurt()
        {
            stateSeconds = 0f;
            ClearNavigation();
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

            Vector3Int playerSupport = WorldFootPositionToSupport(playerFoot.position);
            GetPlayerDistances(
                currentSupport,
                playerSupport,
                out _,
                out float playerSpatialDistance);
            SetState(SelectState(playerSpatialDistance));
        }

        private void EnterDead()
        {
            isPursuitEngaged = false;
            ClearNavigation();
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

        private void UpdateWander()
        {
            if (pathIndex < path.Count || Time.time < nextWanderAttemptTime)
            {
                return;
            }

            float retryInterval = GetRandomizedWanderRetryInterval(
                wanderRetryInterval * navigationIntervalMultiplier);
            nextWanderAttemptTime = Time.time + retryInterval;
            float legRadius = Mathf.Min(wanderRadius, wanderLegRadius);
            for (int attempt = 0;
                attempt < WanderCandidateAttempts;
                attempt++)
            {
                Vector2 offset = RandomInsideCircle(legRadius);
                int randomY = random.Next(
                    -wanderVerticalSearch,
                    wanderVerticalSearch + 1);
                Vector3Int sample = currentSupport + new Vector3Int(
                    Mathf.RoundToInt(offset.x),
                    randomY,
                    Mathf.RoundToInt(offset.y));
                if (TryFindStandableVertically(
                        sample,
                        wanderVerticalSearch,
                        out Vector3Int target)
                    && target != currentSupport
                    && Vector3.Distance(currentSupport, target) <= legRadius
                    && BuildPath(target))
                {
                    return;
                }
            }

            forcedIdleUntil = nextWanderAttemptTime;
            SetState(CreatureBehaviorState.Idle);
        }

        private void UpdatePursue(Vector3Int requestedTarget)
        {
            if (Time.time < nextPursuitPathRefreshTime)
            {
                return;
            }

            if (nextPursuitPathRefreshTime <= 0f)
            {
                nextPursuitPathRefreshTime =
                    Time.time + PursuitPathRefreshInterval;
            }
            else
            {
                do
                {
                    nextPursuitPathRefreshTime +=
                        PursuitPathRefreshInterval;
                }
                while (nextPursuitPathRefreshTime <= Time.time);
            }

            if (TryFindNearestStandable(requestedTarget, 2, 4, out Vector3Int target))
            {
                BuildPath(target, true, true);
            }
        }

        private bool BuildPath(
            Vector3Int target,
            bool preserveNavigationOnFailure = false,
            bool allowPartialPath = false)
        {
            observedSupport = WorldFootPositionToSupport(transform.position);
            bool found = allowPartialPath
                ? CreatureVoxelNavigation.TryFindPursuitPath(
                    query,
                    shape,
                    navigation,
                    observedSupport,
                    target,
                    pathSearchBuffer,
                    out lastExpandedNodeCount,
                    out _)
                : CreatureVoxelNavigation.TryFindPath(
                    query,
                    shape,
                    navigation,
                    observedSupport,
                    target,
                    pathSearchBuffer,
                    out lastExpandedNodeCount);
            if (!found)
            {
                if (!preserveNavigationOnFailure)
                {
                    ClearNavigation();
                }
                return false;
            }

            currentSupport = observedSupport;
            currentTarget = target;
            if (!allowPartialPath)
            {
                CreatureVoxelNavigation.SimplifyPath(
                    query,
                    shape,
                    navigation,
                    pathSearchBuffer);
            }
            path.Clear();
            path.AddRange(pathSearchBuffer);
            pathIndex = path.Count > 1 ? 1 : path.Count;
            movementCommandId++;
            BeginMovementProgressTracking();
            return true;
        }

        private void SynchronizeLogicalPosition()
        {
            observedSupport = WorldFootPositionToSupport(transform.position);
            currentSupport = observedSupport;
        }

        private void AdvanceLogicalPath()
        {
            while (pathIndex < path.Count && IsLogicallyAt(path[pathIndex]))
            {
                currentSupport = path[pathIndex];
                pathIndex++;
                movementCommandId++;
            }
        }

        private void BeginMovementProgressTracking()
        {
            movementSamplePosition = transform.position;
            nextMovementSampleTime =
                Time.time + Mathf.Max(0.1f, stuckSampleInterval);
            stagnantMovementSamples = 0;
        }

        private void UpdateMovementProgress()
        {
            bool expectsMovement = motor != null
                && motor.HasCommand
                && pathIndex < path.Count
                && (currentState == CreatureBehaviorState.Wander
                    || currentState == CreatureBehaviorState.Pursue);
            if (!expectsMovement)
            {
                ResetMovementProgress();
                return;
            }
            if (nextMovementSampleTime <= 0f)
            {
                BeginMovementProgressTracking();
                return;
            }
            if (Time.time < nextMovementSampleTime)
            {
                return;
            }

            Vector3 worldUp = Terrain != null
                ? Terrain.TerrainTransform.up
                : Vector3.up;
            float progress = Vector3.ProjectOnPlane(
                transform.position - movementSamplePosition,
                worldUp).magnitude;
            float minimumProgress = Mathf.Max(
                0.01f,
                stuckMinimumProgressInVoxels
                    * (Terrain != null ? Terrain.VoxelSize : 1f));
            if (progress >= minimumProgress)
            {
                stagnantMovementSamples = 0;
                recoverySteeringUntil = 0f;
            }
            else
            {
                stagnantMovementSamples++;
            }

            movementSamplePosition = transform.position;
            nextMovementSampleTime =
                Time.time + Mathf.Max(0.1f, stuckSampleInterval);
            if (stagnantMovementSamples
                >= Mathf.Max(1, stuckSamplesBeforeRecovery))
            {
                BeginStuckRecovery();
            }
        }

        private void BeginStuckRecovery()
        {
            stagnantMovementSamples = 0;
            recoveryAttempt++;
            ClearNavigation();
            recoverySteeringUntil =
                Time.time + Mathf.Max(0.1f, stuckRecoveryDuration);
            nextWanderAttemptTime = 0f;
            movementSamplePosition = transform.position;
            nextMovementSampleTime =
                Time.time + Mathf.Max(0.1f, stuckSampleInterval);
        }

        private Vector3 ApplyRecoverySteering(
            Vector3 direction,
            Vector3 worldUp)
        {
            if (Time.time >= recoverySteeringUntil
                || stuckSteeringAngle <= 0f
                || direction.sqrMagnitude <= 0.0001f)
            {
                return direction;
            }

            int phase = Mathf.FloorToInt(
                (Time.time + recoveryAttempt * 0.173f) / 0.35f);
            float signedAngle = (phase & 1) == 0
                ? stuckSteeringAngle
                : -stuckSteeringAngle;
            Vector3 up = worldUp.sqrMagnitude > 0.5f
                ? worldUp.normalized
                : Vector3.up;
            return Quaternion.AngleAxis(signedAngle, up) * direction;
        }

        private void ResetMovementProgress()
        {
            movementSamplePosition = transform.position;
            nextMovementSampleTime = 0f;
            stagnantMovementSamples = 0;
        }

        private bool IsLogicallyAt(Vector3Int support)
        {
            if (observedSupport == support)
            {
                return true;
            }

            Vector3 target = SupportToWorldFootPosition(support);
            float tolerance = arrivalToleranceInVoxels * Terrain.VoxelSize;
            return (transform.position - target).sqrMagnitude < tolerance * tolerance;
        }

        private void SubmitCurrentStep()
        {
            if (pathIndex >= path.Count)
            {
                motor.Stop();
                return;
            }

            Vector3Int nextSupport = path[pathIndex];
            Vector3Int step = nextSupport - currentSupport;
            bool adjacent = Mathf.Abs(step.x) <= 1
                && Mathf.Abs(step.z) <= 1;
            if (adjacent)
            {
                if (!CreatureVoxelNavigation.TryResolveTransition(
                        query,
                        shape,
                        navigation,
                        currentSupport,
                        step,
                        out Vector3Int resolved,
                        out _)
                    || resolved != nextSupport)
                {
                    ClearNavigation();
                    return;
                }
            }
            else if (!CreatureVoxelNavigation
                .CanTraverseDirectHorizontalSegment(
                    query,
                    shape,
                    navigation,
                    currentSupport,
                    nextSupport))
            {
                ClearNavigation();
                return;
            }

            Vector3 worldUp = Terrain.TerrainTransform.up;
            Vector3 targetWorldPosition =
                SupportToWorldFootPosition(nextSupport);
            Vector3 worldDirection = Vector3.ProjectOnPlane(
                targetWorldPosition - transform.position,
                worldUp);
            if (worldDirection.sqrMagnitude <= 0.0001f)
            {
                Vector3 localDirection =
                    new Vector3(step.x, 0f, step.z);
                worldDirection =
                    Terrain.TerrainTransform.TransformDirection(
                        localDirection);
            }
            worldDirection = ApplyRecoverySteering(
                worldDirection,
                worldUp);

            bool usesTraversalLink = adjacent
                && query.TryGetTraversalLink(
                    currentSupport,
                    nextSupport,
                    out CreatureTraversalLink link)
                && CreatureVoxelNavigation.IsTraversalLinkAllowed(
                    link,
                    navigation);
            CreatureMovementCommand command = usesTraversalLink
                ? CreatureMovementCommand.TraverseTo(
                    movementCommandId,
                    worldDirection,
                    worldUp,
                    targetWorldPosition)
                : new CreatureMovementCommand(
                    movementCommandId,
                    worldDirection,
                    worldUp,
                    step.y);
            motor.Submit(command);
        }

        private bool EnsureReady()
        {
            ResolveReferences();
            IVoxelTerrain terrain = Terrain;
            if (terrain == null
                || terrain.World == null
                || playerFoot == null
                || motor == null
                || shape == null
                || shape.IsEmpty)
            {
                return false;
            }

            if (!Mathf.Approximately(
                shape.BakedVoxelSize,
                terrain.VoxelSize))
            {
                if (!configurationErrorLogged)
                {
                    Debug.LogError(
                        $"{name}: baked creature voxel size ({shape.BakedVoxelSize}) must match "
                        + $"world voxel size ({terrain.VoxelSize}).",
                        this);
                    configurationErrorLogged = true;
                }
                return false;
            }

            if (query == null)
            {
                query = new MinecraftCaveVoxelQuery(
                    terrain,
                    solidDensityThreshold);
                navigationRevision = query.NavigationRevision;
            }
            else if (navigationRevision != query.NavigationRevision)
            {
                navigationRevision = query.NavigationRevision;
                nextWanderAttemptTime = 0f;
                ClearNavigation();
            }
            return true;
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
                query = null;
                navigationRevision = int.MinValue;
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

            if (shapeAuthoring == null)
            {
                shapeAuthoring =
                    GetComponent<CreatureVoxelShapeAuthoring>();
            }

            CreatureVoxelShape resolvedShape = shapeAuthoring != null
                ? shapeAuthoring.Shape
                : null;
            if (resolvedShape != shape)
            {
                shape = resolvedShape;
                query = null;
                currentSupport = Terrain != null
                    ? WorldFootPositionToSupport(transform.position)
                    : Vector3Int.zero;
            }
        }

        private bool TryFindStandableVertically(
            Vector3Int centre,
            int radius,
            out Vector3Int support)
        {
            for (int distance = 0; distance <= radius; distance++)
            {
                Vector3Int above = centre + Vector3Int.up * distance;
                if (CreatureVoxelNavigation.IsStandable(query, shape, above))
                {
                    support = above;
                    return true;
                }

                if (distance == 0)
                {
                    continue;
                }

                Vector3Int below = centre + Vector3Int.down * distance;
                if (CreatureVoxelNavigation.IsStandable(query, shape, below))
                {
                    support = below;
                    return true;
                }
            }

            support = default;
            return false;
        }

        private bool TryFindNearestStandable(
            Vector3Int centre,
            int horizontalRadius,
            int verticalRadius,
            out Vector3Int support)
        {
            float bestDistanceSquared = float.PositiveInfinity;
            support = default;
            bool found = false;
            for (int z = -horizontalRadius; z <= horizontalRadius; z++)
            {
                for (int x = -horizontalRadius; x <= horizontalRadius; x++)
                {
                    if (x * x + z * z > horizontalRadius * horizontalRadius)
                    {
                        continue;
                    }

                    Vector3Int column = centre + new Vector3Int(x, 0, z);
                    if (!TryFindStandableVertically(column, verticalRadius, out Vector3Int candidate))
                    {
                        continue;
                    }

                    float distanceSquared = (candidate - centre).sqrMagnitude;
                    if (distanceSquared >= bestDistanceSquared)
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    support = candidate;
                    found = true;
                }
            }

            return found;
        }

        private Vector3Int WorldFootPositionToSupport(Vector3 worldPosition)
        {
            IVoxelTerrain terrain = Terrain;
            Vector3 localVoxel = terrain.TerrainTransform
                .InverseTransformPoint(worldPosition) / terrain.VoxelSize;
            Vector3Int foot = new Vector3Int(
                Mathf.RoundToInt(localVoxel.x),
                Mathf.FloorToInt(localVoxel.y + 0.001f),
                Mathf.RoundToInt(localVoxel.z));
            return foot + Vector3Int.down;
        }

        private Vector3 SupportToWorldFootPosition(Vector3Int support)
        {
            IVoxelTerrain terrain = Terrain;
            Vector3 local = (Vector3)(support + Vector3Int.up)
                * terrain.VoxelSize;
            return terrain.TerrainTransform.TransformPoint(local);
        }

        private Vector2 RandomInsideCircle(float radius)
        {
            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            float distance = Mathf.Sqrt((float)random.NextDouble()) * radius;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        }

        private float GetRandomizedWanderRetryInterval(float baseInterval)
        {
            float jitterMultiplier = Mathf.Lerp(
                1f - WanderRetryJitterFraction,
                1f + WanderRetryJitterFraction,
                (float)random.NextDouble());
            return Mathf.Max(0.05f, baseInterval * jitterMultiplier);
        }

        private float GetInitialWanderDelay()
        {
            float maximumDelay = Mathf.Min(
                MaximumInitialWanderDelay,
                Mathf.Max(0f, wanderRetryInterval));
            return (float)random.NextDouble() * maximumDelay;
        }

        private static float GetNavigationIntervalMultiplier(
            float playerDistanceInVoxels)
        {
            float chunkWidth = VoxelColumnChunkData.Width;
            if (playerDistanceInVoxels > chunkWidth
                * MaximumNavigationDistanceInChunks)
            {
                return 0f;
            }
            if (playerDistanceInVoxels > chunkWidth * 3f)
            {
                return 10f;
            }
            if (playerDistanceInVoxels > chunkWidth * 2f)
            {
                return 5f;
            }
            if (playerDistanceInVoxels > chunkWidth)
            {
                return 2f;
            }

            return 1f;
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
            ClearNavigation();
            if (value == CreatureBehaviorState.Wander)
            {
                nextWanderAttemptTime = Time.time + GetInitialWanderDelay();
            }
            if (value == CreatureBehaviorState.Pursue)
            {
                nextPursuitPathRefreshTime = Time.time
                    + pursuitRefreshPhase * MaximumInitialPursuitDelay;
            }
        }

        private void ClearNavigation()
        {
            path.Clear();
            pathIndex = 0;
            movementCommandId++;
            if (Terrain != null)
            {
                SynchronizeLogicalPosition();
            }
            currentTarget = currentSupport;
            if (motor != null)
            {
                motor.Stop();
            }
            ResetMovementProgress();
            recoverySteeringUntil = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebug || Terrain == null)
            {
                return;
            }

            float scale = Terrain.VoxelSize;
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

            Gizmos.color = Color.cyan;
            for (int i = 1; i < path.Count; i++)
            {
                Gizmos.DrawLine(
                    SupportToWorldFootPosition(path[i - 1]),
                    SupportToWorldFootPosition(path[i]));
            }
        }

        private void OnValidate()
        {
            maximumHealth = Mathf.Max(1f, maximumHealth);
            pursuitDistance = Mathf.Max(0f, pursuitDistance);
            pursuitRetentionDistance = Mathf.Max(
                pursuitDistance,
                pursuitRetentionDistance);
            wanderLegRadius = Mathf.Clamp(
                wanderLegRadius,
                1f,
                Mathf.Max(1f, wanderRadius));
            actualMovementThreshold = Mathf.Clamp(
                actualMovementThreshold,
                0.01f,
                0.5f);
            stuckSampleInterval = Mathf.Max(0.1f, stuckSampleInterval);
            stuckMinimumProgressInVoxels = Mathf.Clamp(
                stuckMinimumProgressInVoxels,
                0.01f,
                1f);
            stuckSamplesBeforeRecovery = Mathf.Clamp(
                stuckSamplesBeforeRecovery,
                1,
                6);
            stuckRecoveryDuration = Mathf.Max(0.1f, stuckRecoveryDuration);
            stuckSteeringAngle = Mathf.Clamp(stuckSteeringAngle, 0f, 60f);
            if (navigation != null)
            {
                navigation.maximumSmoothingLookahead = Mathf.Clamp(
                    navigation.maximumSmoothingLookahead,
                    2,
                    32);
            }
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
