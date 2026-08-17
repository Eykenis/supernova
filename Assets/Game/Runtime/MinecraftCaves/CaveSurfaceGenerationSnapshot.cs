using System;
using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Immutable main-thread capture of biome and brush authoring data. Worker
    /// tasks use this snapshot without touching live ScriptableObjects.
    /// </summary>
    internal sealed class CaveSurfaceGenerationSnapshot
    {
        private readonly CaveBiomeSelectionSnapshot[] selections;
        private readonly CaveBiomeRuntimeSnapshot fallbackBiome;

        private CaveSurfaceGenerationSnapshot(
            float noiseFrequency,
            int seedSalt,
            CaveBiomeSelectionSnapshot[] selections,
            CaveBiomeRuntimeSnapshot fallbackBiome,
            bool hasBrushes)
        {
            NoiseFrequency = noiseFrequency;
            SeedSalt = seedSalt;
            this.selections = selections;
            this.fallbackBiome = fallbackBiome;
            HasBrushes = hasBrushes;
        }

        public float NoiseFrequency { get; }
        public int SeedSalt { get; }
        public bool HasBrushes { get; }

        public static CaveSurfaceGenerationSnapshot Capture(
            CaveBiomeCatalog catalog)
        {
            if (catalog == null)
            {
                return null;
            }

            var biomes = new Dictionary<
                CaveBiomeDefinition,
                CaveBiomeRuntimeSnapshot>();
            CaveBiomeRuntimeSnapshot fallback = CaptureBiome(
                catalog.FallbackBiome,
                biomes);
            IReadOnlyList<CaveBiomeSelection> sourceSelections =
                catalog.Selections;
            var capturedSelections =
                new CaveBiomeSelectionSnapshot[sourceSelections != null
                    ? sourceSelections.Count
                    : 0];
            bool hasBrushes = fallback != null && fallback.Brushes.Length > 0;
            for (int i = 0; i < capturedSelections.Length; i++)
            {
                CaveBiomeSelection source = sourceSelections[i];
                CaveBiomeRuntimeSnapshot biome = source != null
                    ? CaptureBiome(source.Biome, biomes)
                    : null;
                capturedSelections[i] = new CaveBiomeSelectionSnapshot(
                    biome,
                    source != null ? source.MinimumNoise : 0f,
                    source != null ? source.MaximumNoise : 0f);
                hasBrushes |= biome != null && biome.Brushes.Length > 0;
            }

            return new CaveSurfaceGenerationSnapshot(
                catalog.NoiseFrequency,
                catalog.SeedSalt,
                capturedSelections,
                fallback,
                hasBrushes);
        }

        public CaveBiomeRuntimeSnapshot Evaluate(
            Vector3 worldVoxelPosition,
            int worldSeed)
        {
            return EvaluateSurface(
                worldVoxelPosition,
                worldSeed,
                out _);
        }

        public CaveBiomeRuntimeSnapshot EvaluateSurface(
            Vector3 worldVoxelPosition,
            int worldSeed,
            out float interiorCoverage)
        {
            float noise = MinecraftCaveNoise.NormalNoise(
                worldVoxelPosition * NoiseFrequency,
                worldSeed ^ SeedSalt,
                2);
            for (int i = 0; i < selections.Length; i++)
            {
                CaveBiomeSelectionSnapshot selection = selections[i];
                if (!selection.Contains(noise))
                {
                    continue;
                }

                interiorCoverage = selection.EvaluateInteriorCoverage(noise);
                return selection.Biome;
            }

            interiorCoverage = 1f;
            return fallbackBiome;
        }

        private static CaveBiomeRuntimeSnapshot CaptureBiome(
            CaveBiomeDefinition definition,
            Dictionary<CaveBiomeDefinition, CaveBiomeRuntimeSnapshot> captured)
        {
            if (definition == null)
            {
                return null;
            }
            if (captured.TryGetValue(
                definition,
                out CaveBiomeRuntimeSnapshot existing))
            {
                return existing;
            }

            IReadOnlyList<CaveSurfaceBrushDefinition> sourceBrushes =
                definition.SurfaceBrushes;
            var brushes = new List<CaveSurfaceBrushRuntimeSnapshot>(
                sourceBrushes != null ? sourceBrushes.Count : 0);
            if (sourceBrushes != null)
            {
                for (int i = 0; i < sourceBrushes.Count; i++)
                {
                    CaveSurfaceBrushDefinition brush = sourceBrushes[i];
                    if (brush == null || !brush.HasRenderableContent)
                    {
                        continue;
                    }
                    brushes.Add(
                        CaveSurfaceBrushRuntimeSnapshot.Capture(brush));
                }
            }

            var result = new CaveBiomeRuntimeSnapshot(
                definition,
                definition.TerrainSurfaceColor,
                definition.TerrainSurfaceEdgeFade,
                definition.TerrainSurfaceOffset,
                brushes.ToArray());
            captured.Add(definition, result);
            return result;
        }
    }

    internal sealed class CaveBiomeRuntimeSnapshot
    {
        public CaveBiomeRuntimeSnapshot(
            CaveBiomeDefinition definition,
            Color terrainSurfaceColor,
            float terrainSurfaceEdgeFade,
            float terrainSurfaceOffset,
            CaveSurfaceBrushRuntimeSnapshot[] brushes)
        {
            Definition = definition;
            TerrainSurfaceColor = terrainSurfaceColor;
            TerrainSurfaceEdgeFade = terrainSurfaceEdgeFade;
            TerrainSurfaceOffset = terrainSurfaceOffset;
            Brushes = brushes ?? Array.Empty<CaveSurfaceBrushRuntimeSnapshot>();
        }

        public CaveBiomeDefinition Definition { get; }
        public Color TerrainSurfaceColor { get; }
        public float TerrainSurfaceEdgeFade { get; }
        public float TerrainSurfaceOffset { get; }
        public CaveSurfaceBrushRuntimeSnapshot[] Brushes { get; }
    }

    internal sealed class CaveSurfaceBrushRuntimeSnapshot
    {
        private readonly VoxelTypeId[] attachableTypes;

        private CaveSurfaceBrushRuntimeSnapshot(
            CaveSurfaceBrushDefinition definition,
            VoxelTypeId[] attachableTypes,
            CaveSurfaceOrientation orientation,
            int seedSalt,
            float densityPerSquareUnit,
            float minimumVerticalAlignment,
            float maximumWallVerticalAlignment,
            float normalOffset,
            float uprightBias,
            Vector2 tangentScaleRange,
            Vector2 normalScaleRange,
            float clumpHorizontalCellSize,
            float clumpVerticalCellSize,
            Vector2 clumpHeightRange,
            Vector2 clumpWidthRange,
            float clumpYawBiasDegrees)
        {
            Definition = definition;
            this.attachableTypes = attachableTypes;
            Orientation = orientation;
            SeedSalt = seedSalt;
            DensityPerSquareUnit = densityPerSquareUnit;
            MinimumVerticalAlignment = minimumVerticalAlignment;
            MaximumWallVerticalAlignment = maximumWallVerticalAlignment;
            NormalOffset = normalOffset;
            UprightBias = uprightBias;
            TangentScaleRange = tangentScaleRange;
            NormalScaleRange = normalScaleRange;
            ClumpHorizontalCellSize = clumpHorizontalCellSize;
            ClumpVerticalCellSize = clumpVerticalCellSize;
            ClumpHeightRange = clumpHeightRange;
            ClumpWidthRange = clumpWidthRange;
            ClumpYawBiasDegrees = clumpYawBiasDegrees;
        }

        public CaveSurfaceBrushDefinition Definition { get; }
        public CaveSurfaceOrientation Orientation { get; }
        public int SeedSalt { get; }
        public float DensityPerSquareUnit { get; }
        public float MinimumVerticalAlignment { get; }
        public float MaximumWallVerticalAlignment { get; }
        public float NormalOffset { get; }
        public float UprightBias { get; }
        public Vector2 TangentScaleRange { get; }
        public Vector2 NormalScaleRange { get; }
        public float ClumpHorizontalCellSize { get; }
        public float ClumpVerticalCellSize { get; }
        public Vector2 ClumpHeightRange { get; }
        public Vector2 ClumpWidthRange { get; }
        public float ClumpYawBiasDegrees { get; }

        public static CaveSurfaceBrushRuntimeSnapshot Capture(
            CaveSurfaceBrushDefinition definition)
        {
            IReadOnlyList<VoxelTypeDefinition> sourceTypes =
                definition.AttachableVoxelTypes;
            var types = new List<VoxelTypeId>(
                sourceTypes != null ? sourceTypes.Count : 0);
            if (sourceTypes != null)
            {
                for (int i = 0; i < sourceTypes.Count; i++)
                {
                    VoxelTypeDefinition type = sourceTypes[i];
                    if (type != null)
                    {
                        types.Add(type.TypeId);
                    }
                }
            }

            return new CaveSurfaceBrushRuntimeSnapshot(
                definition,
                types.ToArray(),
                definition.Orientation,
                definition.SeedSalt,
                definition.DensityPerSquareUnit,
                definition.MinimumVerticalAlignment,
                definition.MaximumWallVerticalAlignment,
                definition.NormalOffset,
                definition.UprightBias,
                definition.TangentScaleRange,
                definition.NormalScaleRange,
                definition.ClumpHorizontalCellSize,
                definition.ClumpVerticalCellSize,
                definition.ClumpHeightRange,
                definition.ClumpWidthRange,
                definition.ClumpYawBiasDegrees);
        }

        public bool CanAttachTo(VoxelTypeId type)
        {
            if (type.IsAir)
            {
                return false;
            }
            for (int i = 0; i < attachableTypes.Length; i++)
            {
                if (attachableTypes[i] == type)
                {
                    return true;
                }
            }
            return false;
        }

        public bool MatchesOrientation(Vector3 outwardNormal)
        {
            float upDot = outwardNormal.normalized.y;
            switch (Orientation)
            {
                case CaveSurfaceOrientation.Upward:
                    return upDot >= MinimumVerticalAlignment;
                case CaveSurfaceOrientation.Downward:
                    return upDot <= -MinimumVerticalAlignment;
                case CaveSurfaceOrientation.Wall:
                    return Mathf.Abs(upDot) <= MaximumWallVerticalAlignment;
                default:
                    return true;
            }
        }
    }

    internal readonly struct CaveBiomeSelectionSnapshot
    {
        public CaveBiomeSelectionSnapshot(
            CaveBiomeRuntimeSnapshot biome,
            float minimumNoise,
            float maximumNoise)
        {
            Biome = biome;
            MinimumNoise = Mathf.Min(minimumNoise, maximumNoise);
            MaximumNoise = Mathf.Max(minimumNoise, maximumNoise);
        }

        public CaveBiomeRuntimeSnapshot Biome { get; }
        public float MinimumNoise { get; }
        public float MaximumNoise { get; }

        public bool Contains(float noise)
        {
            return Biome != null
                && noise >= MinimumNoise
                && noise <= MaximumNoise;
        }

        public float EvaluateInteriorCoverage(float noise)
        {
            if (!Contains(noise))
            {
                return 0f;
            }

            float width = Mathf.Max(0f, Biome.TerrainSurfaceEdgeFade);
            if (width <= Mathf.Epsilon)
            {
                return 1f;
            }

            float boundaryDistance = float.PositiveInfinity;
            if (MinimumNoise > -1f)
            {
                boundaryDistance = noise - MinimumNoise;
            }
            if (MaximumNoise < 1f)
            {
                boundaryDistance = Mathf.Min(
                    boundaryDistance,
                    MaximumNoise - noise);
            }
            if (float.IsPositiveInfinity(boundaryDistance))
            {
                return 1f;
            }

            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(boundaryDistance / width));
        }
    }
}
