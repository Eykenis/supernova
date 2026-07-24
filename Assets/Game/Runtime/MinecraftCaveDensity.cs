using System;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    public enum MinecraftCaveType
    {
        Cheese,
        Spaghetti,
        Noodle,
        Pillar,
        Combined,
    }

    [Serializable]
    public sealed class MinecraftCaveSettings
    {
        [Header("Cheese")]
        [Min(0.001f)] public float cheeseFrequency = 0.045f;
        [Range(-0.5f, 0.5f)] public float cheeseThreshold = 0.04f;
        [Min(0f)] public float cheeseLayerStrength = 0.28f;

        [Header("Spaghetti")]
        [Min(0.001f)] public float spaghettiFrequency = 0.072f;
        [Range(0.01f, 0.4f)] public float spaghettiThickness = 0.065f;
        [Min(0f)] public float spaghettiWarp = 0.38f;
        [Min(0f)] public float spaghettiRoughness = 0.02f;

        [Header("Noodle")]
        [Min(0.001f)] public float noodleFrequency = 0.125f;
        [Range(0.005f, 0.25f)] public float noodleThickness = 0.04f;
        [Range(-1f, 1f)] public float noodleRarity = 0.12f;

        [Header("Pillar")]
        [Min(0.001f)] public float pillarHorizontalFrequency = 0.105f;
        [Min(0.001f)] public float pillarVerticalFrequency = 0.012f;
        [Range(-1f, 1f)] public float pillarRarity = 0.05f;
        [Range(0.1f, 2f)] public float pillarStrength = 1f;

        [Header("Cave Layout")]
        [Min(0.001f)] public float layoutFrequency = 0.012f;
        [Range(-0.5f, 0.5f)] public float layoutThreshold = 0f;
        [Range(0f, 0.3f)] public float corridorInset = 0.08f;
        [Range(0f, 0.3f)] public float shortcutInset = 0.13f;

        [Header("Display Volume")]
        [Range(0.5f, 0.99f)] public float containerHalfExtent = 0.91f;
        [Range(-0.8f, 0.8f)] public float cutawayPlane = -0.24f;
    }

    /// <summary>
    /// Positive density is solid and negative density is cave/air, matching the
    /// sign convention used by the project's voxel and marching-cubes code.
    /// </summary>
    public sealed class MinecraftCaveDensityField
    {
        private readonly MinecraftCaveSettings settings;

        public MinecraftCaveDensityField(int seed, MinecraftCaveSettings settings)
        {
            Seed = seed;
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public int Seed { get; }

        public float SampleSolidDensity(
            Vector3 worldPosition,
            Vector3 normalizedDisplayPosition,
            MinecraftCaveType type,
            bool cutaway)
        {
            float featureDensity = SampleFeatureDensity(worldPosition, type);
            float containerDensity = BoxInteriorDensity(
                normalizedDisplayPosition,
                settings.containerHalfExtent);
            if (cutaway)
            {
                containerDensity = Mathf.Min(
                    containerDensity,
                    normalizedDisplayPosition.z - settings.cutawayPlane);
            }

            return Mathf.Min(containerDensity, featureDensity);
        }

        public float SampleFeatureDensity(Vector3 worldPosition, MinecraftCaveType type)
        {
            switch (type)
            {
                case MinecraftCaveType.Cheese:
                    return SampleCheese(worldPosition);

                case MinecraftCaveType.Spaghetti:
                    return SampleSpaghetti(worldPosition);

                case MinecraftCaveType.Noodle:
                    return SampleNoodle(worldPosition);

                case MinecraftCaveType.Pillar:
                    return SamplePillarChamber(worldPosition);

                case MinecraftCaveType.Combined:
                    return SampleCombined(worldPosition);

                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public float SampleCheese(Vector3 position)
        {
            Vector3 cheesePoint = position * settings.cheeseFrequency;
            float cheese = MinecraftCaveNoise.NormalNoise(cheesePoint, Seed + 101, 4);
            Vector3 layerPoint = new Vector3(position.x, position.y * 2.4f, position.z)
                * settings.cheeseFrequency * 0.72f;
            float layer = MinecraftCaveNoise.NormalNoise(layerPoint, Seed + 127, 2);
            return cheese
                + settings.cheeseThreshold
                + layer * layer * settings.cheeseLayerStrength;
        }

        public float SampleSpaghetti(Vector3 position)
        {
            Vector3 warpPoint = position * settings.spaghettiFrequency * 0.43f;
            Vector3 warp = new Vector3(
                MinecraftCaveNoise.NormalNoise(warpPoint, Seed + 211, 2),
                MinecraftCaveNoise.NormalNoise(warpPoint, Seed + 223, 2),
                MinecraftCaveNoise.NormalNoise(warpPoint, Seed + 227, 2));
            Vector3 point = position * settings.spaghettiFrequency
                + warp * settings.spaghettiWarp;
            float ridgeA = MinecraftCaveNoise.NormalNoise(point, Seed + 233, 3);
            float ridgeB = MinecraftCaveNoise.NormalNoise(
                point + new Vector3(17.3f, -9.1f, 5.7f),
                Seed + 239,
                3);
            float thicknessNoise = MinecraftCaveNoise.NormalNoise(
                position * 0.031f,
                Seed + 241,
                2);
            float roughness = MinecraftCaveNoise.NormalNoise(
                position * 0.19f,
                Seed + 251,
                2) * settings.spaghettiRoughness;
            float thickness = settings.spaghettiThickness * (1f + thicknessNoise * 0.22f);
            return Mathf.Max(Mathf.Abs(ridgeA), Mathf.Abs(ridgeB)) - thickness + roughness;
        }

        public float SampleNoodle(Vector3 position)
        {
            float activation = MinecraftCaveNoise.NormalNoise(
                position * settings.noodleFrequency * 0.31f,
                Seed + 307,
                2);
            if (activation < settings.noodleRarity)
            {
                return 1f;
            }

            Vector3 point = position * settings.noodleFrequency;
            float ridgeA = MinecraftCaveNoise.NormalNoise(point, Seed + 311, 2);
            float ridgeB = MinecraftCaveNoise.NormalNoise(
                point + new Vector3(-11.7f, 7.9f, 19.3f),
                Seed + 313,
                2);
            float thicknessNoise = MinecraftCaveNoise.NormalNoise(
                position * 0.067f,
                Seed + 317,
                2);
            float thickness = settings.noodleThickness * (1f + thicknessNoise * 0.18f);
            return Mathf.Max(Mathf.Abs(ridgeA), Mathf.Abs(ridgeB)) - thickness;
        }

        public float SamplePillar(Vector3 position)
        {
            Vector3 pillarPoint = new Vector3(
                position.x * settings.pillarHorizontalFrequency,
                position.y * settings.pillarVerticalFrequency,
                position.z * settings.pillarHorizontalFrequency);
            float pillarNoise = MinecraftCaveNoise.NormalNoise(pillarPoint, Seed + 401, 3);
            float rareness = MinecraftCaveNoise.NormalNoise(
                position * 0.027f,
                Seed + 409,
                2);
            float thickness = MinecraftCaveNoise.NormalNoise(
                new Vector3(position.x, position.y * 0.2f, position.z) * 0.052f,
                Seed + 419,
                2);
            float gate = pillarNoise - settings.pillarRarity - rareness * 0.16f;
            float thicknessScale = Mathf.Pow(Mathf.Clamp01(0.62f + thickness * 0.38f), 3f);
            return gate * thicknessScale * settings.pillarStrength;
        }

        private float SamplePillarChamber(Vector3 position)
        {
            Vector3 centre = new Vector3(0f, 0f, 2f);
            Vector3 scaled = position - centre;
            scaled = new Vector3(scaled.x / 12f, scaled.y / 10f, scaled.z / 12f);
            float chamber = scaled.magnitude - 1f;
            return Mathf.Max(chamber, SamplePillar(position));
        }

        private float SampleCombined(Vector3 position)
        {
            float layoutA = MinecraftCaveNoise.NormalNoise(
                position * settings.layoutFrequency,
                Seed + 503,
                3);
            float layoutB = MinecraftCaveNoise.NormalNoise(
                position * settings.layoutFrequency * 1.17f
                    + new Vector3(13.7f, -5.9f, 21.1f),
                Seed + 509,
                3);
            float layoutC = MinecraftCaveNoise.NormalNoise(
                position * settings.layoutFrequency * 1.41f
                    + new Vector3(-17.3f, 9.7f, -6.1f),
                Seed + 521,
                2);

            // Two intersecting low-frequency gates make discrete chamber regions.
            float primaryGate = settings.layoutThreshold - layoutA;
            float roomGate = Mathf.Max(
                primaryGate,
                settings.layoutThreshold - layoutB);
            float rooms = Mathf.Max(SampleCheese(position), roomGate);

            // Main tunnels stay inside a slightly smaller primary region. They can
            // connect nearby chambers without creating a world-spanning pipe network.
            float corridors = Mathf.Max(
                SampleSpaghetti(position),
                primaryGate + settings.corridorInset);

            // Noodles require both a deeply interior primary region and a third
            // independent gate, making them occasional shortcuts rather than clutter.
            float shortcutGate = Mathf.Max(
                primaryGate + settings.shortcutInset,
                settings.layoutThreshold + settings.corridorInset - layoutC);
            float shortcuts = Mathf.Max(SampleNoodle(position), shortcutGate);

            float voids = Mathf.Min(rooms, Mathf.Min(corridors, shortcuts));
            return Mathf.Max(voids, SamplePillar(position));
        }

        private static float BoxInteriorDensity(Vector3 position, float halfExtent)
        {
            Vector3 distance = new Vector3(
                halfExtent - Mathf.Abs(position.x),
                halfExtent - Mathf.Abs(position.y),
                halfExtent - Mathf.Abs(position.z));
            return Mathf.Min(distance.x, Mathf.Min(distance.y, distance.z));
        }
    }
}
