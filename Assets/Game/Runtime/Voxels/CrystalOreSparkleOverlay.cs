using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Voxels
{
    /// <summary>
    /// Recreates the crystal ore geometry-shader sparkles in Player builds
    /// with ordinary billboard mesh vertices. The editor keeps using the
    /// original geometry shader so authoring visuals remain unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrystalOreSparkleOverlay : MonoBehaviour
    {
        public const string OverlayDensityProperty =
            "_CrystalOreOverlayDensity";

        private const string SparkleDensityProperty =
            "_DetailAlbedoMapScale";
        private const string SparkleSpeedProperty =
            "_DetailNormalMapScale";
        private const string SparkleSizeProperty =
            "_ClearCoatSmoothness";
        private const string SparkleEnergyProperty =
            "_ClearCoatMask";
        private const string SparkleColorProperty =
            "_EmissionColor";
        private const string OverlayObjectName =
            "Crystal Ore Independent Sparkles";

        private static readonly Dictionary<Material, Material>
            compatibleMaterials = new Dictionary<Material, Material>();
        private static readonly Dictionary<Material, Material>
            overlayMaterials = new Dictionary<Material, Material>();

        private MeshRenderer sourceRenderer;
        private MeshFilter overlayFilter;
        private MeshRenderer overlayRenderer;
        private Mesh overlayMesh;

        public static void Synchronize(
            MeshRenderer renderer,
            Mesh sourceMesh)
        {
            if (renderer == null || Application.isEditor)
            {
                return;
            }

            Material[] materials = PreparePlayerMaterials(renderer);
            CrystalOreSparkleOverlay overlay =
                renderer.GetComponent<CrystalOreSparkleOverlay>();
            bool hasOreMaterial = HasOreMaterial(materials);
            if (!hasOreMaterial || sourceMesh == null || !sourceMesh.isReadable)
            {
                if (overlay != null)
                {
                    overlay.ClearOverlay();
                }
                return;
            }

            if (overlay == null)
            {
                overlay = renderer.gameObject
                    .AddComponent<CrystalOreSparkleOverlay>();
            }
            overlay.sourceRenderer = renderer;
            overlay.Rebuild(sourceMesh, materials);
        }

        public static void Clear(MeshRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            CrystalOreSparkleOverlay overlay =
                renderer.GetComponent<CrystalOreSparkleOverlay>();
            if (overlay != null)
            {
                overlay.ClearOverlay();
            }
        }

        private static Material[] PreparePlayerMaterials(
            MeshRenderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int index = 0; index < materials.Length; index++)
            {
                Material source = materials[index];
                Material prepared = PreparePlayerMaterial(source);
                if (prepared == source)
                {
                    continue;
                }

                materials[index] = prepared;
                changed = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
            return materials;
        }

        private static Material PreparePlayerMaterial(Material source)
        {
            if (source == null
                || source.shader == null
                || source.shader.name != VoxelShaderNames.CrystalOreLit)
            {
                return source;
            }

            Shader compatibleShader =
                Shader.Find(VoxelShaderNames.CrystalOreLitCompatible);
            if (compatibleShader == null)
            {
                return source;
            }

            float density = source.HasProperty(SparkleDensityProperty)
                ? source.GetFloat(SparkleDensityProperty)
                : 0f;
            int renderQueue = source.renderQueue;
            if ((source.hideFlags & HideFlags.DontSave) != 0)
            {
                source.shader = compatibleShader;
                ConfigureCompatibleMaterial(
                    source,
                    density,
                    renderQueue);
                return source;
            }

            if (!compatibleMaterials.TryGetValue(
                    source,
                    out Material compatible)
                || compatible == null)
            {
                compatible = new Material(source)
                {
                    name = source.name + " (Player Geometry-Free)",
                    hideFlags = HideFlags.DontSave,
                };
                compatible.shader = compatibleShader;
                compatibleMaterials[source] = compatible;
            }

            ConfigureCompatibleMaterial(
                compatible,
                density,
                renderQueue);
            return compatible;
        }

        private static void ConfigureCompatibleMaterial(
            Material material,
            float density,
            int renderQueue)
        {
            if (material.HasProperty(OverlayDensityProperty))
            {
                material.SetFloat(
                    OverlayDensityProperty,
                    Mathf.Clamp01(density));
            }
            if (material.HasProperty(SparkleDensityProperty))
            {
                material.SetFloat(SparkleDensityProperty, 0f);
            }
            material.renderQueue = renderQueue;
        }

        private static bool HasOreMaterial(Material[] materials)
        {
            for (int index = 0; index < materials.Length; index++)
            {
                Material material = materials[index];
                if (material != null
                    && material.shader != null
                    && material.shader.name
                        == VoxelShaderNames.CrystalOreLitCompatible
                    && ReadDensity(material) > 0f)
                {
                    return true;
                }
            }
            return false;
        }

        private static float ReadDensity(Material material)
        {
            if (material == null)
            {
                return 0f;
            }
            if (material.HasProperty(OverlayDensityProperty))
            {
                return Mathf.Clamp01(
                    material.GetFloat(OverlayDensityProperty));
            }
            return material.HasProperty(SparkleDensityProperty)
                ? Mathf.Clamp01(
                    material.GetFloat(SparkleDensityProperty))
                : 0f;
        }

        private void Rebuild(
            Mesh sourceMesh,
            Material[] sourceMaterials)
        {
            EnsureOverlayObjects();

            Vector3[] sourceVertices = sourceMesh.vertices;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var seeds = new List<Vector2>();
            var submeshTriangles = new List<List<int>>();
            var materials = new List<Material>();

            int submeshCount = Mathf.Min(
                sourceMesh.subMeshCount,
                sourceMaterials.Length);
            for (int submesh = 0; submesh < submeshCount; submesh++)
            {
                Material sourceMaterial = sourceMaterials[submesh];
                float density = ReadDensity(sourceMaterial);
                if (sourceMaterial == null
                    || sourceMaterial.shader == null
                    || sourceMaterial.shader.name
                        != VoxelShaderNames.CrystalOreLitCompatible
                    || density <= 0f)
                {
                    continue;
                }

                int[] triangles = sourceMesh.GetTriangles(submesh);
                var indices = new List<int>();
                for (int start = 0;
                    start + 2 < triangles.Length;
                    start += 3)
                {
                    int first = triangles[start];
                    int second = triangles[start + 1];
                    int third = triangles[start + 2];
                    if ((uint)first >= (uint)sourceVertices.Length
                        || (uint)second >= (uint)sourceVertices.Length
                        || (uint)third >= (uint)sourceVertices.Length)
                    {
                        continue;
                    }

                    Vector3 centre = (
                        sourceVertices[first]
                        + sourceVertices[second]
                        + sourceVertices[third]) / 3f;
                    Vector3 centreWorld =
                        transform.TransformPoint(centre);
                    Vector3 stableCell = new Vector3(
                        Mathf.Floor(centreWorld.x * 4f),
                        Mathf.Floor(centreWorld.y * 4f),
                        Mathf.Floor(centreWorld.z * 4f));
                    float primitive = start / 3f;
                    float selection = SparkleHash(
                        stableCell
                        + primitive * new Vector3(
                            0.731f,
                            1.137f,
                            1.913f));
                    if (selection > density)
                    {
                        continue;
                    }

                    float phaseSeed = SparkleHash(
                        new Vector3(
                            stableCell.z,
                            stableCell.y,
                            stableCell.x)
                        + primitive * new Vector3(
                            2.417f,
                            0.673f,
                            1.291f));
                    float sizeSeed = SparkleHash(
                        new Vector3(
                            stableCell.y,
                            stableCell.x,
                            stableCell.z)
                        + primitive * new Vector3(
                            1.619f,
                            2.231f,
                            0.419f));
                    AddSparkleQuad(
                        centre,
                        phaseSeed,
                        sizeSeed,
                        vertices,
                        uvs,
                        seeds,
                        indices);
                }

                if (indices.Count == 0)
                {
                    continue;
                }

                submeshTriangles.Add(indices);
                materials.Add(GetOverlayMaterial(sourceMaterial));
            }

            if (vertices.Count == 0)
            {
                ClearOverlay();
                return;
            }

            overlayMesh.Clear();
            overlayMesh.indexFormat = vertices.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            overlayMesh.SetVertices(vertices);
            overlayMesh.SetUVs(0, uvs);
            overlayMesh.SetUVs(1, seeds);
            overlayMesh.subMeshCount = submeshTriangles.Count;
            for (int submesh = 0;
                submesh < submeshTriangles.Count;
                submesh++)
            {
                overlayMesh.SetTriangles(
                    submeshTriangles[submesh],
                    submesh,
                    false);
            }

            Bounds bounds = sourceMesh.bounds;
            bounds.Expand(0.6f);
            overlayMesh.bounds = bounds;
            overlayFilter.sharedMesh = overlayMesh;
            overlayRenderer.sharedMaterials = materials.ToArray();
            overlayRenderer.enabled =
                sourceRenderer == null || sourceRenderer.enabled;
            overlayRenderer.gameObject.SetActive(true);
        }

        private static void AddSparkleQuad(
            Vector3 centre,
            float phaseSeed,
            float sizeSeed,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Vector2> seeds,
            List<int> indices)
        {
            int first = vertices.Count;
            Vector2 seed = new Vector2(phaseSeed, sizeSeed);
            vertices.Add(centre);
            vertices.Add(centre);
            vertices.Add(centre);
            vertices.Add(centre);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            seeds.Add(seed);
            seeds.Add(seed);
            seeds.Add(seed);
            seeds.Add(seed);
            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
            indices.Add(first + 2);
            indices.Add(first + 1);
            indices.Add(first + 3);
        }

        private static Material GetOverlayMaterial(Material source)
        {
            if (!overlayMaterials.TryGetValue(
                    source,
                    out Material overlay)
                || overlay == null)
            {
                Shader shader = Shader.Find(
                    VoxelShaderNames.CrystalOreSparkleOverlay);
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        "Crystal ore sparkle overlay shader was not found.");
                }

                overlay = new Material(shader)
                {
                    name = source.name + " Independent Sparkles",
                    hideFlags = HideFlags.DontSave,
                };
                overlayMaterials[source] = overlay;
            }

            CopyColor(source, overlay, SparkleColorProperty);
            CopyFloat(source, overlay, SparkleEnergyProperty);
            CopyFloat(source, overlay, SparkleSizeProperty);
            CopyFloat(source, overlay, SparkleSpeedProperty);
            return overlay;
        }

        private static void CopyColor(
            Material source,
            Material target,
            string property)
        {
            if (source.HasProperty(property)
                && target.HasProperty(property))
            {
                target.SetColor(property, source.GetColor(property));
            }
        }

        private static void CopyFloat(
            Material source,
            Material target,
            string property)
        {
            if (source.HasProperty(property)
                && target.HasProperty(property))
            {
                target.SetFloat(property, source.GetFloat(property));
            }
        }

        private static float SparkleHash(Vector3 value)
        {
            float hash = Mathf.Sin(Vector3.Dot(
                value,
                new Vector3(12.9898f, 78.233f, 37.719f)))
                * 43758.5453f;
            return hash - Mathf.Floor(hash);
        }

        private void EnsureOverlayObjects()
        {
            if (overlayMesh == null)
            {
                overlayMesh = new Mesh
                {
                    name = OverlayObjectName,
                    hideFlags = HideFlags.DontSave,
                };
            }

            if (overlayRenderer != null && overlayFilter != null)
            {
                return;
            }

            Transform existing = transform.Find(OverlayObjectName);
            GameObject overlayObject = existing != null
                ? existing.gameObject
                : new GameObject(
                    OverlayObjectName,
                    typeof(MeshFilter),
                    typeof(MeshRenderer));
            overlayObject.hideFlags = HideFlags.DontSave;
            overlayObject.layer = gameObject.layer;
            overlayObject.transform.SetParent(transform, false);
            overlayFilter = overlayObject.GetComponent<MeshFilter>();
            overlayRenderer = overlayObject.GetComponent<MeshRenderer>();
            overlayFilter.sharedMesh = overlayMesh;
            overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
            overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            overlayRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        private void LateUpdate()
        {
            if (overlayRenderer != null && sourceRenderer != null)
            {
                overlayRenderer.enabled = sourceRenderer.enabled;
                overlayRenderer.gameObject.layer =
                    sourceRenderer.gameObject.layer;
            }
        }

        private void ClearOverlay()
        {
            if (overlayMesh != null)
            {
                overlayMesh.Clear();
            }
            if (overlayRenderer != null)
            {
                overlayRenderer.sharedMaterials =
                    Array.Empty<Material>();
                overlayRenderer.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (overlayMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(overlayMesh);
            }
            else
            {
                DestroyImmediate(overlayMesh);
            }
            overlayMesh = null;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeMaterials()
        {
            DestroyMaterials(compatibleMaterials.Values);
            DestroyMaterials(overlayMaterials.Values);
            compatibleMaterials.Clear();
            overlayMaterials.Clear();
        }

        private static void DestroyMaterials(
            IEnumerable<Material> materials)
        {
            foreach (Material material in materials)
            {
                if (material == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(material);
                }
                else
                {
                    DestroyImmediate(material);
                }
            }
        }
    }
}
