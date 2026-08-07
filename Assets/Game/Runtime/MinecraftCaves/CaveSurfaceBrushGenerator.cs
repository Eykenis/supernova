using System;
using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    public readonly struct CaveSurfacePlacement
    {
        public CaveSurfacePlacement(
            CaveSurfaceBrushDefinition brush,
            CaveBiomeDefinition biome,
            Vector3 localPosition,
            Vector3 outwardNormal,
            Vector3 scale,
            float yaw,
            Vector3Int anchorVoxel)
            : this(
                brush,
                biome,
                localPosition,
                outwardNormal,
                outwardNormal,
                scale,
                yaw,
                anchorVoxel)
        {
        }

        public CaveSurfacePlacement(
            CaveSurfaceBrushDefinition brush,
            CaveBiomeDefinition biome,
            Vector3 localPosition,
            Vector3 outwardNormal,
            Vector3 stanceNormal,
            Vector3 scale,
            float yaw,
            Vector3Int anchorVoxel)
        {
            Brush = brush;
            Biome = biome;
            LocalPosition = localPosition;
            OutwardNormal = outwardNormal;
            StanceNormal = stanceNormal.sqrMagnitude > Mathf.Epsilon
                ? stanceNormal.normalized
                : outwardNormal;
            Scale = scale;
            Yaw = yaw;
            AnchorVoxel = anchorVoxel;
        }

        public CaveSurfaceBrushDefinition Brush { get; }
        public CaveBiomeDefinition Biome { get; }
        public Vector3 LocalPosition { get; }

        /// <summary>
        /// The surface normal pointing away from solid voxels. Used for orientation
        /// gating and for offsetting the placement off the surface.
        /// </summary>
        public Vector3 OutwardNormal { get; }

        /// <summary>
        /// The axis the instance is stood up along, being
        /// <see cref="OutwardNormal"/> blended toward world up by the brush's
        /// upright bias. It is baked into the instance matrix, so the blade shader
        /// recovers it as the instance's local up and shades with it, which is what
        /// keeps a patch from shimmering over curved terrain.
        /// </summary>
        public Vector3 StanceNormal { get; }

        public Vector3 Scale { get; }
        public float Yaw { get; }
        public Vector3Int AnchorVoxel { get; }
        public Quaternion LocalRotation =>
            Quaternion.FromToRotation(Vector3.up, StanceNormal)
            * Quaternion.AngleAxis(Yaw, Vector3.up);
    }

    /// <summary>
    /// Converts exposed terrain triangles into deterministic biome brush placements.
    /// It only emits a placement when a solid voxel of the rendered type and an air
    /// sample can both be resolved around the triangle, excluding solid-type seams.
    /// </summary>
    public static class CaveSurfaceBrushGenerator
    {
        private const int BiomeSampleCellSize = 4;
        private const double Inverse53BitRange =
            1.0 / 9007199254740992.0;

        public static List<CaveSurfacePlacement> Generate(
            VoxelMeshData meshData,
            InfiniteVoxelWorld world,
            Vector3Int meshSection,
            int sectionStartY,
            float voxelSize,
            float isoLevel,
            int worldSeed,
            CaveBiomeCatalog biomeCatalog,
            ISet<Vector3Int> carvedVoxels = null)
        {
            var placements = new List<CaveSurfacePlacement>();
            if (meshData == null
                || world == null
                || biomeCatalog == null
                || voxelSize <= 0f
                || meshData.TriangleCount == 0)
            {
                return placements;
            }

            var sectionVoxelOrigin = new Vector3(
                meshSection.x * VoxelColumnChunkData.Width,
                sectionStartY,
                meshSection.z * VoxelColumnChunkData.Depth);
            var biomesBySampleCell =
                new Dictionary<Vector3Int, CaveBiomeDefinition>();
            IReadOnlyList<VoxelTypeId> surfaceTypes = meshData.SubmeshTypes;
            for (int typeIndex = 0; typeIndex < surfaceTypes.Count; typeIndex++)
            {
                VoxelTypeId surfaceType = surfaceTypes[typeIndex];
                IReadOnlyList<int> triangles = meshData.GetTriangles(surfaceType);
                for (int triangle = 0; triangle + 2 < triangles.Count; triangle += 3)
                {
                    Vector3 first = meshData.Vertices[triangles[triangle]];
                    Vector3 second = meshData.Vertices[triangles[triangle + 1]];
                    Vector3 third = meshData.Vertices[triangles[triangle + 2]];
                    Vector3 cross = Vector3.Cross(second - first, third - first);
                    float doubledArea = cross.magnitude;
                    if (doubledArea <= Mathf.Epsilon)
                    {
                        continue;
                    }

                    Vector3 centroid = (first + second + third) / 3f;
                    Vector3 centroidVoxel = sectionVoxelOrigin
                        + centroid / voxelSize;
                    if (CaveSurfaceDisturbance.IsNearCarvedVoxel(
                        centroidVoxel,
                        carvedVoxels))
                    {
                        continue;
                    }
                    Vector3Int biomeSampleCell = GetBiomeSampleCell(
                        centroidVoxel);
                    if (!biomesBySampleCell.TryGetValue(
                        biomeSampleCell,
                        out CaveBiomeDefinition biome))
                    {
                        Vector3 biomeSamplePosition =
                            (Vector3)biomeSampleCell * BiomeSampleCellSize
                            + Vector3.one * (BiomeSampleCellSize * 0.5f);
                        biome = biomeCatalog.Evaluate(
                            biomeSamplePosition,
                            worldSeed);
                        biomesBySampleCell.Add(biomeSampleCell, biome);
                    }
                    if (biome == null || biome.SurfaceBrushes.Count == 0)
                    {
                        continue;
                    }

                    if (!TryResolveAttachment(
                        world,
                        sectionVoxelOrigin,
                        centroid,
                        cross / doubledArea,
                        surfaceType,
                        voxelSize,
                        isoLevel,
                        out Vector3 outwardNormal,
                        out _))
                    {
                        continue;
                    }

                    float area = doubledArea * 0.5f;
                    for (int brushIndex = 0;
                        brushIndex < biome.SurfaceBrushes.Count;
                        brushIndex++)
                    {
                        CaveSurfaceBrushDefinition brush =
                            biome.SurfaceBrushes[brushIndex];
                        if (brush == null
                            || !brush.HasRenderableContent
                            || !brush.CanAttachTo(surfaceType)
                            || !brush.MatchesOrientation(outwardNormal))
                        {
                            continue;
                        }

                        double expected = brush.DensityPerSquareUnit * area;
                        int guaranteed = Mathf.FloorToInt((float)expected);
                        ulong seed = BuildSeed(
                            worldSeed,
                            brush.SeedSalt,
                            meshSection,
                            surfaceType,
                            triangle / 3);
                        var random = new DeterministicRandom(seed);
                        int count = guaranteed;
                        if (random.NextDouble() < expected - guaranteed)
                        {
                            count++;
                        }

                        for (int instance = 0; instance < count; instance++)
                        {
                            Vector3 position = SampleTriangle(
                                first,
                                second,
                                third,
                                ref random,
                                out Vector3 barycentric);
                            Vector3 placementVoxel = sectionVoxelOrigin
                                + position / voxelSize;
                            if (CaveSurfaceDisturbance.IsNearCarvedVoxel(
                                placementVoxel,
                                carvedVoxels))
                            {
                                continue;
                            }
                            if (!TryResolveAttachment(
                                world,
                                sectionVoxelOrigin,
                                position,
                                outwardNormal,
                                surfaceType,
                                voxelSize,
                                isoLevel,
                                out Vector3 resolvedNormal,
                                out Vector3Int anchorVoxel))
                            {
                                continue;
                            }

                            Vector2 tangentRange = brush.TangentScaleRange;
                            Vector2 normalRange = brush.NormalScaleRange;
                            float tangentScale = Mathf.Lerp(
                                tangentRange.x,
                                tangentRange.y,
                                (float)random.NextDouble());
                            float normalScale = Mathf.Lerp(
                                normalRange.x,
                                normalRange.y,
                                (float)random.NextDouble());
                            float yaw = (float)random.NextDouble() * 360f;

                            // Everything below draws no further random values, so
                            // the stream stays byte-identical to the pre-clumping
                            // generator and existing placements do not move.
                            Vector3 shadingNormal = InterpolateNormal(
                                meshData,
                                triangles,
                                triangle,
                                barycentric,
                                resolvedNormal);

                            // Blend the stance toward the vertical. Grass fully
                            // aligned to a marching-cubes normal lies over at up to
                            // ~53 degrees on the shallowest accepted slope, which
                            // reads as flattened rather than growing. Ceiling
                            // brushes bias toward down so vines still hang.
                            Vector3 vertical = shadingNormal.y < 0f
                                ? Vector3.down
                                : Vector3.up;
                            Vector3 stanceNormal = Vector3.Lerp(
                                shadingNormal,
                                vertical,
                                brush.UprightBias);
                            if (stanceNormal.sqrMagnitude <= Mathf.Epsilon)
                            {
                                stanceNormal = shadingNormal;
                            }

                            CaveSurfaceClumpAttributes clump =
                                CaveSurfaceClumpField.Sample(
                                    placementVoxel,
                                    brush.ClumpHorizontalCellSize,
                                    brush.ClumpVerticalCellSize,
                                    brush.ClumpHeightRange,
                                    brush.ClumpWidthRange,
                                    brush.ClumpYawBiasDegrees,
                                    worldSeed,
                                    brush.SeedSalt);

                            placements.Add(new CaveSurfacePlacement(
                                brush,
                                biome,
                                position + resolvedNormal * brush.NormalOffset,
                                resolvedNormal,
                                stanceNormal,
                                new Vector3(
                                    tangentScale * clump.WidthMultiplier,
                                    normalScale * clump.HeightMultiplier,
                                    tangentScale * clump.WidthMultiplier),
                                yaw + clump.YawBiasDegrees,
                                anchorVoxel));
                        }
                    }
                }
            }
            return placements;
        }

        private static Vector3Int GetBiomeSampleCell(Vector3 worldVoxelPosition)
        {
            return new Vector3Int(
                Mathf.FloorToInt(worldVoxelPosition.x / BiomeSampleCellSize),
                Mathf.FloorToInt(worldVoxelPosition.y / BiomeSampleCellSize),
                Mathf.FloorToInt(worldVoxelPosition.z / BiomeSampleCellSize));
        }

        private static bool TryResolveAttachment(
            InfiniteVoxelWorld world,
            Vector3 sectionVoxelOrigin,
            Vector3 localSurfacePosition,
            Vector3 faceNormal,
            VoxelTypeId surfaceType,
            float voxelSize,
            float isoLevel,
            out Vector3 outwardNormal,
            out Vector3Int anchorVoxel)
        {
            outwardNormal = faceNormal.normalized;
            anchorVoxel = default;
            if (outwardNormal.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            Vector3 surfaceVoxelPosition = sectionVoxelOrigin
                + localSurfacePosition / voxelSize;
            Vector3Int centre = new Vector3Int(
                Mathf.RoundToInt(surfaceVoxelPosition.x),
                Mathf.RoundToInt(surfaceVoxelPosition.y),
                Mathf.RoundToInt(surfaceVoxelPosition.z));
            bool foundSolid = false;
            bool foundAir = false;
            float closestSolidDistance = float.PositiveInfinity;
            float closestAirDistance = float.PositiveInfinity;
            Vector3 closestSolid = default;
            Vector3 closestAir = default;

            for (int z = -1; z <= 1; z++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        var coordinate = new Vector3Int(
                            centre.x + x,
                            centre.y + y,
                            centre.z + z);
                        if (!world.TryGetSample(
                            coordinate.x,
                            coordinate.y,
                            coordinate.z,
                            out VoxelSample sample))
                        {
                            continue;
                        }

                        float distance = ((Vector3)coordinate
                            - surfaceVoxelPosition).sqrMagnitude;
                        if (sample.IsSolid(isoLevel)
                            && sample.Type == surfaceType
                            && distance < closestSolidDistance)
                        {
                            foundSolid = true;
                            closestSolidDistance = distance;
                            closestSolid = coordinate;
                            anchorVoxel = coordinate;
                        }
                        else if (!sample.IsSolid(isoLevel)
                            && distance < closestAirDistance)
                        {
                            foundAir = true;
                            closestAirDistance = distance;
                            closestAir = coordinate;
                        }
                    }
                }
            }

            if (!foundSolid || !foundAir)
            {
                return false;
            }

            Vector3 solidToAir = closestAir - closestSolid;
            if (Vector3.Dot(outwardNormal, solidToAir) < 0f)
            {
                outwardNormal = -outwardNormal;
            }
            return true;
        }

        /// <summary>
        /// Uniformly samples a point on the triangle and reports the barycentric
        /// weights that produced it, so callers can interpolate vertex attributes
        /// at the same point without recovering them.
        /// </summary>
        private static Vector3 SampleTriangle(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            ref DeterministicRandom random,
            out Vector3 barycentric)
        {
            float root = Mathf.Sqrt((float)random.NextDouble());
            float secondWeight = (float)random.NextDouble();
            barycentric = new Vector3(
                1f - root,
                root * (1f - secondWeight),
                root * secondWeight);
            return barycentric.x * first
                + barycentric.y * second
                + barycentric.z * third;
        }

        /// <summary>
        /// Interpolates the smoothed terrain normal at a sample point.
        /// <para>
        /// This is safe because <c>VoxelMeshData.PrepareForUpload</c> has already
        /// run: <c>MinecraftCaveInfiniteWorld</c> calls <c>CreateMesh</c>, which
        /// finalises normals, before it spawns surface content. Falls back to the
        /// resolved face normal if the mesh has no normal data.
        /// </para>
        /// </summary>
        private static Vector3 InterpolateNormal(
            VoxelMeshData meshData,
            IReadOnlyList<int> triangles,
            int triangle,
            Vector3 barycentric,
            Vector3 fallback)
        {
            List<Vector3> normals = meshData.Normals;
            int firstIndex = triangles[triangle];
            int secondIndex = triangles[triangle + 1];
            int thirdIndex = triangles[triangle + 2];
            if (normals == null
                || thirdIndex >= normals.Count
                || secondIndex >= normals.Count
                || firstIndex >= normals.Count)
            {
                return fallback;
            }

            Vector3 interpolated = barycentric.x * normals[firstIndex]
                + barycentric.y * normals[secondIndex]
                + barycentric.z * normals[thirdIndex];
            if (interpolated.sqrMagnitude <= Mathf.Epsilon)
            {
                return fallback;
            }

            interpolated.Normalize();

            // The mesher's winding can disagree with the solid-to-air direction
            // that TryResolveAttachment established; trust the latter.
            return Vector3.Dot(interpolated, fallback) < 0f
                ? -interpolated
                : interpolated;
        }

        private static ulong BuildSeed(
            int worldSeed,
            int seedSalt,
            Vector3Int meshSection,
            VoxelTypeId surfaceType,
            int triangle)
        {
            ulong value = (uint)worldSeed;
            value ^= (ulong)(uint)seedSalt * 0x9E3779B185EBCA87UL;
            value ^= (ulong)(uint)meshSection.x * 0xC2B2AE3D27D4EB4FUL;
            value ^= (ulong)(uint)meshSection.y * 0x165667B19E3779F9UL;
            value ^= (ulong)(uint)meshSection.z * 0x85EBCA77C2B2AE63UL;
            value ^= (ulong)surfaceType.Value * 0x27D4EB2F165667C5UL;
            value ^= (ulong)(uint)triangle * 0x94D049BB133111EBUL;
            return Mix(value);
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        private struct DeterministicRandom
        {
            private ulong state;

            public DeterministicRandom(ulong seed)
            {
                state = seed;
            }

            public double NextDouble()
            {
                return (NextUInt64() >> 11) * Inverse53BitRange;
            }

            private ulong NextUInt64()
            {
                state += 0x9E3779B97F4A7C15UL;
                return Mix(state);
            }
        }
    }
}
