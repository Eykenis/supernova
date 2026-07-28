using System;
using System.Collections.Generic;
using Supernova.Gameplay;
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
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreaturePhysicsMotor))]
    public sealed class CreatureBehaviorAgent : MonoBehaviour, IDamageable
    {
        public const float DefaultSimulationDistance = 160f;
        public const float DefaultPursuitDistance = 32f;
        public const float DefaultWanderRadius = 32f;
        public const float DeadDespawnDelay = 5f;

        [Header("References")]
        [SerializeField] private MinecraftCaveInfiniteWorld caveWorld;
        [SerializeField] private Transform playerFoot;
        [SerializeField] private CreatureVoxelShapeAuthoring shapeAuthoring;
        [SerializeField] private CreaturePhysicsMotor motor;

        [Header("Behavior Distances (voxels)")]
        [SerializeField, Min(1f)] private float simulationDistance = DefaultSimulationDistance;
        [SerializeField, Min(0f)] private float pursuitDistance = DefaultPursuitDistance;
        [SerializeField, Min(0f)] private float attackDistance = 2f;
        [SerializeField, Min(1f)] private float wanderRadius = DefaultWanderRadius;

        [Header("Navigation")]
        [SerializeField] private CreatureNavigationSettings navigation = new CreatureNavigationSettings();
        [SerializeField, Min(0.05f)] private float pursuitReplanInterval = 0.5f;
        [SerializeField, Min(0.05f)] private float wanderRetryInterval = 1.5f;
        [SerializeField, Range(1, 16)] private int wanderRejectionAttempts = 16;
        [SerializeField, Range(1, 32)] private int wanderVerticalSearch = 8;
        [SerializeField] private float solidDensityThreshold;
        [SerializeField, Range(0.5f, 1f)] private float arrivalToleranceInVoxels = 0.999f;

        [Header("Combat")]
        [SerializeField, Min(1f)] private float maximumHealth = 60f;
        [SerializeField] private CharacterVitals vitals = new CharacterVitals();
        [SerializeField, Min(0f)] private float attackDamage = 10f;
        [SerializeField, Min(0.01f)] private float attackWindup = 0.2f;
        [SerializeField, Min(0.02f)] private float attackDuration = 0.55f;
        [SerializeField, Min(0.02f)] private float attackCooldown = 0.9f;
        [SerializeField, Min(0.02f)] private float hurtDuration = 0.3f;
        [SerializeField, Min(0f)] private float attackImpulse = 1.5f;

        [Header("Debug")]
        [SerializeField] private bool drawDebug = true;
        [SerializeField] private CreatureBehaviorState currentState;
        [SerializeField] private int lastExpandedNodeCount;
        [SerializeField] private Vector3Int observedSupport;
        [SerializeField] private int pathIndex;

        private readonly List<Vector3Int> path = new List<Vector3Int>();
        private System.Random random;
        private MinecraftCaveVoxelQuery query;
        private CreatureVoxelShape shape;
        private Vector3Int currentSupport;
        private Vector3Int currentTarget;
        private Vector3Int lastPursuitTarget;
        private float nextPursuitReplanTime;
        private float nextWanderAttemptTime;
        private float forcedIdleUntil;
        private bool hasPursuitTarget;
        private bool configurationErrorLogged;
        private int movementCommandId;
        private CharacterStateMachine<CreatureBehaviorState> stateMachine;
        private float stateSeconds;
        private float nextAttackTime;
        private bool attackApplied;
        private bool deathCleanupScheduled;

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
        public MinecraftCaveInfiniteWorld CaveWorld => caveWorld;
        public Transform PlayerFoot => playerFoot;

        public void BindWorldContext(
            MinecraftCaveInfiniteWorld world,
            Transform targetPlayerFoot)
        {
            caveWorld = world;
            playerFoot = targetPlayerFoot;
            query = null;
            configurationErrorLogged = false;
            ResolveReferences();
            if (caveWorld != null)
            {
                SynchronizeLogicalPosition();
            }
        }

        private void Awake()
        {
            random = new System.Random(unchecked(GetInstanceID() * 486187739));
            ResolveReferences();
            EnsureVitals(true);
            BuildStateMachine();
        }

        private void OnEnable()
        {
            ClearNavigation();
            ResolveReferences();
            EnsureStateMachine();
            stateMachine.Start(vitals.IsAlive
                ? CreatureBehaviorState.Idle
                : CreatureBehaviorState.Dead);
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
            if (pathIndex >= path.Count)
            {
                currentSupport = observedSupport;
            }

            Vector3Int playerSupport = WorldFootPositionToSupport(playerFoot.position);
            float playerDistance = Vector3.Distance(currentSupport, playerSupport);
            CreatureBehaviorState desiredState = SelectState(playerDistance);
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
                return CreatureBehaviorState.Idle;
            }

            if (playerDistance <= attackDistance)
            {
                return CreatureBehaviorState.Attack;
            }

            if (playerDistance <= pursuitDistance)
            {
                return CreatureBehaviorState.Pursue;
            }

            return Time.time < forcedIdleUntil
                ? CreatureBehaviorState.Idle
                : CreatureBehaviorState.Wander;
        }

        public bool ReceiveDamage(in DamageInfo damage)
        {
            EnsureVitals(false);
            if (!vitals.ApplyDamage(damage.Amount)) return false;

            ResolveReferences();
            EnsureStateMachine();
            if (motor != null && damage.Impulse > 0f)
            {
                motor.ApplyImpulse(damage.Direction * damage.Impulse);
            }

            SetState(vitals.IsAlive
                ? CreatureBehaviorState.Hurt
                : CreatureBehaviorState.Dead);
            return true;
        }

        public void RestoreFullHealth()
        {
            EnsureVitals(false);
            vitals.RestoreFullHealth();
            EnsureStateMachine();
            SetState(CreatureBehaviorState.Idle);
        }

        private void TickIdle(float deltaTime)
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
            Vector3 up = caveWorld != null ? caveWorld.transform.up : Vector3.up;
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
            float voxelSize = caveWorld != null ? caveWorld.VoxelSize : 1f;
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
            float playerDistance = Vector3.Distance(currentSupport, playerSupport);
            SetState(SelectState(playerDistance));
        }

        private void EnterDead()
        {
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

            nextWanderAttemptTime = Time.time + wanderRetryInterval;
            for (int attempt = 0; attempt < wanderRejectionAttempts; attempt++)
            {
                Vector2 offset = RandomInsideCircle(wanderRadius);
                int randomY = random.Next(-wanderVerticalSearch, wanderVerticalSearch + 1);
                Vector3Int sample = currentSupport + new Vector3Int(
                    Mathf.RoundToInt(offset.x),
                    randomY,
                    Mathf.RoundToInt(offset.y));

                if (!TryFindStandableVertically(sample, wanderVerticalSearch, out Vector3Int target)
                    || Vector3.Distance(currentSupport, target) > wanderRadius)
                {
                    continue;
                }

                if (BuildPath(target))
                {
                    return;
                }
            }

            forcedIdleUntil = Time.time + wanderRetryInterval;
            SetState(CreatureBehaviorState.Idle);
        }

        private void UpdatePursue(Vector3Int requestedTarget)
        {
            bool targetChanged = !hasPursuitTarget || requestedTarget != lastPursuitTarget;
            if (Time.time < nextPursuitReplanTime)
            {
                return;
            }

            if (!targetChanged && pathIndex < path.Count)
            {
                return;
            }

            nextPursuitReplanTime = Time.time + pursuitReplanInterval;
            lastPursuitTarget = requestedTarget;
            hasPursuitTarget = true;

            if (TryFindNearestStandable(requestedTarget, 2, 4, out Vector3Int target))
            {
                BuildPath(target);
            }
            else
            {
                ClearNavigation();
            }
        }

        private bool BuildPath(Vector3Int target)
        {
            SynchronizeLogicalPosition();
            bool found = CreatureVoxelNavigation.TryFindPath(
                query,
                shape,
                navigation,
                currentSupport,
                target,
                path,
                out lastExpandedNodeCount);
            if (!found)
            {
                ClearNavigation();
                return false;
            }

            currentTarget = target;
            pathIndex = path.Count > 1 ? 1 : path.Count;
            movementCommandId++;
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

        private bool IsLogicallyAt(Vector3Int support)
        {
            if (observedSupport == support)
            {
                return true;
            }

            Vector3 target = SupportToWorldFootPosition(support);
            float tolerance = arrivalToleranceInVoxels * caveWorld.VoxelSize;
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

            Vector3 localDirection = new Vector3(step.x, 0f, step.z);
            Vector3 worldDirection = caveWorld.transform.TransformDirection(localDirection);
            motor.Submit(new CreatureMovementCommand(
                movementCommandId,
                worldDirection,
                caveWorld.transform.up,
                step.y));
        }

        private bool EnsureReady()
        {
            ResolveReferences();
            if (caveWorld == null
                || caveWorld.World == null
                || playerFoot == null
                || motor == null
                || shape == null
                || shape.IsEmpty)
            {
                return false;
            }

            if (!Mathf.Approximately(shape.BakedVoxelSize, caveWorld.VoxelSize))
            {
                if (!configurationErrorLogged)
                {
                    Debug.LogError(
                        $"{name}: baked creature voxel size ({shape.BakedVoxelSize}) must match "
                        + $"world voxel size ({caveWorld.VoxelSize}).",
                        this);
                    configurationErrorLogged = true;
                }

                return false;
            }

            query ??= new MinecraftCaveVoxelQuery(caveWorld, solidDensityThreshold);
            return true;
        }

        private void ResolveReferences()
        {
            if (caveWorld == null)
            {
                caveWorld = FindObjectOfType<MinecraftCaveInfiniteWorld>();
                query = null;
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
                shapeAuthoring = GetComponent<CreatureVoxelShapeAuthoring>();
            }

            CreatureVoxelShape resolvedShape = shapeAuthoring != null
                ? shapeAuthoring.Shape
                : null;
            if (resolvedShape != shape)
            {
                shape = resolvedShape;
                query = null;
                currentSupport = caveWorld != null
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
            Vector3 localVoxel = caveWorld.transform.InverseTransformPoint(worldPosition)
                / caveWorld.VoxelSize;
            Vector3Int foot = new Vector3Int(
                Mathf.RoundToInt(localVoxel.x),
                Mathf.FloorToInt(localVoxel.y + 0.001f),
                Mathf.RoundToInt(localVoxel.z));
            return foot + Vector3Int.down;
        }

        private Vector3 SupportToWorldFootPosition(Vector3Int support)
        {
            Vector3 local = (Vector3)(support + Vector3Int.up) * caveWorld.VoxelSize;
            return caveWorld.transform.TransformPoint(local);
        }

        private Vector2 RandomInsideCircle(float radius)
        {
            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            float distance = Mathf.Sqrt((float)random.NextDouble()) * radius;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
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
        }

        private void EnsureStateMachine()
        {
            if (stateMachine == null) BuildStateMachine();
            if (!stateMachine.IsRunning)
            {
                stateMachine.Start(vitals.IsAlive
                    ? CreatureBehaviorState.Idle
                    : CreatureBehaviorState.Dead);
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

        private void EnterState(CreatureBehaviorState value)
        {
            currentState = value;
            ClearNavigation();
            hasPursuitTarget = false;
            if (value == CreatureBehaviorState.Wander) nextWanderAttemptTime = 0f;
        }

        private void ClearNavigation()
        {
            path.Clear();
            pathIndex = 0;
            movementCommandId++;
            if (motor != null)
            {
                motor.Stop();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebug || caveWorld == null)
            {
                return;
            }

            float scale = caveWorld.VoxelSize;
            Gizmos.color = currentState switch
            {
                CreatureBehaviorState.Idle => Color.gray,
                CreatureBehaviorState.Wander => Color.yellow,
                CreatureBehaviorState.Pursue => new Color(1f, 0.35f, 0.1f),
                CreatureBehaviorState.Attack => Color.red,
                CreatureBehaviorState.Hurt => Color.magenta,
                CreatureBehaviorState.Dead => Color.black,
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
