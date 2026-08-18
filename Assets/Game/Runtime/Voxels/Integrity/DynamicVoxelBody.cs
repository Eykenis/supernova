using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Supernova.MinecraftCaves;
using UnityEngine;

namespace Supernova.Voxels.Integrity
{
    public readonly struct DynamicVoxelRaycastHit
    {
        public DynamicVoxelRaycastHit(
            DynamicVoxelAddress address,
            VoxelSample sample,
            Vector3 point,
            Vector3 normal,
            float distance)
        {
            Address = address;
            Sample = sample;
            Point = point;
            Normal = normal;
            Distance = distance;
        }

        public DynamicVoxelAddress Address { get; }
        public VoxelSample Sample { get; }
        public Vector3 Point { get; }
        public Vector3 Normal { get; }
        public float Distance { get; }
    }

    /// <summary>
    /// A small scene-local voxel world moving as one Rigidbody. Coordinates stay
    /// in the source world's grid, while Pivot maps that grid into this body's
    /// local transform. Topology and all generated data are rebuilt off-thread.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DynamicVoxelBody : MonoBehaviour
    {
        private static readonly Vector3Int[] CellCornerOffsets =
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(1, 1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 1),
            new Vector3Int(0, 1, 1),
            new Vector3Int(1, 1, 1),
        };

        private DynamicVoxelBodyRegistry registry;
        private Guid lineageId;
        private Dictionary<Vector3Int, VoxelSample> samples;
        private float isoLevel;
        private float voxelSize;
        private MarchingCubesVertexPlacement vertexPlacement;
        private VoxelGroupMap groupMap;
        private Dictionary<VoxelTypeId, float> massByType;
        private float defaultMassPerFullVoxel;
        private VoxelConvexDecompositionSettings convexSettings;
        private MinecraftCaveInfiniteWorld oreDropTerrain;
        private VoxelTypeCatalog voxelTypeCatalog;
        private Material fallbackMaterial;
        private Vector3 pivot;
        private VoxelMeshRaycastBvh raycastBvh;
        private readonly VoxelMiningProgress miningProgress =
            new VoxelMiningProgress();
        private Task<DynamicVoxelBodyBuildResult> rebuildTask;
        private CancellationTokenSource rebuildCancellation;
        private int revision;
        private int failedRebuildCount;
        private bool initialized;

        public Guid LineageId => lineageId;
        public int VoxelCount => samples != null ? samples.Count : 0;
        public Vector3 Pivot => pivot;
        public VoxelTypeCatalog VoxelTypeCatalog => voxelTypeCatalog;
        public DynamicVoxelBodyRegistry Registry => registry;
        public IEnumerable<Vector3Int> Coordinates =>
            samples != null
                ? samples.Keys
                : Array.Empty<Vector3Int>();

        internal void Initialize(
            DynamicVoxelBodyRegistry ownerRegistry,
            Guid bodyLineageId,
            DynamicVoxelComponentBuildData buildData,
            float bodyIsoLevel,
            float bodyVoxelSize,
            MarchingCubesVertexPlacement bodyVertexPlacement,
            VoxelGroupMap bodyGroupMap,
            Dictionary<VoxelTypeId, float> bodyMassByType,
            float bodyDefaultMassPerFullVoxel,
            VoxelConvexDecompositionSettings bodyConvexSettings,
            MinecraftCaveInfiniteWorld recoveredOreOwner,
            VoxelTypeCatalog catalog,
            Material materialFallback)
        {
            registry = ownerRegistry;
            lineageId = bodyLineageId;
            samples = buildData.Samples;
            isoLevel = bodyIsoLevel;
            voxelSize = bodyVoxelSize;
            vertexPlacement = bodyVertexPlacement;
            groupMap = bodyGroupMap;
            massByType = bodyMassByType;
            defaultMassPerFullVoxel = bodyDefaultMassPerFullVoxel;
            convexSettings = bodyConvexSettings;
            oreDropTerrain = recoveredOreOwner;
            voxelTypeCatalog = catalog;
            fallbackMaterial = materialFallback;
            pivot = buildData.Pivot;
            raycastBvh = buildData.RaycastBvh;
            initialized = true;
            name = $"DynamicVoxelBody_{samples.Count}Voxels";
            registry?.RegisterBody(this);
        }

        public bool TryGetSample(
            Vector3Int coordinate,
            out VoxelSample sample)
        {
            sample = default;
            return samples != null
                && samples.TryGetValue(coordinate, out sample)
                && sample.IsSolid(isoLevel);
        }

        public bool TryRaycastExact(
            Ray worldRay,
            float maxWorldDistance,
            out DynamicVoxelRaycastHit hit)
        {
            hit = default;
            if (!initialized
                || raycastBvh == null
                || maxWorldDistance <= 0f)
            {
                return false;
            }

            Vector3 worldDirection = worldRay.direction.normalized;
            Vector3 localOrigin = transform.InverseTransformPoint(
                worldRay.origin);
            Vector3 localEnd = transform.InverseTransformPoint(
                worldRay.origin + worldDirection * maxWorldDistance);
            Vector3 localVector = localEnd - localOrigin;
            float localLength = localVector.magnitude;
            if (localLength <= 0.000001f)
            {
                return false;
            }

            Vector3 localDirection = localVector / localLength;
            var localRay = new Ray(localOrigin, localDirection);
            if (!raycastBvh.TryRaycast(
                localRay,
                localLength,
                out float localDistance,
                out Vector3 localNormal))
            {
                return false;
            }

            Vector3 localPoint = localRay.GetPoint(localDistance);
            if (!TryResolveSurfaceSample(
                localPoint + localDirection * voxelSize * 0.05f,
                localPoint,
                localOrigin,
                localDirection,
                out Vector3Int coordinate,
                out VoxelSample sample))
            {
                return false;
            }

            Vector3 worldPoint = transform.TransformPoint(localPoint);
            Vector3 worldNormal = transform.worldToLocalMatrix.transpose
                .MultiplyVector(localNormal).normalized;
            float worldDistance =
                (worldPoint - worldRay.origin).magnitude;
            if (worldDistance > maxWorldDistance + 0.0001f)
            {
                return false;
            }

            hit = new DynamicVoxelRaycastHit(
                new DynamicVoxelAddress(lineageId, coordinate),
                sample,
                worldPoint,
                worldNormal,
                worldDistance);
            return true;
        }

        public bool TryMineVoxel(
            Vector3Int coordinate,
            out VoxelMiningResult result)
        {
            result = default;
            if (!TryMineBrush(
                coordinate,
                Vector3.zero,
                VoxelMiningBrushSettings.SingleVoxel,
                out VoxelMiningBrushResult brushResult))
            {
                return false;
            }
            result = brushResult.PrimaryResult;
            return true;
        }

        public bool TryMineBrush(
            Vector3Int primaryCoordinate,
            Vector3 worldDirection,
            VoxelMiningBrushSettings settings,
            out VoxelMiningBrushResult result)
        {
            result = default;
            _ = worldDirection;
            if (!TryGetSample(primaryCoordinate, out VoxelSample primarySample))
            {
                return false;
            }

            var pending = new Queue<MiningNode>();
            var visited = new HashSet<Vector3Int>();
            pending.Enqueue(new MiningNode(primaryCoordinate, settings.Power));
            visited.Add(primaryCoordinate);
            int candidateCount = 0;
            int damagedCount = 0;
            int destroyedCount = 0;
            VoxelMiningResult primaryResult = default;
            bool hasPrimaryResult = false;

            while (pending.Count > 0
                && candidateCount < settings.MaxAffectedSamples)
            {
                MiningNode node = pending.Dequeue();
                if (!TryGetSample(node.Coordinate, out VoxelSample sample)
                    || sample.Type != primarySample.Type)
                {
                    continue;
                }

                candidateCount++;
                int durability = VoxelTypeUtility.ResolveDurability(
                    sample.Type,
                    voxelTypeCatalog != null
                        ? voxelTypeCatalog.Definitions
                        : null);
                if (!miningProgress.TryApplyDamage(
                    node.Coordinate,
                    sample,
                    durability,
                    node.Damage,
                    false,
                    out VoxelMiningResult damageResult))
                {
                    continue;
                }

                damagedCount++;
                if (node.Coordinate == primaryCoordinate)
                {
                    primaryResult = damageResult;
                    hasPrimaryResult = true;
                }
                if (!damageResult.Destroyed)
                {
                    continue;
                }

                if (TryHarvestOreVein(
                    node.Coordinate,
                    sample.Type,
                    out int harvestedCount))
                {
                    destroyedCount += harvestedCount;
                }
                else
                {
                    RemoveSample(node.Coordinate);
                    destroyedCount++;
                }

                float propagatedDamage =
                    damageResult.ExcessDamage / settings.PropagationDivisor;
                if (propagatedDamage <= 0f)
                {
                    continue;
                }

                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            if (x == 0 && y == 0 && z == 0)
                            {
                                continue;
                            }
                            Vector3Int neighbour = node.Coordinate
                                + new Vector3Int(x, y, z);
                            if (visited.Add(neighbour))
                            {
                                pending.Enqueue(new MiningNode(
                                    neighbour,
                                    propagatedDamage));
                            }
                        }
                    }
                }
            }

            result = new VoxelMiningBrushResult(
                primaryCoordinate,
                primarySample.Type,
                candidateCount,
                damagedCount,
                destroyedCount,
                hasPrimaryResult ? primaryResult : default);
            HandleDestroyedSamples(destroyedCount);
            return damagedCount > 0;
        }

        public bool TryMineExplosion(
            Vector3 worldCenter,
            VoxelExplosionSettings settings,
            out VoxelExplosionResult result)
        {
            result = default;
            if (!initialized || samples == null || samples.Count == 0)
            {
                return false;
            }

            float radiusSquared = settings.Radius * settings.Radius;
            var pending = new List<MiningNode>();
            var strongestScheduledDamage =
                new Dictionary<Vector3Int, float>();
            var processed = new HashSet<Vector3Int>();
            int candidateCount = 0;
            foreach (KeyValuePair<Vector3Int, VoxelSample> pair in samples)
            {
                if (!pair.Value.IsSolid(isoLevel))
                {
                    continue;
                }
                Vector3 worldPosition = transform.TransformPoint(
                    (Vector3)pair.Key * voxelSize - pivot);
                float distanceSquared =
                    (worldPosition - worldCenter).sqrMagnitude;
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }
                float damage = settings.GetPower(Mathf.Sqrt(distanceSquared));
                if (damage <= 0f)
                {
                    continue;
                }

                candidateCount++;
                pending.Add(new MiningNode(pair.Key, damage));
                strongestScheduledDamage[pair.Key] = damage;
            }

            int damagedCount = 0;
            int destroyedCount = 0;
            while (pending.Count > 0)
            {
                MiningNode node = RemoveStrongestNode(pending);
                if (processed.Contains(node.Coordinate)
                    || !strongestScheduledDamage.TryGetValue(
                        node.Coordinate,
                        out float strongestDamage)
                    || node.Damage + 0.0001f < strongestDamage
                    || !TryGetSample(
                        node.Coordinate,
                        out VoxelSample sample))
                {
                    continue;
                }

                processed.Add(node.Coordinate);
                int durability = VoxelTypeUtility.ResolveDurability(
                    sample.Type,
                    voxelTypeCatalog != null
                        ? voxelTypeCatalog.Definitions
                        : null);
                if (!miningProgress.TryApplyDamage(
                    node.Coordinate,
                    sample,
                    durability,
                    node.Damage,
                    false,
                    out VoxelMiningResult damageResult))
                {
                    continue;
                }

                damagedCount++;
                if (!damageResult.Destroyed)
                {
                    continue;
                }

                if (TryHarvestOreVein(
                    node.Coordinate,
                    sample.Type,
                    out int harvestedCount))
                {
                    destroyedCount += harvestedCount;
                }
                else
                {
                    RemoveSample(node.Coordinate);
                    destroyedCount++;
                }

                float propagatedDamage = damageResult.ExcessDamage
                    / settings.PropagationDivisor;
                if (propagatedDamage <= 0f)
                {
                    continue;
                }

                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            if (x == 0 && y == 0 && z == 0)
                            {
                                continue;
                            }
                            Vector3Int neighbour = node.Coordinate
                                + new Vector3Int(x, y, z);
                            Vector3 neighbourWorldPosition =
                                transform.TransformPoint(
                                    (Vector3)neighbour * voxelSize - pivot);
                            if ((neighbourWorldPosition - worldCenter)
                                    .sqrMagnitude > radiusSquared
                                || processed.Contains(neighbour)
                                || !TryGetSample(
                                    neighbour,
                                    out VoxelSample neighbourSample)
                                || neighbourSample.Type != sample.Type
                                || (strongestScheduledDamage.TryGetValue(
                                        neighbour,
                                        out float scheduledDamage)
                                    && scheduledDamage >= propagatedDamage))
                            {
                                continue;
                            }

                            strongestScheduledDamage[neighbour] =
                                propagatedDamage;
                            pending.Add(new MiningNode(
                                neighbour,
                                propagatedDamage));
                        }
                    }
                }
            }

            result = new VoxelExplosionResult(
                worldCenter,
                candidateCount,
                damagedCount,
                destroyedCount);
            HandleDestroyedSamples(destroyedCount);
            return damagedCount > 0;
        }

        internal bool StartPendingRebuild()
        {
            if (!initialized || samples == null || samples.Count == 0
                || rebuildTask != null)
            {
                return false;
            }

            int snapshotRevision = revision;
            var snapshot = new Dictionary<Vector3Int, VoxelSample>(samples);
            rebuildCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = rebuildCancellation.Token;
            rebuildTask = Task.Run(() => DynamicVoxelBodyBuilder.Build(
                snapshotRevision,
                snapshot,
                isoLevel,
                voxelSize,
                vertexPlacement,
                groupMap,
                massByType,
                defaultMassPerFullVoxel,
                convexSettings,
                VoxelConvexDecompositionPriority.Interactive,
                VoxelConvexDecompositionQuality.Interactive,
                cancellationToken));
            return true;
        }

        internal bool TryCommitCompletedRebuild()
        {
            if (rebuildTask == null || !rebuildTask.IsCompleted)
            {
                return false;
            }

            DynamicVoxelBodyBuildResult result = rebuildTask.Result;
            rebuildTask = null;
            rebuildCancellation?.Dispose();
            rebuildCancellation = null;
            if (result.Error != null)
            {
                if (result.Error is OperationCanceledException)
                {
                    registry?.QueueRebuild(this);
                    return true;
                }
                Debug.LogException(result.Error, this);
                failedRebuildCount++;
                if (failedRebuildCount <= 2)
                {
                    registry?.QueueRebuild(this);
                }
                return true;
            }
            if (result.Revision != revision)
            {
                registry?.QueueRebuild(this);
                return true;
            }
            if (result.Components.Count == 0)
            {
                registry?.UnregisterBody(this);
                Destroy(gameObject);
                return true;
            }

            CommitComponents(result.Components);
            failedRebuildCount = 0;
            return true;
        }

        private void CommitComponents(
            List<DynamicVoxelComponentBuildData> components)
        {
            Rigidbody sourceBody = GetComponent<Rigidbody>();
            Vector3 oldPivot = pivot;
            Vector3 oldPosition = sourceBody.position;
            Quaternion oldRotation = sourceBody.rotation;
            Vector3 oldLocalScale = transform.localScale;
            Vector3 oldWorldScale = transform.lossyScale;
            Matrix4x4 sourcePose = Matrix4x4.TRS(
                oldPosition,
                oldRotation,
                oldWorldScale);
            Vector3[] positions = new Vector3[components.Count];
            Vector3[] velocities = new Vector3[components.Count];
            for (int i = 0; i < components.Count; i++)
            {
                positions[i] = sourcePose.MultiplyPoint3x4(
                    components[i].Pivot - oldPivot);
                velocities[i] = sourceBody.GetPointVelocity(positions[i]);
            }
            Vector3 angularVelocity = sourceBody.angularVelocity;

            registry?.UnregisterBody(this);
            SetRigidbodyPose(sourceBody, positions[0], oldRotation);
            transform.localScale = oldLocalScale;
            ApplyBuildData(components[0]);
            sourceBody.velocity = velocities[0];
            sourceBody.angularVelocity = angularVelocity;
            registry?.RegisterBody(this);

            for (int i = 1; i < components.Count; i++)
            {
                DynamicVoxelComponentBuildData component = components[i];
                Material[] materials = ResolveMaterials(component.MeshData);
                GameObject childObject =
                    VoxelIntegrityRigidbodyFactory.CreateFromMarchingCubes(
                        component.Coordinates,
                        component.MeshData,
                        voxelSize,
                        null,
                        materials,
                        component.Mass,
                        component.MassProperties,
                        component.ConvexColliderMeshes);
                childObject.transform.localScale = oldWorldScale;
                Rigidbody childBody = childObject.GetComponent<Rigidbody>();
                SetRigidbodyPose(childBody, positions[i], oldRotation);
                DynamicVoxelBody child =
                    childObject.AddComponent<DynamicVoxelBody>();
                child.Initialize(
                    registry,
                    lineageId,
                    component,
                    isoLevel,
                    voxelSize,
                    vertexPlacement,
                    groupMap,
                    massByType,
                    defaultMassPerFullVoxel,
                    convexSettings,
                    oreDropTerrain,
                    voxelTypeCatalog,
                    fallbackMaterial);
                childBody.velocity = velocities[i];
                childBody.angularVelocity = angularVelocity;
            }
            Physics.SyncTransforms();
        }

        private static void SetRigidbodyPose(
            Rigidbody body,
            Vector3 position,
            Quaternion rotation)
        {
            RigidbodyInterpolation interpolation = body.interpolation;
            body.interpolation = RigidbodyInterpolation.None;
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
            body.interpolation = interpolation;
            body.WakeUp();
        }

        private void ApplyBuildData(DynamicVoxelComponentBuildData buildData)
        {
            samples = buildData.Samples;
            pivot = buildData.Pivot;
            raycastBvh = buildData.RaycastBvh;
            name = $"DynamicVoxelBody_{samples.Count}Voxels";

            MeshFilter filter = GetComponent<MeshFilter>();
            Mesh oldMesh = filter.sharedMesh;
            Mesh mesh = buildData.MeshData.CreateMesh(
                $"DynamicVoxelBody_{samples.Count}Voxels");
            mesh.hideFlags = HideFlags.DontSave;
            var vertices = new List<Vector3>(mesh.vertexCount);
            mesh.GetVertices(vertices);
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] -= pivot;
            }
            mesh.SetVertices(vertices);
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterials = ResolveMaterials(buildData.MeshData);
            CrystalOreSparkleOverlay.Synchronize(renderer, mesh);

            MeshCollider[] oldColliders = GetComponents<MeshCollider>();
            for (int i = 0; i < oldColliders.Length; i++)
            {
                Mesh oldColliderMesh = oldColliders[i].sharedMesh;
                oldColliders[i].enabled = false;
                DestroyOwnedObject(oldColliders[i]);
                if (oldColliderMesh != null && oldColliderMesh != oldMesh)
                {
                    DestroyOwnedObject(oldColliderMesh);
                }
            }
            VoxelIntegrityRigidbodyFactory.AddConvexMeshColliders(
                gameObject,
                buildData.ConvexColliderMeshes,
                samples.Count);

            Rigidbody body = GetComponent<Rigidbody>();
            body.mass = Mathf.Max(0.01f, buildData.Mass);
            body.centerOfMass = Vector3.zero;
            if (oldMesh != null)
            {
                DestroyOwnedObject(oldMesh);
            }
        }

        private Material[] ResolveMaterials(VoxelMeshData meshData)
        {
            return VoxelTypeUtility.ResolveMaterials(
                meshData,
                fallbackMaterial,
                voxelTypeCatalog != null
                    ? voxelTypeCatalog.Definitions
                    : null);
        }

        private bool TryHarvestOreVein(
            Vector3Int start,
            VoxelTypeId type,
            out int harvestedCount)
        {
            harvestedCount = 0;
            if (oreDropTerrain == null
                || !oreDropTerrain.IsRecoverableOreType(type))
            {
                return false;
            }

            HashSet<Vector3Int> component = FindConnectedOreVein(start, type);
            if (component.Count == 0)
            {
                return false;
            }

            VoxelMeshData meshData =
                MarchingCubesMesher.BuildCapturedTypeComponent(
                    component,
                    samples,
                    type,
                    isoLevel,
                    voxelSize,
                    vertexPlacement);
            if (meshData.Vertices.Count == 0
                || meshData.Triangles.Count == 0)
            {
                return false;
            }

            oreDropTerrain.CreateOreVeinBody(
                component,
                type,
                meshData,
                transform,
                pivot);
            foreach (Vector3Int coordinate in component)
            {
                RemoveSample(coordinate);
            }
            harvestedCount = component.Count;
            return true;
        }

        private HashSet<Vector3Int> FindConnectedOreVein(
            Vector3Int start,
            VoxelTypeId type)
        {
            var component = new HashSet<Vector3Int>();
            if (!samples.TryGetValue(start, out VoxelSample startSample)
                || !startSample.IsSolid(isoLevel)
                || startSample.Type != type)
            {
                return component;
            }

            var pending = new Queue<Vector3Int>();
            component.Add(start);
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                Vector3Int coordinate = pending.Dequeue();
                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            if (x == 0 && y == 0 && z == 0)
                            {
                                continue;
                            }
                            Vector3Int neighbour = coordinate
                                + new Vector3Int(x, y, z);
                            if (component.Contains(neighbour)
                                || !samples.TryGetValue(
                                    neighbour,
                                    out VoxelSample sample)
                                || !sample.IsSolid(isoLevel)
                                || sample.Type != type)
                            {
                                continue;
                            }

                            component.Add(neighbour);
                            pending.Enqueue(neighbour);
                        }
                    }
                }
            }
            return component;
        }

        private void RemoveSample(Vector3Int coordinate)
        {
            samples.Remove(coordinate);
            miningProgress.Reset(coordinate);
            registry?.RemoveCoordinate(
                this,
                new DynamicVoxelAddress(lineageId, coordinate));
        }

        private void HandleDestroyedSamples(int destroyedCount)
        {
            if (destroyedCount <= 0)
            {
                return;
            }

            revision++;
            failedRebuildCount = 0;
            rebuildCancellation?.Cancel();
            if (samples.Count == 0)
            {
                registry?.UnregisterBody(this);
                Destroy(gameObject);
            }
            else
            {
                registry?.QueueRebuild(this);
            }
        }

        private bool TryResolveSurfaceSample(
            Vector3 pointOnSolidSide,
            Vector3 surfacePoint,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            out Vector3Int coordinate,
            out VoxelSample sample)
        {
            coordinate = default;
            sample = default;
            Vector3 gridPosition =
                (pointOnSolidSide + pivot) / voxelSize;
            var cellOrigin = new Vector3Int(
                Mathf.FloorToInt(gridPosition.x),
                Mathf.FloorToInt(gridPosition.y),
                Mathf.FloorToInt(gridPosition.z));
            bool found = false;
            float bestSurfaceDistance = float.PositiveInfinity;
            float bestRayDepth = float.PositiveInfinity;
            float tieTolerance = voxelSize * voxelSize * 0.0001f;
            for (int i = 0; i < CellCornerOffsets.Length; i++)
            {
                Vector3Int candidate = cellOrigin + CellCornerOffsets[i];
                if (!TryGetSample(candidate, out VoxelSample candidateSample))
                {
                    continue;
                }
                Vector3 candidateLocal =
                    (Vector3)candidate * voxelSize - pivot;
                float surfaceDistance =
                    (candidateLocal - surfacePoint).sqrMagnitude;
                float rayDepth = Vector3.Dot(
                    candidateLocal - rayOrigin,
                    rayDirection);
                if (surfaceDistance < bestSurfaceDistance - tieTolerance
                    || (Mathf.Abs(surfaceDistance - bestSurfaceDistance)
                        <= tieTolerance
                        && rayDepth < bestRayDepth))
                {
                    coordinate = candidate;
                    sample = candidateSample;
                    bestSurfaceDistance = surfaceDistance;
                    bestRayDepth = rayDepth;
                    found = true;
                }
            }
            return found;
        }

        private void OnDestroy()
        {
            rebuildCancellation?.Cancel();
            rebuildCancellation?.Dispose();
            rebuildCancellation = null;
            registry?.UnregisterBody(this);
        }

        private static MiningNode RemoveStrongestNode(
            List<MiningNode> pending)
        {
            int strongestIndex = 0;
            for (int i = 1; i < pending.Count; i++)
            {
                if (pending[i].Damage > pending[strongestIndex].Damage)
                {
                    strongestIndex = i;
                }
            }

            MiningNode node = pending[strongestIndex];
            int lastIndex = pending.Count - 1;
            pending[strongestIndex] = pending[lastIndex];
            pending.RemoveAt(lastIndex);
            return node;
        }

        private static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private readonly struct MiningNode
        {
            public MiningNode(Vector3Int coordinate, float damage)
            {
                Coordinate = coordinate;
                Damage = damage;
            }

            public Vector3Int Coordinate { get; }
            public float Damage { get; }
        }
    }
}
