using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Deterministic, world-coordinate 3D gradient noise. NormalNoise combines two
    /// independently seeded octave stacks, matching the role of Minecraft's normal noise.
    /// </summary>
    public static class MinecraftCaveNoise
    {
        private static readonly Vector3[] Gradients =
        {
            new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f),
            new Vector3(1f, -1f, 0f), new Vector3(-1f, -1f, 0f),
            new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f),
            new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, -1f),
            new Vector3(0f, 1f, 1f), new Vector3(0f, -1f, 1f),
            new Vector3(0f, 1f, -1f), new Vector3(0f, -1f, -1f),
        };

        public static float NormalNoise(
            Vector3 position,
            int seed,
            int octaves = 3,
            float lacunarity = 2f,
            float persistence = 0.5f)
        {
            float first = FractalPerlin(position, seed, octaves, lacunarity, persistence);
            Vector3 offset = SeedOffset(seed ^ unchecked((int)0x9E3779B9u));
            float second = FractalPerlin(
                position * 1.0181269f + offset,
                seed ^ unchecked((int)0x85EBCA6Bu),
                octaves,
                lacunarity,
                persistence);
            return Mathf.Clamp((first + second) * 0.55f, -1f, 1f);
        }

        public static float FractalPerlin(
            Vector3 position,
            int seed,
            int octaves,
            float lacunarity,
            float persistence)
        {
            float value = 0f;
            float amplitude = 1f;
            float amplitudeSum = 0f;
            float frequency = 1f;

            for (int octave = 0; octave < Mathf.Max(1, octaves); octave++)
            {
                value += Perlin(position * frequency, seed + octave * 1013) * amplitude;
                amplitudeSum += amplitude;
                frequency *= lacunarity;
                amplitude *= persistence;
            }

            return amplitudeSum > 0f ? value / amplitudeSum : 0f;
        }

        private static float Perlin(Vector3 position, int seed)
        {
            int x0 = Mathf.FloorToInt(position.x);
            int y0 = Mathf.FloorToInt(position.y);
            int z0 = Mathf.FloorToInt(position.z);
            float tx = position.x - x0;
            float ty = position.y - y0;
            float tz = position.z - z0;
            float u = Fade(tx);
            float v = Fade(ty);
            float w = Fade(tz);

            float n000 = GradientDot(x0, y0, z0, tx, ty, tz, seed);
            float n100 = GradientDot(x0 + 1, y0, z0, tx - 1f, ty, tz, seed);
            float n010 = GradientDot(x0, y0 + 1, z0, tx, ty - 1f, tz, seed);
            float n110 = GradientDot(x0 + 1, y0 + 1, z0, tx - 1f, ty - 1f, tz, seed);
            float n001 = GradientDot(x0, y0, z0 + 1, tx, ty, tz - 1f, seed);
            float n101 = GradientDot(x0 + 1, y0, z0 + 1, tx - 1f, ty, tz - 1f, seed);
            float n011 = GradientDot(x0, y0 + 1, z0 + 1, tx, ty - 1f, tz - 1f, seed);
            float n111 = GradientDot(
                x0 + 1,
                y0 + 1,
                z0 + 1,
                tx - 1f,
                ty - 1f,
                tz - 1f,
                seed);

            float x00 = Mathf.Lerp(n000, n100, u);
            float x10 = Mathf.Lerp(n010, n110, u);
            float x01 = Mathf.Lerp(n001, n101, u);
            float x11 = Mathf.Lerp(n011, n111, u);
            float y0Value = Mathf.Lerp(x00, x10, v);
            float y1Value = Mathf.Lerp(x01, x11, v);
            return Mathf.Lerp(y0Value, y1Value, w) * 0.70710678f;
        }

        private static float GradientDot(
            int x,
            int y,
            int z,
            float dx,
            float dy,
            float dz,
            int seed)
        {
            uint hash = Hash(x, y, z, seed);
            Vector3 gradient = Gradients[hash % (uint)Gradients.Length];
            return gradient.x * dx + gradient.y * dy + gradient.z * dz;
        }

        private static float Fade(float value)
        {
            return value * value * value * (value * (value * 6f - 15f) + 10f);
        }

        private static Vector3 SeedOffset(int seed)
        {
            uint x = Avalanche((uint)seed);
            uint y = Avalanche(x ^ 0x68E31DA4u);
            uint z = Avalanche(y ^ 0xB5297A4Du);
            return new Vector3(
                (x & 0xFFFFu) / 521.3f,
                (y & 0xFFFFu) / 487.1f,
                (z & 0xFFFFu) / 503.9f);
        }

        private static uint Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint hash = (uint)seed;
                hash ^= (uint)x * 0x8DA6B343u;
                hash ^= (uint)y * 0xD8163841u;
                hash ^= (uint)z * 0xCB1AB31Fu;
                return Avalanche(hash);
            }
        }

        private static uint Avalanche(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }
}
