using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Generates stylised grass blade meshes for instanced rendering.
    /// <para>
    /// Channel contract relied upon by
    /// <c>Supernova/Vegetation/Cave Grass Blade</c>:
    /// </para>
    /// <list type="bullet">
    /// <item>Root vertices sit at exactly <c>y == 0</c> so the shader can pin them
    /// and blades never detach from the terrain when the wind bends them.</item>
    /// <item><c>UV0.x</c> spans the blade width, <c>UV0.y</c> is the root-to-tip
    /// ratio driving wind weight, the colour gradient and the root occlusion.</item>
    /// <item><c>UV1.x</c> is the blade index within the instance (phase and hue
    /// jitter), <c>UV1.y</c> is that blade's height multiplier.</item>
    /// <item><c>COLOR</c>, <c>UV2</c> and <c>UV3</c> are deliberately left empty
    /// and reserved for future interaction (trample, cut, burn).</item>
    /// </list>
    /// Blades are single sided; the shader renders them with <c>Cull Off</c> and
    /// flips the normal for backfaces, which halves the geometry the previous
    /// duplicated-vertex placeholder needed.
    /// </summary>
    public static class CaveGrassBladeMeshBuilder
    {
        /// <summary>Golden angle, so blade yaws spread without clustering.</summary>
        private const float BladeYawIncrement = 137.507764f;
        private const float BladeYawJitter = 26f;
        private const float BladeHeightJitter = 0.28f;
        private const float BladeRootScatter = 0.4f;

        /// <summary>
        /// Extra bounds margin in blade heights. Instanced draws frustum-cull
        /// against mesh bounds, so wind-displaced tips must stay inside them.
        /// </summary>
        private const float WindBoundsMargin = 0.75f;

        public static Mesh Build(
            in CaveGrassBladeMeshSettings settings,
            string meshName = "Cave Grass Blade")
        {
            CaveGrassBladeMeshSettings sanitized = settings.Sanitized();
            int bladeCount = sanitized.bladeCount;
            int segments = sanitized.segmentsPerBlade;
            int ringsPerBlade = segments + 1;

            var vertices = new List<Vector3>(bladeCount * ringsPerBlade * 2);
            var normals = new List<Vector3>(vertices.Capacity);
            var bladeUvs = new List<Vector2>(vertices.Capacity);
            var bladeData = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(bladeCount * segments * 6);

            var random = new BladeRandom(sanitized.seed);
            float maximumHeight = 0f;
            float maximumHalfWidth = 0f;

            for (int blade = 0; blade < bladeCount; blade++)
            {
                float yaw = blade * BladeYawIncrement
                    + ((float)random.NextDouble() * 2f - 1f) * BladeYawJitter;
                float heightScale = 1f
                    + ((float)random.NextDouble() * 2f - 1f) * BladeHeightJitter;
                float bladeHeight = sanitized.height * heightScale;
                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

                // Roots fan out slightly so the blades do not all emanate from a
                // single point, but stay on the y == 0 plane.
                float scatterAngle = (float)random.NextDouble() * Mathf.PI * 2f;
                float scatterRadius = (float)random.NextDouble()
                    * sanitized.rootHalfWidth * BladeRootScatter;
                var rootOffset = new Vector3(
                    Mathf.Cos(scatterAngle) * scatterRadius,
                    0f,
                    Mathf.Sin(scatterAngle) * scatterRadius);

                int bladeFirstVertex = vertices.Count;
                for (int ring = 0; ring <= segments; ring++)
                {
                    float heightRatio = (float)ring / segments;
                    float halfWidth = Mathf.Lerp(
                        sanitized.rootHalfWidth,
                        sanitized.rootHalfWidth * sanitized.tipWidthFraction,
                        heightRatio);

                    // Quadratic lean keeps the root tangent vertical, so the base
                    // stays flush with the ground while the tip carries the bend.
                    float lean = sanitized.restingBend
                        * bladeHeight
                        * heightRatio
                        * heightRatio;
                    var spine = new Vector3(
                        0f,
                        bladeHeight * heightRatio,
                        lean);

                    var tangent = new Vector3(
                        0f,
                        bladeHeight,
                        2f * sanitized.restingBend * bladeHeight * heightRatio);
                    Vector3 normal = Vector3
                        .Cross(Vector3.right, tangent)
                        .normalized;

                    Vector3 rotatedNormal = rotation * normal;
                    Vector3 left = rotation
                        * (spine + new Vector3(-halfWidth, 0f, 0f))
                        + rootOffset;
                    Vector3 right = rotation
                        * (spine + new Vector3(halfWidth, 0f, 0f))
                        + rootOffset;

                    // Root vertices must land exactly on the plane; the rotation
                    // above is yaw only, but clamp to defend the shader contract.
                    if (ring == 0)
                    {
                        left.y = 0f;
                        right.y = 0f;
                    }

                    vertices.Add(left);
                    vertices.Add(right);
                    normals.Add(rotatedNormal);
                    normals.Add(rotatedNormal);
                    bladeUvs.Add(new Vector2(0f, heightRatio));
                    bladeUvs.Add(new Vector2(1f, heightRatio));
                    bladeData.Add(new Vector2(blade, heightScale));
                    bladeData.Add(new Vector2(blade, heightScale));

                    maximumHalfWidth = Mathf.Max(maximumHalfWidth, halfWidth);
                }

                for (int segment = 0; segment < segments; segment++)
                {
                    int lower = bladeFirstVertex + segment * 2;
                    int upper = lower + 2;
                    triangles.Add(lower);
                    triangles.Add(upper);
                    triangles.Add(lower + 1);
                    triangles.Add(lower + 1);
                    triangles.Add(upper);
                    triangles.Add(upper + 1);
                }

                maximumHeight = Mathf.Max(maximumHeight, bladeHeight);
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, bladeUvs);
            mesh.SetUVs(1, bladeData);
            mesh.SetTriangles(triangles, 0, false);

            float margin = maximumHeight * WindBoundsMargin;
            float horizontalExtent = maximumHeight
                * Mathf.Abs(sanitized.restingBend)
                + maximumHalfWidth
                + margin;
            mesh.bounds = new Bounds(
                new Vector3(0f, maximumHeight * 0.5f, 0f),
                new Vector3(
                    horizontalExtent * 2f,
                    maximumHeight + margin,
                    horizontalExtent * 2f));
            return mesh;
        }

        /// <summary>
        /// splitmix64, matching the generator's stream so authored meshes stay
        /// byte-identical across machines and editor sessions.
        /// </summary>
        private struct BladeRandom
        {
            private const double Inverse53BitRange = 1.0 / 9007199254740992.0;

            private ulong state;

            public BladeRandom(int seed)
            {
                state = (ulong)(uint)seed * 0x9E3779B185EBCA87UL;
            }

            public double NextDouble()
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong value = state;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (value >> 11) * Inverse53BitRange;
            }
        }
    }
}
