using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Emits the configured bomb-tool explosion exactly once when a valuable
    /// treasure reaches zero value.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ValuableObject))]
    public sealed class TreasureDestructionExplosion : MonoBehaviour
    {
        private ValuableObject valuable;
        private PlayerToolDefinition explosionTool;
        private IVoxelTerrain terrain;
        private bool subscribed;

        public bool HasExploded { get; private set; }
        public GameplayExplosionResult LastExplosionResult { get; private set; }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public void Configure(
            PlayerToolDefinition tool,
            IVoxelTerrain voxelTerrain)
        {
            explosionTool = tool;
            terrain = voxelTerrain;
            HasExploded = false;
            LastExplosionResult = default;
            Subscribe();
        }

        public void PrepareForReuse()
        {
            HasExploded = false;
            LastExplosionResult = default;
            Subscribe();
        }

        public void PrepareForPool()
        {
            Unsubscribe();
            explosionTool = null;
            terrain = null;
            HasExploded = false;
            LastExplosionResult = default;
        }

        public bool Detonate()
        {
            if (HasExploded
                || explosionTool == null
                || explosionTool.BombProjectilePrefab == null)
            {
                return false;
            }

            HasExploded = true;
            BombProjectile bomb = explosionTool.BombProjectilePrefab;
            GameObject effect = explosionTool.BombExplosionEffectPrefab != null
                ? explosionTool.BombExplosionEffectPrefab
                : bomb.ExplosionEffectPrefab;
            LastExplosionResult = GameplayExplosion.Detonate(
                gameObject,
                transform.position,
                terrain,
                bomb.ExplosionSettings,
                explosionTool.BombEntityExplosionImpulse,
                bomb.EntityUpwardModifier,
                effect,
                explosionTool.BombExplosionEffectLifetime,
                GetComponent<Rigidbody>());
            return true;
        }

        private void HandleBroken()
        {
            Detonate();
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled)
            {
                return;
            }
            if (valuable == null)
            {
                valuable = GetComponent<ValuableObject>();
            }
            if (valuable == null)
            {
                return;
            }

            valuable.Broken += HandleBroken;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }
            if (valuable != null)
            {
                valuable.Broken -= HandleBroken;
            }
            subscribed = false;
        }
    }
}
