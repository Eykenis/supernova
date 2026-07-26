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
        [SerializeField, Min(0f)] private float terrainClearancePadding = 0.75f;
        [SerializeField, Min(1f)] private float exitPassageLength = 12f;
        [SerializeField, Min(1f)] private float exitPassageWidth = 6f;
        [SerializeField, Min(1f)] private float exitPassageHeight = 4.5f;

        private bool hasExitTarget;
        private Vector3 exitTargetWorldPosition;

        public Transform PlayerSpawnPoint => ResolvePlayerSpawnPoint();
        public bool HasExitTarget => hasExitTarget;
        public Vector3 ExitTargetWorldPosition => exitTargetWorldPosition;

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
            clearanceBounds.Expand(worldPadding * 2f);
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
            Vector3 structureUp = transform.up;
            Vector3 structureFloor = transform.position;
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
                        bool insideExitPassage =
                            passageForwardDistance >= 0f
                            && passageForwardDistance <= passageLength
                            && Mathf.Abs(passageRightDistance)
                                <= worldPassageWidth * 0.5f
                            && passageUpDistance >= 0f
                            && passageUpDistance <= worldPassageHeight;
                        bool aboveStructureFloor = Vector3.Dot(
                                sampleWorldPosition - structureFloor,
                                structureUp) >= 0f;
                        if ((!clearanceBounds.Contains(sampleWorldPosition)
                                || !aboveStructureFloor)
                            && !insideExitPassage
                            || !world.TryGetDensity(x, y, z, out float density)
                            || density < 0f)
                        {
                            continue;
                        }

                        world.SetVoxel(
                            x,
                            y,
                            z,
                            airDensity,
                            VoxelTypeId.Air);
                        clearedSamples++;
                    }
                }
            }

            return clearedSamples;
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
