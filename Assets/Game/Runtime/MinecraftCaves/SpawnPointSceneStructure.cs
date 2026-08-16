using System;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Aligns a scene-authored structure so its player marker matches the world spawn pose.
    /// The structure remains a normal scene object, while its placement follows the same
    /// deterministic spawn selected by <see cref="MinecraftCaveInfiniteWorld"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnPointSceneStructure : MonoBehaviour
    {
        [SerializeField] private Transform playerSpawnPoint;
        [Header("Mission Extraction")]
        [Tooltip(
            "Cell-local volume that accepts treasure for mission scoring. "
            + "Keep this inside the enclosed cabin so exterior platforms and "
            + "runtime portal endpoints cannot contribute value.")]
        [SerializeField] private Vector3 missionExtractionLocalCenter =
            new Vector3(-0.62f, 2f, 0f);
        [SerializeField] private Vector3 missionExtractionLocalSize =
            new Vector3(6f, 4f, 6f);
        [SerializeField, Min(0f)] private float terrainClearancePadding = 0.75f;
        [SerializeField, Min(1f)] private float exitPassageLength = 12f;
        [SerializeField, Min(1f)] private float exitPassageWidth = 6f;
        [SerializeField, Min(1f)] private float exitPassageHeight = 4.5f;
        [Header("Landing Ground")]
        [Tooltip(
            "Extra walkable ground beyond the Cell footprint. Authored "
            + "lengths follow the Cell scale.")]
        [SerializeField, Min(0f)] private float landingGroundMargin = 6f;
        [SerializeField, Min(0.1f)] private float landingGroundThickness = 3f;
        [SerializeField, Min(1f)] private float landingGroundHeadroom = 4f;
        [Header("Terrain Blending")]
        [Tooltip(
            "Soft density transition outside the guaranteed landing-ground, "
            + "Cell-clearance, passage, and shaft cores. Authored lengths "
            + "follow the Cell scale.")]
        [SerializeField, Min(0f)] private float terrainTransitionWidth = 1f;
        [Header("Landing Shaft")]
        [Tooltip(
            "Carves the full horizontal footprint above the landing Cell "
            + "through the top boundary of the voxel world.")]
        [SerializeField] private bool carveLandingShaftToWorldTop = true;

        private bool hasExitTarget;
        private Vector3 exitTargetWorldPosition;

        public Transform PlayerSpawnPoint => ResolvePlayerSpawnPoint();
        public Bounds MissionExtractionLocalBounds
        {
            get
            {
                Vector3 safeSize = new Vector3(
                    Mathf.Max(0.1f, Mathf.Abs(missionExtractionLocalSize.x)),
                    Mathf.Max(0.1f, Mathf.Abs(missionExtractionLocalSize.y)),
                    Mathf.Max(0.1f, Mathf.Abs(missionExtractionLocalSize.z)));
                return new Bounds(missionExtractionLocalCenter, safeSize);
            }
        }
        public bool HasExitTarget => hasExitTarget;
        public Vector3 ExitTargetWorldPosition => exitTargetWorldPosition;
        public event Action Placed;

        public void Configure(Transform spawnPoint)
        {
            playerSpawnPoint = spawnPoint;
        }

        public void PlaceAt(Vector3 playerWorldPosition, Quaternion playerWorldRotation)
        {
            Transform spawnPoint = ResolvePlayerSpawnPoint();
            Quaternion rootToSpawnRotation =
                Quaternion.Inverse(transform.rotation) * spawnPoint.rotation;

            transform.rotation =
                playerWorldRotation * Quaternion.Inverse(rootToSpawnRotation);
            Vector3 rootToSpawnPosition = spawnPoint.position - transform.position;
            transform.position = playerWorldPosition - rootToSpawnPosition;
            Placed?.Invoke();
        }

        public void SetExitTarget(Vector3 targetWorldPosition)
        {
            exitTargetWorldPosition = targetWorldPosition;
            hasExitTarget = true;
        }

        public void ClearExitTarget()
        {
            hasExitTarget = false;
        }

        public float GetMinimumExitTargetDistance()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return GetWorldLength(
                    exitPassageWidth * 0.5f + terrainClearancePadding);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Transform spawnPoint = ResolvePlayerSpawnPoint();
            float worldPadding = GetWorldLength(terrainClearancePadding);
            float maximumHorizontalDistance = 0f;
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            for (int xIndex = 0; xIndex <= 1; xIndex++)
            {
                for (int yIndex = 0; yIndex <= 1; yIndex++)
                {
                    for (int zIndex = 0; zIndex <= 1; zIndex++)
                    {
                        var corner = new Vector3(
                            xIndex == 0 ? minimum.x : maximum.x,
                            yIndex == 0 ? minimum.y : maximum.y,
                            zIndex == 0 ? minimum.z : maximum.z);
                        float horizontalDistance = Vector3.ProjectOnPlane(
                            corner - spawnPoint.position,
                            spawnPoint.up).magnitude;
                        maximumHorizontalDistance = Mathf.Max(
                            maximumHorizontalDistance,
                            horizontalDistance);
                    }
                }
            }

            return maximumHorizontalDistance + worldPadding;
        }

        public int StabilizeLandingGround(
            InfiniteVoxelWorld world,
            Transform terrainTransform,
            float voxelSize,
            float solidDensity,
            VoxelTypeId solidType,
            float airDensity,
            out int clearedHeadroomSamples)
        {
            clearedHeadroomSamples = 0;
            if (world == null || terrainTransform == null || voxelSize <= 0f)
            {
                return 0;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return 0;
            }

            float worldPadding = GetWorldLength(terrainClearancePadding);
            float worldMargin = GetWorldLength(landingGroundMargin);
            float worldThickness = GetWorldLength(landingGroundThickness);
            float worldHeadroom = GetWorldLength(landingGroundHeadroom);
            float worldTransitionWidth =
                GetWorldLength(terrainTransitionWidth);
            BuildStructureFootprint(
                renderers,
                worldPadding,
                out float footprintMinimumRight,
                out float footprintMaximumRight,
                out float footprintMinimumForward,
                out float footprintMaximumForward);

            Vector3 structureFloor = transform.position;
            Vector3 structureRight = transform.right;
            Vector3 structureForward = transform.forward;
            Vector3 structureUp = transform.up;
            Bounds iterationBounds = BuildLandingGroundBounds(
                structureFloor,
                structureRight,
                structureForward,
                structureUp,
                footprintMinimumRight
                    - worldMargin
                    - worldTransitionWidth,
                footprintMaximumRight
                    + worldMargin
                    + worldTransitionWidth,
                footprintMinimumForward
                    - worldMargin
                    - worldTransitionWidth,
                footprintMaximumForward
                    + worldMargin
                    + worldTransitionWidth,
                worldThickness,
                worldHeadroom);

            Vector3 minimumVoxel =
                terrainTransform.InverseTransformPoint(iterationBounds.min)
                / voxelSize;
            Vector3 maximumVoxel =
                terrainTransform.InverseTransformPoint(iterationBounds.max)
                / voxelSize;
            int minimumX = Mathf.FloorToInt(
                Mathf.Min(minimumVoxel.x, maximumVoxel.x));
            int minimumY = Mathf.Max(
                0,
                Mathf.FloorToInt(
                    Mathf.Min(minimumVoxel.y, maximumVoxel.y)));
            int minimumZ = Mathf.FloorToInt(
                Mathf.Min(minimumVoxel.z, maximumVoxel.z));
            int maximumX = Mathf.CeilToInt(
                Mathf.Max(minimumVoxel.x, maximumVoxel.x));
            int maximumY = Mathf.Min(
                VoxelColumnChunkData.Height - 1,
                Mathf.CeilToInt(
                    Mathf.Max(minimumVoxel.y, maximumVoxel.y)));
            int maximumZ = Mathf.CeilToInt(
                Mathf.Max(minimumVoxel.z, maximumVoxel.z));
            float maximumRoundedDistance =
                worldMargin + worldTransitionWidth;
            float maximumRoundedDistanceSquared =
                maximumRoundedDistance * maximumRoundedDistance;
            float isoLevel = (solidDensity + airDensity) * 0.5f;
            int supportedSamples = 0;

            for (int z = minimumZ; z <= maximumZ; z++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    Vector3 columnWorldPosition =
                        terrainTransform.TransformPoint(
                            new Vector3(x, 0f, z) * voxelSize);
                    Vector3 columnOffset =
                        columnWorldPosition - structureFloor;
                    float rightDistance = Vector3.Dot(
                        columnOffset,
                        structureRight);
                    float forwardDistance = Vector3.Dot(
                        columnOffset,
                        structureForward);
                    float rightOutside = DistanceOutsideInterval(
                        rightDistance,
                        footprintMinimumRight,
                        footprintMaximumRight);
                    float forwardOutside = DistanceOutsideInterval(
                        forwardDistance,
                        footprintMinimumForward,
                        footprintMaximumForward);
                    float roundedDistanceSquared =
                        rightOutside * rightOutside
                        + forwardOutside * forwardOutside;
                    if (roundedDistanceSquared
                        > maximumRoundedDistanceSquared)
                    {
                        continue;
                    }
                    float roundedDistance =
                        Mathf.Sqrt(roundedDistanceSquared);
                    float blendWeight = EvaluateTransitionWeight(
                        roundedDistance - worldMargin,
                        worldTransitionWidth);
                    if (blendWeight <= 0f)
                    {
                        continue;
                    }

                    for (int y = minimumY; y <= maximumY; y++)
                    {
                        if (!world.TryGetSample(
                            x,
                            y,
                            z,
                            out VoxelSample sample))
                        {
                            continue;
                        }

                        Vector3 sampleWorldPosition =
                            terrainTransform.TransformPoint(
                                new Vector3(x, y, z) * voxelSize);
                        float heightFromFloor = Vector3.Dot(
                            sampleWorldPosition - structureFloor,
                            structureUp);
                        if (heightFromFloor >= -worldThickness
                            && heightFromFloor <= 0f)
                        {
                            float blendedDensity = Mathf.Lerp(
                                sample.Density,
                                solidDensity,
                                blendWeight);
                            VoxelTypeId blendedType =
                                ResolveBlendedSolidType(
                                    sample,
                                    blendedDensity,
                                    isoLevel,
                                    solidType,
                                    blendWeight);
                            if (Mathf.Approximately(
                                    blendedDensity,
                                    sample.Density)
                                && blendedType == sample.Type)
                            {
                                continue;
                            }
                            world.SetVoxel(
                                x,
                                y,
                                z,
                                blendedDensity,
                                blendedType);
                            supportedSamples++;
                        }
                        else if (heightFromFloor > 0f
                            && heightFromFloor <= worldHeadroom)
                        {
                            float blendedDensity = Mathf.Lerp(
                                sample.Density,
                                airDensity,
                                blendWeight);
                            VoxelTypeId blendedType =
                                blendedDensity < isoLevel
                                    ? VoxelTypeId.Air
                                    : sample.Type;
                            if (Mathf.Approximately(
                                    blendedDensity,
                                    sample.Density)
                                && blendedType == sample.Type)
                            {
                                continue;
                            }
                            world.SetVoxel(
                                x,
                                y,
                                z,
                                blendedDensity,
                                blendedType);
                            clearedHeadroomSamples++;
                        }
                    }
                }
            }

            return supportedSamples;
        }

        public int CarveTerrainClearance(
            InfiniteVoxelWorld world,
            Transform terrainTransform,
            float voxelSize,
            float airDensity)
        {
            if (world == null || terrainTransform == null || voxelSize <= 0f)
            {
                return 0;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return 0;
            }

            Bounds clearanceBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                clearanceBounds.Encapsulate(renderers[i].bounds);
            }
            float worldPadding = GetWorldLength(terrainClearancePadding);
            float worldPassageWidth = GetWorldLength(exitPassageWidth);
            float worldPassageHeight = GetWorldLength(exitPassageHeight);
            float worldTransitionWidth =
                GetWorldLength(terrainTransitionWidth);
            clearanceBounds.Expand(worldPadding * 2f);
            BuildStructureFootprint(
                renderers,
                worldPadding,
                out float shaftMinimumRight,
                out float shaftMaximumRight,
                out float shaftMinimumForward,
                out float shaftMaximumForward);
            Transform spawnPoint = ResolvePlayerSpawnPoint();
            BuildExitPassage(
                spawnPoint,
                out Vector3 passageStart,
                out Vector3 passageEnd,
                out Vector3 passageForwardAxis,
                out Vector3 passageRightAxis,
                out Vector3 passageUpAxis,
                out float passageLength);
            Bounds passageBounds = BuildExitPassageBounds(
                passageStart,
                passageEnd,
                passageRightAxis,
                passageUpAxis,
                worldPassageWidth,
                worldPassageHeight);
            Bounds iterationBounds = clearanceBounds;
            iterationBounds.Encapsulate(passageBounds);
            iterationBounds.Expand(
                Vector3.one * worldTransitionWidth * 2f);

            Vector3 minVoxel = terrainTransform.InverseTransformPoint(iterationBounds.min)
                / voxelSize;
            Vector3 maxVoxel = terrainTransform.InverseTransformPoint(iterationBounds.max)
                / voxelSize;
            int minX = Mathf.FloorToInt(Mathf.Min(minVoxel.x, maxVoxel.x));
            int minY = Mathf.FloorToInt(Mathf.Min(minVoxel.y, maxVoxel.y));
            int minZ = Mathf.FloorToInt(Mathf.Min(minVoxel.z, maxVoxel.z));
            int maxX = Mathf.CeilToInt(Mathf.Max(minVoxel.x, maxVoxel.x));
            int maxY = Mathf.CeilToInt(Mathf.Max(minVoxel.y, maxVoxel.y));
            int maxZ = Mathf.CeilToInt(Mathf.Max(minVoxel.z, maxVoxel.z));
            minY = Mathf.Max(0, minY);
            maxY = carveLandingShaftToWorldTop
                ? VoxelColumnChunkData.Height - 1
                : Mathf.Min(VoxelColumnChunkData.Height - 1, maxY);
            Vector3 structureUp = transform.up;
            Vector3 structureRight = transform.right;
            Vector3 structureForward = transform.forward;
            Vector3 structureFloor = transform.position;
            const float isoLevel = 0f;
            int clearedSamples = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector3 sampleWorldPosition = terrainTransform.TransformPoint(
                            new Vector3(x, y, z) * voxelSize);
                        Vector3 passageOffset =
                            sampleWorldPosition - passageStart;
                        float passageForwardDistance =
                            Vector3.Dot(passageOffset, passageForwardAxis);
                        float passageRightDistance =
                            Vector3.Dot(passageOffset, passageRightAxis);
                        float passageProgress = passageLength > Mathf.Epsilon
                            ? Mathf.Clamp01(
                                passageForwardDistance / passageLength)
                            : 0f;
                        Vector3 passageFloor = Vector3.Lerp(
                            passageStart,
                            passageEnd,
                            passageProgress);
                        float passageUpDistance = Vector3.Dot(
                            sampleWorldPosition - passageFloor,
                            passageUpAxis);
                        float heightAboveStructureFloor = Vector3.Dot(
                            sampleWorldPosition - structureFloor,
                            structureUp);
                        bool aboveStructureFloor =
                            heightAboveStructureFloor >= 0f;
                        bool aboveStructureFloorTransition =
                            heightAboveStructureFloor > 0.0001f;
                        Vector3 structureOffset =
                            sampleWorldPosition - structureFloor;
                        float shaftRightDistance = Vector3.Dot(
                            structureOffset,
                            structureRight);
                        float shaftForwardDistance = Vector3.Dot(
                            structureOffset,
                            structureForward);
                        float rawClearanceDistance =
                            DistanceOutsideBounds(
                                sampleWorldPosition,
                                clearanceBounds);
                        float clearanceDistance =
                            aboveStructureFloor
                                && rawClearanceDistance <= 0f
                            ? 0f
                            : aboveStructureFloorTransition
                                ? rawClearanceDistance
                                : float.PositiveInfinity;
                        float rawPassageDistance = DistanceOutsideBox(
                            passageForwardDistance,
                            0f,
                            passageLength,
                            passageRightDistance,
                            -worldPassageWidth * 0.5f,
                            worldPassageWidth * 0.5f,
                            passageUpDistance,
                            0f,
                            worldPassageHeight);
                        float passageDistance =
                            passageUpDistance >= 0f
                                && rawPassageDistance <= 0f
                            ? 0f
                            : passageUpDistance > 0.0001f
                                ? rawPassageDistance
                                : float.PositiveInfinity;
                        float rawShaftDistance =
                            DistanceOutsideRectangle(
                                shaftRightDistance,
                                shaftMinimumRight,
                                shaftMaximumRight,
                                shaftForwardDistance,
                                shaftMinimumForward,
                                shaftMaximumForward);
                        float shaftDistance =
                            carveLandingShaftToWorldTop
                                && aboveStructureFloor
                                && rawShaftDistance <= 0f
                            ? 0f
                            : carveLandingShaftToWorldTop
                                && aboveStructureFloorTransition
                                ? rawShaftDistance
                                : float.PositiveInfinity;
                        float blendWeight = Mathf.Max(
                            EvaluateTransitionWeight(
                                clearanceDistance,
                                worldTransitionWidth),
                            Mathf.Max(
                                EvaluateTransitionWeight(
                                    passageDistance,
                                    worldTransitionWidth),
                                EvaluateTransitionWeight(
                                    shaftDistance,
                                    worldTransitionWidth)));
                        if (blendWeight <= 0f
                            || !world.TryGetSample(
                                x,
                                y,
                                z,
                                out VoxelSample sample)
                            || sample.Density < isoLevel)
                        {
                            continue;
                        }

                        float blendedDensity = Mathf.Lerp(
                            sample.Density,
                            airDensity,
                            blendWeight);
                        VoxelTypeId blendedType =
                            blendedDensity < isoLevel
                                ? VoxelTypeId.Air
                                : sample.Type;
                        if (Mathf.Approximately(
                                blendedDensity,
                                sample.Density)
                            && blendedType == sample.Type)
                        {
                            continue;
                        }
                        world.SetVoxel(
                            x,
                            y,
                            z,
                            blendedDensity,
                            blendedType);
                        clearedSamples++;
                    }
                }
            }

            return clearedSamples;
        }

        private void BuildStructureFootprint(
            Renderer[] renderers,
            float worldPadding,
            out float minimumRight,
            out float maximumRight,
            out float minimumForward,
            out float maximumForward)
        {
            Vector3 origin = transform.position;
            Vector3 right = transform.right;
            Vector3 forward = transform.forward;
            minimumRight = float.PositiveInfinity;
            maximumRight = float.NegativeInfinity;
            minimumForward = float.PositiveInfinity;
            maximumForward = float.NegativeInfinity;

            foreach (Renderer renderer in renderers)
            {
                Bounds localBounds = renderer.localBounds;
                Vector3 minimum = localBounds.min;
                Vector3 maximum = localBounds.max;
                for (int xIndex = 0; xIndex <= 1; xIndex++)
                {
                    for (int yIndex = 0; yIndex <= 1; yIndex++)
                    {
                        for (int zIndex = 0; zIndex <= 1; zIndex++)
                        {
                            var localCorner = new Vector3(
                                xIndex == 0 ? minimum.x : maximum.x,
                                yIndex == 0 ? minimum.y : maximum.y,
                                zIndex == 0 ? minimum.z : maximum.z);
                            Vector3 corner = renderer.transform.TransformPoint(
                                localCorner);
                            Vector3 offset = corner - origin;
                            float rightDistance = Vector3.Dot(offset, right);
                            float forwardDistance = Vector3.Dot(offset, forward);
                            minimumRight = Mathf.Min(
                                minimumRight,
                                rightDistance);
                            maximumRight = Mathf.Max(
                                maximumRight,
                                rightDistance);
                            minimumForward = Mathf.Min(
                                minimumForward,
                                forwardDistance);
                            maximumForward = Mathf.Max(
                                maximumForward,
                                forwardDistance);
                        }
                    }
                }
            }

            minimumRight -= worldPadding;
            maximumRight += worldPadding;
            minimumForward -= worldPadding;
            maximumForward += worldPadding;
        }

        private static Bounds BuildLandingGroundBounds(
            Vector3 origin,
            Vector3 right,
            Vector3 forward,
            Vector3 up,
            float minimumRight,
            float maximumRight,
            float minimumForward,
            float maximumForward,
            float thickness,
            float headroom)
        {
            var bounds = new Bounds(origin, Vector3.zero);
            for (int rightIndex = 0; rightIndex <= 1; rightIndex++)
            {
                float rightDistance = rightIndex == 0
                    ? minimumRight
                    : maximumRight;
                for (int forwardIndex = 0;
                    forwardIndex <= 1;
                    forwardIndex++)
                {
                    float forwardDistance = forwardIndex == 0
                        ? minimumForward
                        : maximumForward;
                    Vector3 horizontalOffset =
                        right * rightDistance + forward * forwardDistance;
                    bounds.Encapsulate(
                        origin + horizontalOffset - up * thickness);
                    bounds.Encapsulate(
                        origin + horizontalOffset + up * headroom);
                }
            }

            return bounds;
        }

        private static float DistanceOutsideInterval(
            float value,
            float minimum,
            float maximum)
        {
            if (value < minimum)
            {
                return minimum - value;
            }
            if (value > maximum)
            {
                return value - maximum;
            }
            return 0f;
        }

        private static float DistanceOutsideRectangle(
            float firstValue,
            float firstMinimum,
            float firstMaximum,
            float secondValue,
            float secondMinimum,
            float secondMaximum)
        {
            float firstDistance = DistanceOutsideInterval(
                firstValue,
                firstMinimum,
                firstMaximum);
            float secondDistance = DistanceOutsideInterval(
                secondValue,
                secondMinimum,
                secondMaximum);
            return Mathf.Sqrt(
                firstDistance * firstDistance
                + secondDistance * secondDistance);
        }

        private static float DistanceOutsideBox(
            float firstValue,
            float firstMinimum,
            float firstMaximum,
            float secondValue,
            float secondMinimum,
            float secondMaximum,
            float thirdValue,
            float thirdMinimum,
            float thirdMaximum)
        {
            float firstDistance = DistanceOutsideInterval(
                firstValue,
                firstMinimum,
                firstMaximum);
            float secondDistance = DistanceOutsideInterval(
                secondValue,
                secondMinimum,
                secondMaximum);
            float thirdDistance = DistanceOutsideInterval(
                thirdValue,
                thirdMinimum,
                thirdMaximum);
            return Mathf.Sqrt(
                firstDistance * firstDistance
                + secondDistance * secondDistance
                + thirdDistance * thirdDistance);
        }

        private static float DistanceOutsideBounds(
            Vector3 point,
            Bounds bounds)
        {
            return DistanceOutsideBox(
                point.x,
                bounds.min.x,
                bounds.max.x,
                point.y,
                bounds.min.y,
                bounds.max.y,
                point.z,
                bounds.min.z,
                bounds.max.z);
        }

        private static float EvaluateTransitionWeight(
            float distanceOutsideCore,
            float transitionWidth)
        {
            if (distanceOutsideCore <= 0f)
            {
                return 1f;
            }
            if (transitionWidth <= Mathf.Epsilon
                || distanceOutsideCore >= transitionWidth)
            {
                return 0f;
            }

            float normalized = 1f
                - distanceOutsideCore / transitionWidth;
            return normalized * normalized * (3f - 2f * normalized);
        }

        private static VoxelTypeId ResolveBlendedSolidType(
            VoxelSample original,
            float blendedDensity,
            float isoLevel,
            VoxelTypeId targetSolidType,
            float blendWeight)
        {
            if (blendedDensity < isoLevel)
            {
                return VoxelTypeId.Air;
            }
            if (original.Density < isoLevel || blendWeight >= 0.5f)
            {
                return targetSolidType;
            }
            return original.Type;
        }

        private void BuildExitPassage(
            Transform spawnPoint,
            out Vector3 start,
            out Vector3 end,
            out Vector3 forward,
            out Vector3 right,
            out Vector3 up,
            out float length)
        {
            start = spawnPoint.position;
            up = spawnPoint.up;
            Vector3 requestedEnd = hasExitTarget
                ? exitTargetWorldPosition
                : start
                    + spawnPoint.forward * GetWorldLength(exitPassageLength);
            Vector3 horizontalOffset = Vector3.ProjectOnPlane(
                requestedEnd - start,
                up);
            if (horizontalOffset.sqrMagnitude <= 0.0001f)
            {
                horizontalOffset = spawnPoint.forward
                    * GetWorldLength(exitPassageLength);
                requestedEnd = start + horizontalOffset;
            }

            length = horizontalOffset.magnitude;
            forward = horizontalOffset / length;
            right = Vector3.Cross(up, forward).normalized;
            end = requestedEnd;
        }

        private Bounds BuildExitPassageBounds(
            Vector3 start,
            Vector3 end,
            Vector3 right,
            Vector3 up,
            float worldPassageWidth,
            float worldPassageHeight)
        {
            var bounds = new Bounds(start, Vector3.zero);
            Vector3 halfRight = right * (worldPassageWidth * 0.5f);
            Vector3 passageUp = up * worldPassageHeight;
            for (int rightSign = -1; rightSign <= 1; rightSign += 2)
            {
                Vector3 lateralOffset = halfRight * rightSign;
                bounds.Encapsulate(start + lateralOffset);
                bounds.Encapsulate(start + lateralOffset + passageUp);
                bounds.Encapsulate(end + lateralOffset);
                bounds.Encapsulate(end + lateralOffset + passageUp);
            }

            return bounds;
        }

        private float GetWorldLength(float authoredLength)
        {
            float worldScale = (
                transform.TransformVector(Vector3.right).magnitude
                + transform.TransformVector(Vector3.up).magnitude
                + transform.TransformVector(Vector3.forward).magnitude)
                / 3f;
            return authoredLength * Mathf.Max(0.0001f, worldScale);
        }

        private Transform ResolvePlayerSpawnPoint()
        {
            if (playerSpawnPoint == null
                || (playerSpawnPoint != transform
                    && !playerSpawnPoint.IsChildOf(transform)))
            {
                return transform;
            }

            return playerSpawnPoint;
        }
    }
}
