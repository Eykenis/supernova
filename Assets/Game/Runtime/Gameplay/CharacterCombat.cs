using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Gameplay
{
    public interface ICharacterState<TState> where TState : struct, Enum
    {
        TState Id { get; }
        void Enter();
        void Tick(float deltaTime);
        void Exit();
    }

    /// <summary>
    /// Small state machine with no dependency on input, animation, or either Unity motor type.
    /// States receive those concerns through the context captured by their implementation.
    /// </summary>
    public sealed class CharacterStateMachine<TState> where TState : struct, Enum
    {
        private readonly Dictionary<TState, ICharacterState<TState>> states =
            new Dictionary<TState, ICharacterState<TState>>();
        private ICharacterState<TState> current;

        public bool IsRunning => current != null;
        public TState Current => current != null ? current.Id : default;

        public void Add(ICharacterState<TState> state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            states.Add(state.Id, state);
        }

        public void Start(TState state)
        {
            if (current != null) current.Exit();
            current = Get(state);
            current.Enter();
        }

        public bool Change(TState state)
        {
            if (current != null && EqualityComparer<TState>.Default.Equals(current.Id, state))
            {
                return false;
            }

            ICharacterState<TState> next = Get(state);
            if (current != null) current.Exit();
            current = next;
            current.Enter();
            return true;
        }

        public void Tick(float deltaTime)
        {
            current?.Tick(Mathf.Max(0f, deltaTime));
        }

        public void Stop()
        {
            if (current != null) current.Exit();
            current = null;
        }

        private ICharacterState<TState> Get(TState state)
        {
            if (!states.TryGetValue(state, out ICharacterState<TState> result))
            {
                throw new InvalidOperationException($"State '{state}' is not registered.");
            }

            return result;
        }
    }

    public readonly struct DamageInfo
    {
        public DamageInfo(
            float amount,
            GameObject source,
            Vector3 point,
            Vector3 direction,
            float impulse = 0f)
        {
            Amount = Mathf.Max(0f, amount);
            Source = source;
            Point = point;
            Direction = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.zero;
            Impulse = Mathf.Max(0f, impulse);
        }

        public float Amount { get; }
        public GameObject Source { get; }
        public Vector3 Point { get; }
        public Vector3 Direction { get; }
        public float Impulse { get; }
    }

    public interface IDamageable
    {
        GameObject Owner { get; }
        float CurrentHealth { get; }
        float MaximumHealth { get; }
        bool IsAlive { get; }
        bool ReceiveDamage(in DamageInfo damage);
    }

    /// <summary>
    /// Marks damage receivers that firearm projectiles are allowed to damage.
    /// Keeping this separate from IDamageable prevents bullets from damaging the
    /// player or unrelated damageable world objects.
    /// </summary>
    public interface IMonsterDamageable : IDamageable
    {
    }

    [Serializable]
    public sealed class CharacterVitals
    {
        [SerializeField, Min(0.01f)] private float maximumHealth = 100f;
        [SerializeField, Min(0f)] private float currentHealth = 100f;

        public float MaximumHealth => maximumHealth;
        public float CurrentHealth => currentHealth;
        public bool IsAlive => currentHealth > 0f;

        public void Initialize(float configuredMaximumHealth, bool refill)
        {
            maximumHealth = Mathf.Max(0.01f, configuredMaximumHealth);
            currentHealth = refill
                ? maximumHealth
                : Mathf.Clamp(currentHealth, 0f, maximumHealth);
        }

        public bool ApplyDamage(float amount)
        {
            if (!IsAlive || amount <= 0f) return false;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
            return true;
        }

        public void RestoreFullHealth()
        {
            currentHealth = maximumHealth;
        }
    }

    public static class MeleeCombat
    {
        private static readonly Collider[] Hits = new Collider[32];
        private static readonly HashSet<int> DamagedOwners = new HashSet<int>();

        public static int DamageSphere(
            GameObject source,
            Vector3 centre,
            float radius,
            Vector3 forward,
            float minimumForwardDot,
            float damage,
            float impulse,
            int layerMask = ~0)
        {
            if (source == null || radius <= 0f || damage <= 0f) return 0;

            int count = Physics.OverlapSphereNonAlloc(
                centre,
                radius,
                Hits,
                layerMask,
                QueryTriggerInteraction.Collide);
            int damagedCount = 0;
            DamagedOwners.Clear();
            Vector3 normalizedForward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                Collider hit = Hits[i];
                if (hit == null || !TryFindDamageable(hit, out IDamageable target)) continue;
                if (!target.IsAlive || target.Owner == null || target.Owner == source) continue;
                if (target.Owner.transform.root == source.transform.root) continue;

                Vector3 point = hit.ClosestPoint(centre);
                Vector3 direction = point - source.transform.position;
                if (normalizedForward.sqrMagnitude > 0f
                    && direction.sqrMagnitude > 0.0001f
                    && Vector3.Dot(normalizedForward, direction.normalized) < minimumForwardDot)
                {
                    continue;
                }

                if (!DamagedOwners.Add(target.Owner.GetInstanceID())) continue;

                var info = new DamageInfo(damage, source, point, direction, impulse);
                if (target.ReceiveDamage(info)) damagedCount++;
            }

            return damagedCount;
        }

        public static bool TryFindDamageable(Component component, out IDamageable damageable)
        {
            damageable = null;
            if (component == null) return false;

            Transform cursor = component.transform;
            while (cursor != null)
            {
                MonoBehaviour[] behaviours = cursor.GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IDamageable candidate)
                    {
                        damageable = candidate;
                        return true;
                    }
                }

                cursor = cursor.parent;
            }

            return false;
        }
    }
}
