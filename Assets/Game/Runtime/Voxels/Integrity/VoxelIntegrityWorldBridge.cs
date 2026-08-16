using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Supernova.MinecraftCaves;
using UnityEngine;

namespace Supernova.Voxels.Integrity
{
    /// <summary>
    /// Production IVoxelTerrain proxy. Pickaxe and bomb calls still run through
    /// MinecraftCaveInfiniteWorld; this proxy records their exact mutations and
    /// performs the integrity pass before returning control to gameplay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoxelIntegrityWorldBridge : MonoBehaviour, IVoxelTerrain
    {
        [SerializeField] private MinecraftCaveInfiniteWorld terrain;
        [SerializeField, Min(64)] private int maxVisitedVoxels = 16384;
        [SerializeField, Min(0.001f)]
        private float defaultMassPerFullVoxel =
            MinedOreDrop.DefaultMassDensity;
        [SerializeField] private bool showDebugOverlay;
        [SerializeField, Min(1)] private int maxConcurrentBodyBuilds = 2;
        [SerializeField, Min(1)] private int maxBodyCommitsPerFrame = 1;
        [SerializeField, Range(0.01f, 1f)]
        private float colliderConcavityThreshold = 0.1f;
        [SerializeField, Range(1, 64)]
        private int maxConvexCollidersPerBody = 8;


        private readonly Dictionary<Vector3Int, VoxelSample> removedSamples =
            new Dictionary<Vector3Int, VoxelSample>();
        private InfiniteVoxelWorld capturedWorld;
        private VoxelIntegritySearch search;
        private readonly System.Diagnostics.Stopwatch passStopwatch =
            new System.Diagnostics.Stopwatch();
        private readonly Queue<IntegrityBodyBuildRequest> queuedBodyBuilds =
            new Queue<IntegrityBodyBuildRequest>();
        private readonly List<Task<IntegrityBodyBuildResult>> activeBodyBuilds =
            new List<Task<IntegrityBodyBuildResult>>();
        private DynamicVoxelBodyRegistry dynamicBodyRegistry;



        public MinecraftCaveInfiniteWorld SourceTerrain => terrain;
        public Transform TerrainTransform =>
            terrain != null ? terrain.TerrainTransform : transform;
        public InfiniteVoxelWorld World => terrain != null ? terrain.World : null;
        public VoxelTypeCatalog VoxelTypeCatalog =>
            terrain != null ? terrain.VoxelTypeCatalog : null;
        public float VoxelSize => terrain != null ? terrain.VoxelSize : 1f;
        public float IsoLevel => terrain != null ? terrain.IsoLevel : 0f;
        public DynamicVoxelBodyRegistry DynamicBodyRegistry =>
            dynamicBodyRegistry != null
                ? dynamicBodyRegistry
                : GetComponent<DynamicVoxelBodyRegistry>();

        public int LastSeedCount { get; private set; }
        public int LastFillCount { get; private set; }
        public int LastVisitedVoxelCount { get; private set; }

        public int LastRemovedVoxelCount { get; private set; }
        public int LastComponentCount { get; private set; }
        public int LastSupportedComponentCount { get; private set; }
        public int LastCollapsedComponentCount { get; private set; }
        public int LastCollapsedVoxelCount { get; private set; }
        public float LastTerrainMutationMilliseconds { get; private set; }
        public float LastSearchMilliseconds { get; private set; }
        public float LastCollapseMilliseconds { get; private set; }
        public float LastPassMilliseconds { get; private set; }
        public float LastBodyBuildMilliseconds { get; private set; }
        public float LastBodyCommitMilliseconds { get; private set; }
        public int PendingBodyBuildCount =>
            queuedBodyBuilds.Count + activeBodyBuilds.Count;

        public float LastMillisecondsPerFill =>
            LastFillCount > 0
                ? LastSearchMilliseconds / LastFillCount
                : 0f;

        public float LastCollapsedMass { get; private set; }

        public void Configure(
            MinecraftCaveInfiniteWorld sourceTerrain,
            int visitedVoxelLimit = 16384)
        {
            terrain = sourceTerrain;
            maxVisitedVoxels = Mathf.Max(64, visitedVoxelLimit);
            search = new VoxelIntegritySearch(maxVisitedVoxels);
        }

        public Vector3Int WorldPositionToVoxel(Vector3 worldPosition)
        {
            return terrain != null
                ? terrain.WorldPositionToVoxel(worldPosition)
                : default;
        }

        public bool TryMineVoxel(
            Vector3Int coordinate,
            out VoxelMiningResult result)
        {
            BeginCapture();
            try
            {
                if (terrain == null)
                {
                    result = default;
                    return false;
                }

                return terrain.TryMineVoxel(coordinate, out result);
            }
            finally
            {
                EndCapture();
            }
        }

        public bool TryMineBrush(
            Vector3Int primaryCoordinate,
            Vector3 worldDirection,
            VoxelMiningBrushSettings settings,
            out VoxelMiningBrushResult result)
        {
            BeginCapture();
            try
            {
                if (terrain == null)
                {
                    result = default;
                    return false;
                }

                return terrain.TryMineBrush(
                    primaryCoordinate,
                    worldDirection,
                    settings,
                    out result);
            }
            finally
            {
                EndCapture();
            }
        }

        public bool TryMineExplosion(
            Vector3 worldCenter,
            VoxelExplosionSettings settings,
            out VoxelExplosionResult result)
        {
            BeginCapture();
            try
            {
                VoxelExplosionResult terrainResult = default;
                bool terrainMined = terrain != null
                    && terrain.TryMineExplosion(
                        worldCenter,
                        settings,
                        out terrainResult);
                EnsureDynamicBodyRegistry();
                VoxelExplosionResult dynamicResult = default;
                bool dynamicMined = dynamicBodyRegistry != null
                    && dynamicBodyRegistry.TryMineExplosion(
                        worldCenter,
                        settings,
                        out dynamicResult);
                result = new VoxelExplosionResult(
                    worldCenter,
                    terrainResult.CandidateCount
                        + dynamicResult.CandidateCount,
                    terrainResult.DamagedCount
                        + dynamicResult.DamagedCount,
                    terrainResult.DestroyedCount
                        + dynamicResult.DestroyedCount);
                return terrainMined || dynamicMined;
            }
            finally
            {
                EndCapture();
            }
        }

        public bool TrySetVoxelAndRebuild(
            int worldX,
            int worldY,
            int worldZ,
            float density,
            VoxelTypeId type)
        {
            BeginCapture();
            try
            {
                return terrain != null
                    && terrain.TrySetVoxelAndRebuild(
                        worldX,
                        worldY,
                        worldZ,
                        density,
                        type);
            }
            finally
            {
                EndCapture();
            }
        }

        private void Awake()
        {
            EnsureSearch();
            EnsureDynamicBodyRegistry();
        }

        private void OnDisable()
        {
            StopCapture();
            queuedBodyBuilds.Clear();
            activeBodyBuilds.Clear();
        }

        private void Update()
        {
            CommitCompletedBodyBuilds();
            StartQueuedBodyBuilds();
        }


        private void BeginCapture()
        {
            StopCapture();
            passStopwatch.Restart();

            removedSamples.Clear();
            capturedWorld = World;
            if (capturedWorld != null)
            {
                capturedWorld.SampleChanged += OnSampleChanged;
            }
        }

        private void EndCapture()
        {
            StopCapture();
            float terrainMilliseconds =
                (float)passStopwatch.Elapsed.TotalMilliseconds;
            ResetLastResult();
            LastTerrainMutationMilliseconds = terrainMilliseconds;
            if (removedSamples.Count == 0)
            {
                passStopwatch.Stop();
                LastPassMilliseconds =
                    (float)passStopwatch.Elapsed.TotalMilliseconds;
                return;
            }

            try
            {
                AnalyzeRemovedVoxels();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                passStopwatch.Stop();
                LastPassMilliseconds =
                    (float)passStopwatch.Elapsed.TotalMilliseconds;
            }
        }

        private void StopCapture()
        {
            if (capturedWorld == null)
            {
                return;
            }

            capturedWorld.SampleChanged -= OnSampleChanged;
            capturedWorld = null;
        }

        private void OnSampleChanged(
            Vector3Int coordinate,
            VoxelSample previous,
            VoxelSample current)
        {
            if (!previous.IsSolid(IsoLevel)
                || current.IsSolid(IsoLevel)
                || removedSamples.ContainsKey(coordinate))
            {
                return;
            }

            removedSamples.Add(coordinate, previous);
        }

        private void AnalyzeRemovedVoxels()
        {
            InfiniteVoxelWorld world = World;
            if (world == null)
            {
                Debug.LogWarning(
                    "Voxel integrity pass skipped because the generated world "
                    + "is unavailable.",
                    this);
                return;
            }

            var supportTypes = new HashSet<VoxelTypeId>();
            IReadOnlyList<VoxelTypeDefinition> definitions =
                VoxelTypeCatalog != null
                    ? VoxelTypeCatalog.Definitions
                    : null;
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    VoxelTypeDefinition definition = definitions[i];
                    if (definition != null
                        && definition.IsStructuralSupport)
                    {
                        supportTypes.Add(definition.TypeId);
                    }
                }
            }

            EnsureSearch();
            var map = new InfiniteVoxelIntegrityMap(
                world,
                IsoLevel,
                supportTypes);
            var searchStopwatch =
                System.Diagnostics.Stopwatch.StartNew();
            VoxelIntegrityResult result = search.Analyze(
                removedSamples.Keys,
                map);
            searchStopwatch.Stop();
            LastSearchMilliseconds =
                (float)searchStopwatch.Elapsed.TotalMilliseconds;

            LastSeedCount = result.SeedCount;
            LastFillCount = result.FillCount;
            LastVisitedVoxelCount = result.VisitedVoxelCount;

            LastRemovedVoxelCount = removedSamples.Count;
            LastComponentCount = result.Components.Count;
            var collapseStopwatch =
                System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < result.Components.Count; i++)
            {
                VoxelIntegrityComponent component = result.Components[i];
                if (component.IsSupported)
                {
                    LastSupportedComponentCount++;
                    continue;
                }

                CollapseComponent(component);
            }

            collapseStopwatch.Stop();
            LastCollapseMilliseconds =
                (float)collapseStopwatch.Elapsed.TotalMilliseconds;

            Debug.Log(
                $"Integrity pass: {LastRemovedVoxelCount} removed, "
                + $"{LastSeedCount} seeds, {LastFillCount} fills, "
                + $"{LastVisitedVoxelCount} unique visited, "
                + $"{LastComponentCount} adjacent components, "
                + $"{LastCollapsedComponentCount} collapsed "
                + $"({LastCollapsedVoxelCount} voxels, "
                + $"{LastCollapsedMass:F2} mass).",
                this);
        }


        private void CollapseComponent(VoxelIntegrityComponent component)
        {
            if (component.Voxels.Count == 0)
            {
                return;
            }

            InfiniteVoxelWorld world = World;
            var componentVoxels =
                new List<Vector3Int>(component.Voxels);
            var capturedSamples =
                new Dictionary<Vector3Int, VoxelSample>(
                    componentVoxels.Count);
            for (int i = 0; i < componentVoxels.Count; i++)
            {
                Vector3Int coordinate = componentVoxels[i];
                if (world.TryGetSample(
                        coordinate.x,
                        coordinate.y,
                        coordinate.z,
                        out VoxelSample sample)
                    && sample.IsSolid(IsoLevel))
                {
                    capturedSamples.Add(coordinate, sample);
                }
            }
            if (capturedSamples.Count == 0)
            {
                Debug.LogWarning(
                    "An unsupported component could not be snapshotted and "
                    + "was left in the world.",
                    this);
                return;
            }

            IReadOnlyList<VoxelTypeDefinition> definitions =
                VoxelTypeCatalog != null
                    ? VoxelTypeCatalog.Definitions
                    : null;
            VoxelGroupMap groupMap =
                VoxelGroupMap.FromDefinitions(definitions);
            Dictionary<VoxelTypeId, float> massByType =
                BuildMassByTypeLookup();
            var convexSettings = new VoxelConvexDecompositionSettings(
                colliderConcavityThreshold,
                maxConvexCollidersPerBody);
            queuedBodyBuilds.Enqueue(
                new IntegrityBodyBuildRequest(
                    componentVoxels,
                    capturedSamples,
                    IsoLevel,
                    VoxelSize,
                    terrain.VertexPlacement,
                    groupMap,
                    massByType,
                    defaultMassPerFullVoxel,
                    convexSettings));

            for (int i = 0; i < componentVoxels.Count; i++)
            {
                Vector3Int coordinate = componentVoxels[i];
                terrain.TrySetVoxelAndRebuild(
                    coordinate.x,
                    coordinate.y,
                    coordinate.z,
                    IsoLevel - 1f,
                    VoxelTypeId.Air);
            }

            LastCollapsedComponentCount++;
            LastCollapsedVoxelCount += componentVoxels.Count;
            StartQueuedBodyBuilds();
        }

        private void StartQueuedBodyBuilds()
        {
            int concurrency = Mathf.Max(1, maxConcurrentBodyBuilds);
            while (activeBodyBuilds.Count < concurrency
                && queuedBodyBuilds.Count > 0)
            {
                IntegrityBodyBuildRequest request =
                    queuedBodyBuilds.Dequeue();
                activeBodyBuilds.Add(
                    Task.Run(() => BuildBodyData(request)));
            }
        }

        private void CommitCompletedBodyBuilds()
        {
            int remainingCommits = Mathf.Max(
                1,
                maxBodyCommitsPerFrame);
            for (int i = activeBodyBuilds.Count - 1;
                i >= 0 && remainingCommits > 0;
                i--)
            {
                Task<IntegrityBodyBuildResult> task = activeBodyBuilds[i];
                if (!task.IsCompleted)
                {
                    continue;
                }

                activeBodyBuilds.RemoveAt(i);
                remainingCommits--;
                IntegrityBodyBuildResult result = task.Result;
                LastBodyBuildMilliseconds = result.BuildMilliseconds;
                if (result.Error != null
                    || result.Components == null
                    || result.Components.Count == 0)
                {
                    RestoreCapturedComponent(result.Request);
                    LastCollapsedComponentCount = Mathf.Max(
                        0,
                        LastCollapsedComponentCount - 1);
                    LastCollapsedVoxelCount = Mathf.Max(
                        0,
                        LastCollapsedVoxelCount
                        - result.Request.Component.Count);
                    if (result.Error != null)
                    {
                        Debug.LogException(result.Error, this);
                    }
                    else
                    {
                        Debug.LogWarning(
                            "An unsupported component produced no closed "
                            + "surface and was restored.",
                            this);
                    }
                    continue;
                }

                var commitStopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                EnsureDynamicBodyRegistry();
                IReadOnlyList<VoxelTypeDefinition> definitions =
                    VoxelTypeCatalog != null
                        ? VoxelTypeCatalog.Definitions
                        : null;
                Material fallback =
                    terrain != null
                    && terrain.BaseSolidVoxelType != null
                        ? terrain.BaseSolidVoxelType.Material
                        : null;
                Guid lineageId = Guid.NewGuid();
                for (int componentIndex = 0;
                    componentIndex < result.Components.Count;
                    componentIndex++)
                {
                    DynamicVoxelComponentBuildData component =
                        result.Components[componentIndex];
                    Material[] materials =
                        VoxelTypeUtility.ResolveMaterials(
                            component.MeshData,
                            fallback,
                            definitions);
                    GameObject rigidObject =
                        VoxelIntegrityRigidbodyFactory
                            .CreateFromMarchingCubes(
                                component.Coordinates,
                                component.MeshData,
                                result.Request.VoxelSize,
                                TerrainTransform,
                                materials,
                                component.Mass,
                                component.MassProperties,
                                component.ConvexColliderMeshes);
                    DynamicVoxelBody dynamicBody =
                        rigidObject.AddComponent<DynamicVoxelBody>();
                    dynamicBody.Initialize(
                        dynamicBodyRegistry,
                        lineageId,
                        component,
                        result.Request.IsoLevel,
                        result.Request.VoxelSize,
                        result.Request.VertexPlacement,
                        result.Request.GroupMap,
                        result.Request.MassByType,
                        result.Request.DefaultMassPerFullVoxel,
                        result.Request.ConvexSettings,
                        terrain,
                        VoxelTypeCatalog,
                        fallback);
                    LastCollapsedMass +=
                        rigidObject.GetComponent<Rigidbody>().mass;
                }
                commitStopwatch.Stop();
                LastBodyCommitMilliseconds =
                    (float)commitStopwatch.Elapsed.TotalMilliseconds;
            }
        }

        private void RestoreCapturedComponent(
                    IntegrityBodyBuildRequest request)
        {
            if (terrain == null || World == null)
            {
                return;
            }

            foreach (KeyValuePair<Vector3Int, VoxelSample> pair
                in request.Samples)
            {
                Vector3Int coordinate = pair.Key;
                if (World.TryGetSample(
                        coordinate.x,
                        coordinate.y,
                        coordinate.z,
                        out VoxelSample current)
                    && current.IsSolid(IsoLevel))
                {
                    continue;
                }

                VoxelSample sample = pair.Value;
                terrain.TrySetVoxelAndRebuild(
                    coordinate.x,
                    coordinate.y,
                    coordinate.z,
                    sample.Density,
                    sample.Type);
            }
        }

        private static IntegrityBodyBuildResult BuildBodyData(
                    IntegrityBodyBuildRequest request)
        {
            var stopwatch =
                System.Diagnostics.Stopwatch.StartNew();
            try
            {
                DynamicVoxelBodyBuildResult build =
                    DynamicVoxelBodyBuilder.Build(
                        0,
                        request.Samples,
                        request.IsoLevel,
                        request.VoxelSize,
                        request.VertexPlacement,
                        request.GroupMap,
                        request.MassByType,
                        request.DefaultMassPerFullVoxel,
                        request.ConvexSettings);

                stopwatch.Stop();
                return new IntegrityBodyBuildResult(
                    request,
                    build.Components,
                    (float)stopwatch.Elapsed.TotalMilliseconds,
                    build.Error);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                return new IntegrityBodyBuildResult(
                    request,
                    null,
                    (float)stopwatch.Elapsed.TotalMilliseconds,
                    exception);
            }
        }





        private Dictionary<VoxelTypeId, float> BuildMassByTypeLookup()
        {
            var lookup = new Dictionary<VoxelTypeId, float>();
            if (terrain == null)
            {
                return lookup;
            }
            for (int featureIndex = 0;
                featureIndex < terrain.OreFeatures.Count;
                featureIndex++)
            {
                var feature = terrain.OreFeatures[featureIndex];
                if (feature != null && feature.ResultVoxelType != null)
                {
                    lookup[feature.ResultVoxelType.TypeId] =
                        feature.MassDensity;
                }
            }
            return lookup;
        }

        private void EnsureSearch()
        {
            if (search == null)
            {
                search = new VoxelIntegritySearch(maxVisitedVoxels);
            }
        }

        private void EnsureDynamicBodyRegistry()
        {
            if (dynamicBodyRegistry == null)
            {
                dynamicBodyRegistry =
                    GetComponent<DynamicVoxelBodyRegistry>();
                if (dynamicBodyRegistry == null)
                {
                    dynamicBodyRegistry =
                        gameObject.AddComponent<DynamicVoxelBodyRegistry>();
                }
            }
            dynamicBodyRegistry.Configure(
                maxConcurrentBodyBuilds,
                maxBodyCommitsPerFrame);
        }

        private void ResetLastResult()
        {
            LastSeedCount = 0;
            LastFillCount = 0;
            LastVisitedVoxelCount = 0;
            LastRemovedVoxelCount = 0;
            LastComponentCount = 0;
            LastSupportedComponentCount = 0;
            LastCollapsedComponentCount = 0;
            LastCollapsedVoxelCount = 0;
            LastCollapsedMass = 0f;
            LastTerrainMutationMilliseconds = 0f;
            LastSearchMilliseconds = 0f;
            LastCollapseMilliseconds = 0f;
            LastPassMilliseconds = 0f;

        }

        private sealed class IntegrityBodyBuildRequest
        {
            public IntegrityBodyBuildRequest(
                List<Vector3Int> component,
                Dictionary<Vector3Int, VoxelSample> samples,
                float isoLevel,
                float voxelSize,
                MarchingCubesVertexPlacement vertexPlacement,
                VoxelGroupMap groupMap,
                Dictionary<VoxelTypeId, float> massByType,
                float defaultMassPerFullVoxel,
                VoxelConvexDecompositionSettings convexSettings)
            {
                Component = component;
                Samples = samples;
                IsoLevel = isoLevel;
                VoxelSize = voxelSize;
                VertexPlacement = vertexPlacement;
                GroupMap = groupMap;
                MassByType = massByType;
                DefaultMassPerFullVoxel = defaultMassPerFullVoxel;
                ConvexSettings = convexSettings;
            }

            public List<Vector3Int> Component { get; }
            public Dictionary<Vector3Int, VoxelSample> Samples { get; }
            public float IsoLevel { get; }
            public float VoxelSize { get; }
            public MarchingCubesVertexPlacement VertexPlacement { get; }
            public VoxelGroupMap GroupMap { get; }
            public Dictionary<VoxelTypeId, float> MassByType { get; }
            public float DefaultMassPerFullVoxel { get; }
            public VoxelConvexDecompositionSettings ConvexSettings { get; }
        }

        private sealed class IntegrityBodyBuildResult
        {
            public IntegrityBodyBuildResult(
                IntegrityBodyBuildRequest request,
                List<DynamicVoxelComponentBuildData> components,
                float buildMilliseconds,
                Exception error)
            {
                Request = request;
                Components = components;
                BuildMilliseconds = buildMilliseconds;
                Error = error;
            }

            public IntegrityBodyBuildRequest Request { get; }
            public List<DynamicVoxelComponentBuildData> Components { get; }
            public float BuildMilliseconds { get; }
            public Exception Error { get; }
        }


        private void OnGUI()
        {
            if (!showDebugOverlay)
            {
                return;
            }

            GUILayout.BeginArea(
                new Rect(16f, 16f, 560f, 214f),
                GUI.skin.box);
            GUILayout.Label("Voxel Integrity");
            GUILayout.Label(
                "Actual Player prefab: pickaxe / bomb inputs use the proxy.");
            GUILayout.Label(
                "Generation: "
                + (terrain != null
                    ? terrain.GenerationStage.ToString()
                    : "No world"));
            GUILayout.Label(
                $"Timing: terrain {LastTerrainMutationMilliseconds:F1} ms, "
                + $"search {LastSearchMilliseconds:F1} ms "
                + $"({LastMillisecondsPerFill:F2} ms/fill), "
                + $"collapse {LastCollapseMilliseconds:F1} ms, "
                + $"total {LastPassMilliseconds:F1} ms");
            GUILayout.Label(
                $"Async bodies: pending {PendingBodyBuildCount}, "
                + $"last worker {LastBodyBuildMilliseconds:F1} ms, "
                + $"main commit {LastBodyCommitMilliseconds:F1} ms");

            GUILayout.Label(
                            $"Last pass: removed {LastRemovedVoxelCount}, "
                            + $"seeds {LastSeedCount}, fills {LastFillCount}, "
                            + $"visited {LastVisitedVoxelCount}, "
                            + $"components {LastComponentCount}, "
                            + $"supported {LastSupportedComponentCount}, "
                            + $"collapsed {LastCollapsedComponentCount} "
                            + $"({LastCollapsedVoxelCount} voxels, "
                            + $"{LastCollapsedMass:F2} mass)");
            GUILayout.EndArea();
        }
    }
}
