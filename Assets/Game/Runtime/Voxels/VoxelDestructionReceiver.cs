using Supernova.Effects;
using Supernova.MinecraftCaves;
using UnityEngine;

namespace Supernova.Voxels
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MinecraftCaveInfiniteWorld))]
    public sealed class VoxelDestructionReceiver : AreaEffectReceiverBehaviour
    {
        [SerializeField, Min(0.01f)] private float terrainRadiusMultiplier = 1f;
        private MinecraftCaveInfiniteWorld terrain;

        protected override void OnEnable()
        {
            terrain = GetComponent<MinecraftCaveInfiniteWorld>();
            base.OnEnable();
        }

        public override void ReceiveAreaEffect(in AreaEffectContext context)
        {
            if (terrain == null || terrain.World == null || context.Radius <= 0f) return;
            terrain.CarveSphere(
                context.Origin,
                context.Radius * terrainRadiusMultiplier,
                context.TerrainRandomness,
                context.Seed);
        }
    }
}
