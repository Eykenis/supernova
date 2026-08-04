using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Draws dense surface content without creating a GameObject per placement.
    /// The component lives under a mesh section, so rebuilding or unloading that
    /// section removes all of its instance batches immediately.
    /// <para>
    /// Placements are grouped by (brush, biome) so one shared material can serve
    /// every biome, with the biome's vegetation tint supplied through a cached
    /// <see cref="MaterialPropertyBlock"/>. Within a group they are sorted into
    /// spatially coherent batches so per-batch frustum culling and per-batch LOD
    /// selection are meaningful; a batch is the unit of both.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveSurfaceInstanceRenderer : MonoBehaviour
    {
        public const int MaximumInstancesPerDrawCall = 1023;

        private static readonly ProfilerMarker CullMarker =
            new ProfilerMarker("CaveSurface.Instances.Cull");
        private static readonly ProfilerMarker DrawMarker =
            new ProfilerMarker("CaveSurface.Instances.Draw");

        private static readonly int RootColorId = Shader.PropertyToID("_RootColor");
        private static readonly int TipColorId = Shader.PropertyToID("_TipColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int TintVariationId =
            Shader.PropertyToID("_TintVariation");
        private static readonly int WindStrengthId =
            Shader.PropertyToID("_WindStrength");
        private static readonly int WindFrequencyId =
            Shader.PropertyToID("_WindFrequency");
        private static readonly int WindScrollSpeedId =
            Shader.PropertyToID("_WindScrollSpeed");
        private static readonly int WindBendExponentId =
            Shader.PropertyToID("_WindBendExponent");
        private static readonly int WindDirectionId =
            Shader.PropertyToID("_WindDirection");
        private static readonly int FadeStartDistanceId =
            Shader.PropertyToID("_FadeStartDistance");
        private static readonly int FadeEndDistanceId =
            Shader.PropertyToID("_FadeEndDistance");
        private static readonly int ClumpCellSizeId =
            Shader.PropertyToID("_ClumpCellSize");

        /// <summary>
        /// Frustum planes are shared across every section for a given camera and
        /// frame, so they are computed once instead of per component.
        /// </summary>
        private static readonly Plane[] FrustumPlanes = new Plane[6];
        private static Camera frustumCamera;
        private static int frustumFrame = -1;

        private readonly List<InstanceGroup> groups = new List<InstanceGroup>();
        private Camera distanceCamera;
        private Matrix4x4 lastLocalToWorld;
        private bool hasWorldMatrices;

        public int InstanceCount { get; private set; }
        public int DrawCallCount { get; private set; }

        /// <summary>Number of (brush, biome) groups held by this section.</summary>
        public int GroupCount => groups.Count;

        /// <summary>
        /// Retained name for the group count. A brush that appears under several
        /// biomes within one section now yields one group per biome.
        /// </summary>
        public int BrushCount => groups.Count;

        /// <summary>
        /// Builds the instance batches for this section.
        /// <para>
        /// Placements are reordered for spatial coherence, so a render index does
        /// not correspond to the caller's input index. Instance identity is the
        /// anchor voxel set, not the ordering; see <see cref="GetAnchorVoxel"/>.
        /// </para>
        /// </summary>
        public void Configure(IReadOnlyList<CaveSurfacePlacement> placements)
        {
            groups.Clear();
            InstanceCount = 0;
            DrawCallCount = 0;
            hasWorldMatrices = false;
            if (placements == null || placements.Count == 0)
            {
                enabled = false;
                return;
            }

            var placementsByGroup = new Dictionary<
                GroupKey,
                List<CaveSurfacePlacement>>();
            var groupOrder = new List<GroupKey>();
            for (int i = 0; i < placements.Count; i++)
            {
                CaveSurfacePlacement placement = placements[i];
                CaveSurfaceBrushDefinition brush = placement.Brush;
                if (brush == null
                    || brush.RenderMode !=
                        CaveSurfaceBrushRenderMode.InstancedMesh
                    || !brush.HasRenderableContent)
                {
                    continue;
                }

                var key = new GroupKey(brush, placement.Biome);
                if (!placementsByGroup.TryGetValue(
                    key,
                    out List<CaveSurfacePlacement> groupPlacements))
                {
                    groupPlacements = new List<CaveSurfacePlacement>();
                    placementsByGroup.Add(key, groupPlacements);
                    groupOrder.Add(key);
                }
                groupPlacements.Add(placement);
            }

            for (int i = 0; i < groupOrder.Count; i++)
            {
                GroupKey key = groupOrder[i];
                var group = new InstanceGroup(
                    key.Brush,
                    key.Biome,
                    placementsByGroup[key]);
                groups.Add(group);
                InstanceCount += group.InstanceCount;
                DrawCallCount += group.DrawCallCount;
            }
            enabled = groups.Count > 0;
        }

        /// <summary>
        /// Returns the anchor voxel of a rendered instance. The render order is not
        /// the order placements were supplied in, because
        /// <see cref="Configure"/> sorts them for spatial coherence.
        /// </summary>
        public Vector3Int GetAnchorVoxel(int groupIndex, int instanceIndex)
        {
            if (groupIndex < 0 || groupIndex >= groups.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(groupIndex));
            }
            return groups[groupIndex].GetAnchorVoxel(instanceIndex);
        }

        /// <summary>Instances held by one group, for enumerating anchors.</summary>
        public int GetGroupInstanceCount(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= groups.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(groupIndex));
            }
            return groups[groupIndex].InstanceCount;
        }

        private void LateUpdate()
        {
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            if (!hasWorldMatrices || localToWorld != lastLocalToWorld)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    groups[i].UpdateWorldMatrices(localToWorld);
                }
                lastLocalToWorld = localToWorld;
                hasWorldMatrices = true;
            }

            if (distanceCamera == null)
            {
                distanceCamera = Camera.main;
            }
            if (distanceCamera == null)
            {
                return;
            }

            // Instanced draws are submitted once and replayed for every camera, so
            // culling and LOD follow the main camera only. A secondary camera (for
            // example a portal view) would see grass selected for the player's
            // viewpoint. Acceptable while no such camera renders the cave.
            using (CullMarker.Auto())
            {
                EnsureFrustumPlanes(distanceCamera);
            }

            Vector3 cameraPosition = distanceCamera.transform.position;
            using (DrawMarker.Auto())
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    groups[i].Draw(
                        gameObject.layer,
                        cameraPosition,
                        FrustumPlanes);
                }
            }
        }

        private static void EnsureFrustumPlanes(Camera camera)
        {
            int frame = Time.frameCount;
            if (frustumFrame == frame && ReferenceEquals(frustumCamera, camera))
            {
                return;
            }
            GeometryUtility.CalculateFrustumPlanes(camera, FrustumPlanes);
            frustumCamera = camera;
            frustumFrame = frame;
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(
                CaveSurfaceBrushDefinition brush,
                CaveBiomeDefinition biome)
            {
                Brush = brush;
                Biome = biome;
            }

            public CaveSurfaceBrushDefinition Brush { get; }
            public CaveBiomeDefinition Biome { get; }

            public bool Equals(GroupKey other)
            {
                return ReferenceEquals(Brush, other.Brush)
                    && ReferenceEquals(Biome, other.Biome);
            }

            public override bool Equals(object obj)
            {
                return obj is GroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                int brushHash = Brush != null ? Brush.GetInstanceID() : 0;
                int biomeHash = Biome != null ? Biome.GetInstanceID() : 0;
                return brushHash * 397 ^ biomeHash;
            }
        }

        private sealed class InstanceGroup
        {
            private readonly CaveSurfaceBrushDefinition brush;
            private readonly Matrix4x4[][] localMatrices;
            private readonly Matrix4x4[][] worldMatrices;
            private readonly int[] batchCounts;
            private readonly Bounds[] localBatchBounds;
            private readonly Bounds[] worldBatchBounds;
            private readonly Vector3Int[] anchors;
            private readonly MaterialPropertyBlock propertyBlock;

            public InstanceGroup(
                CaveSurfaceBrushDefinition brush,
                CaveBiomeDefinition biome,
                List<CaveSurfacePlacement> placements)
            {
                this.brush = brush;
                InstanceCount = placements.Count;

                // Sort into spatially coherent runs first. Without this a batch
                // spans the whole section and its bounds cover everything, which
                // makes per-batch culling and LOD no better than all-or-nothing.
                SortSpatially(placements);

                int batchCount = Mathf.CeilToInt(
                    InstanceCount / (float)MaximumInstancesPerDrawCall);
                localMatrices = new Matrix4x4[batchCount][];
                worldMatrices = new Matrix4x4[batchCount][];
                batchCounts = new int[batchCount];
                localBatchBounds = new Bounds[batchCount];
                worldBatchBounds = new Bounds[batchCount];
                anchors = new Vector3Int[InstanceCount];

                Mesh boundsMesh = brush.ResolveLodMesh(0f);
                Bounds meshBounds = boundsMesh != null
                    ? boundsMesh.bounds
                    : new Bounds(Vector3.zero, Vector3.one);

                for (int batch = 0; batch < batchCount; batch++)
                {
                    int count = Mathf.Min(
                        MaximumInstancesPerDrawCall,
                        InstanceCount - batch * MaximumInstancesPerDrawCall);
                    batchCounts[batch] = count;
                    localMatrices[batch] = new Matrix4x4[count];
                    worldMatrices[batch] = new Matrix4x4[count];

                    Bounds bounds = default;
                    bool hasBounds = false;
                    for (int index = 0; index < count; index++)
                    {
                        int placementIndex =
                            batch * MaximumInstancesPerDrawCall + index;
                        CaveSurfacePlacement placement =
                            placements[placementIndex];
                        Matrix4x4 matrix = Matrix4x4.TRS(
                            placement.LocalPosition,
                            placement.LocalRotation,
                            placement.Scale);
                        localMatrices[batch][index] = matrix;
                        anchors[placementIndex] = placement.AnchorVoxel;

                        Bounds instanceBounds = TransformBounds(
                            meshBounds,
                            matrix);
                        if (!hasBounds)
                        {
                            bounds = instanceBounds;
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(instanceBounds);
                        }
                    }
                    localBatchBounds[batch] = bounds;
                }

                propertyBlock = CreatePropertyBlock(brush, biome);
                DrawCallCount = batchCount;
            }

            public int InstanceCount { get; }
            public int DrawCallCount { get; }

            public Vector3Int GetAnchorVoxel(int instanceIndex)
            {
                if (instanceIndex < 0 || instanceIndex >= anchors.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(instanceIndex));
                }
                return anchors[instanceIndex];
            }

            public void UpdateWorldMatrices(Matrix4x4 localToWorld)
            {
                for (int batch = 0; batch < localMatrices.Length; batch++)
                {
                    for (int index = 0; index < batchCounts[batch]; index++)
                    {
                        worldMatrices[batch][index] = localToWorld
                            * localMatrices[batch][index];
                    }
                    worldBatchBounds[batch] = TransformBounds(
                        localBatchBounds[batch],
                        localToWorld);
                }
            }

            public void Draw(
                int layer,
                Vector3 cameraPosition,
                Plane[] frustumPlanes)
            {
                Material material = brush.InstanceMaterial;
                if (material == null || !material.enableInstancing)
                {
                    return;
                }

                float maximumDistance = brush.MaximumDrawDistance;
                float squaredMaximum = maximumDistance * maximumDistance;
                for (int batch = 0; batch < worldMatrices.Length; batch++)
                {
                    Bounds bounds = worldBatchBounds[batch];
                    if (maximumDistance > 0f
                        && bounds.SqrDistance(cameraPosition) > squaredMaximum)
                    {
                        continue;
                    }
                    if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
                    {
                        continue;
                    }

                    // One tier per batch, chosen from the batch centre. Because
                    // batches are spatially tight the switch happens at a batch
                    // boundary rather than through the middle of a patch.
                    float distance = Mathf.Sqrt(
                        bounds.SqrDistance(cameraPosition));
                    Mesh mesh = brush.ResolveLodMesh(distance);
                    if (mesh == null)
                    {
                        continue;
                    }

                    int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
                    for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                    {
                        Graphics.DrawMeshInstanced(
                            mesh,
                            subMesh,
                            material,
                            worldMatrices[batch],
                            batchCounts[batch],
                            propertyBlock,
                            brush.InstanceShadowCastingMode,
                            brush.InstanceReceiveShadows,
                            layer,
                            null,
                            LightProbeUsage.Off,
                            null);
                    }
                }
            }

            /// <summary>
            /// Orders placements along a coarse Morton curve so nearby instances
            /// land in the same batch.
            /// </summary>
            private static void SortSpatially(
                List<CaveSurfacePlacement> placements)
            {
                if (placements.Count <= MaximumInstancesPerDrawCall)
                {
                    return;
                }
                placements.Sort(CompareByMortonCode);
            }

            private static int CompareByMortonCode(
                CaveSurfacePlacement first,
                CaveSurfacePlacement second)
            {
                return MortonCode(first.LocalPosition)
                    .CompareTo(MortonCode(second.LocalPosition));
            }

            /// <summary>
            /// Interleaves the low bits of a quantised position. Section-local
            /// coordinates span 0..32, so a half-unit grid fits comfortably.
            /// </summary>
            private static uint MortonCode(Vector3 localPosition)
            {
                uint x = (uint)Mathf.Clamp(
                    Mathf.FloorToInt(localPosition.x * 2f), 0, 1023);
                uint y = (uint)Mathf.Clamp(
                    Mathf.FloorToInt(localPosition.y * 2f), 0, 1023);
                uint z = (uint)Mathf.Clamp(
                    Mathf.FloorToInt(localPosition.z * 2f), 0, 1023);
                return Interleave(x) | Interleave(y) << 1 | Interleave(z) << 2;
            }

            private static uint Interleave(uint value)
            {
                value &= 0x000003FFu;
                value = (value | value << 16) & 0x030000FFu;
                value = (value | value << 8) & 0x0300F00Fu;
                value = (value | value << 4) & 0x030C30C3u;
                value = (value | value << 2) & 0x09249249u;
                return value;
            }

            /// <summary>
            /// Builds the per-group property block carrying the biome's vegetation
            /// tint and the brush's wind response, so a single shared material
            /// serves every biome rather than needing one variant each.
            /// </summary>
            private static MaterialPropertyBlock CreatePropertyBlock(
                CaveSurfaceBrushDefinition brush,
                CaveBiomeDefinition biome)
            {
                var block = new MaterialPropertyBlock();
                float windResponse = 1f;
                if (biome != null)
                {
                    block.SetColor(RootColorId, biome.VegetationRootColor);
                    block.SetColor(TipColorId, biome.VegetationTipColor);
                    block.SetColor(RimColorId, biome.VegetationRimColor);
                    block.SetFloat(TintVariationId, biome.VegetationTintVariation);
                    windResponse = biome.VegetationWindResponse;
                }

                block.SetFloat(WindStrengthId, brush.WindStrength * windResponse);
                block.SetFloat(WindFrequencyId, brush.WindFrequency);
                block.SetFloat(WindScrollSpeedId, brush.WindScrollSpeed);
                block.SetFloat(WindBendExponentId, brush.WindBendExponent);
                Vector2 windDirection = brush.WindDirection;
                block.SetVector(
                    WindDirectionId,
                    new Vector4(windDirection.x, windDirection.y, 0f, 0f));

                // Same cell sizes the CPU clump field uses, so the shader's tint
                // patches line up with the height and facing patches instead of
                // cutting across them. Interpreted in world units, which matches
                // voxel units at the default voxel size of one.
                block.SetVector(
                    ClumpCellSizeId,
                    new Vector4(
                        brush.ClumpHorizontalCellSize,
                        brush.ClumpVerticalCellSize,
                        0f,
                        0f));

                float drawDistance = brush.MaximumDrawDistance;
                float fadeBand = brush.FadeBandDistance;
                if (drawDistance <= 0f)
                {
                    // No distance culling, so place the fade far enough away that
                    // it never engages.
                    block.SetFloat(FadeStartDistanceId, float.MaxValue * 0.5f);
                    block.SetFloat(FadeEndDistanceId, float.MaxValue);
                }
                else
                {
                    block.SetFloat(
                        FadeStartDistanceId,
                        Mathf.Max(0f, drawDistance - fadeBand));
                    block.SetFloat(FadeEndDistanceId, drawDistance);
                }
                return block;
            }

            private static Bounds TransformBounds(
                Bounds bounds,
                Matrix4x4 matrix)
            {
                Vector3 centre = matrix.MultiplyPoint3x4(bounds.center);
                Vector3 extents = bounds.extents;
                Vector3 axisX = matrix.MultiplyVector(
                    new Vector3(extents.x, 0f, 0f));
                Vector3 axisY = matrix.MultiplyVector(
                    new Vector3(0f, extents.y, 0f));
                Vector3 axisZ = matrix.MultiplyVector(
                    new Vector3(0f, 0f, extents.z));
                extents = new Vector3(
                    Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x)
                        + Mathf.Abs(axisZ.x),
                    Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y)
                        + Mathf.Abs(axisZ.y),
                    Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z)
                        + Mathf.Abs(axisZ.z));
                return new Bounds(centre, extents * 2f);
            }
        }
    }
}
