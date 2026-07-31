using System.Collections;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Creatures;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Voxels
{
    /// <summary>
    /// Independent, mineable SolidGun platform. Its fixed 16-sided mesh grows
    /// radially without forcing cave voxel mesh rebuilds.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(
        typeof(MeshFilter),
        typeof(MeshRenderer),
        typeof(MeshCollider))]
    public sealed class SolidVoxelPrototype : MonoBehaviour
    {
        public const int PlatformSides = 16;

        private Mesh generatedMesh;
        private bool destroyedByMining;
        private float worldDiameter;
        private float worldThickness;
        private MinecraftCaveInfiniteWorld navigationWorld;
        private bool navigationRegistered;

        public Mesh GeneratedMesh => generatedMesh;
        public bool IsGrowthComplete { get; private set; }
        public Material PlatformMaterial =>
            GetComponent<MeshRenderer>().sharedMaterial;

        public static SolidVoxelPrototype Create(
            Vector3 impactPoint,
            int diameter,
            float unitSize,
            float thickness,
            float growthDuration,
            Material material)
        {
            float worldDiameter =
                Mathf.Max(1, diameter) * Mathf.Max(0.01f, unitSize);
            var platformObject = new GameObject("SolidGun Platform");
            platformObject.transform.SetPositionAndRotation(
                impactPoint,
                Quaternion.identity);

            SolidVoxelPrototype platform =
                platformObject.AddComponent<SolidVoxelPrototype>();
            platform.Initialize(
                worldDiameter,
                thickness,
                growthDuration,
                material);
            return platform;
        }

        public static Mesh BuildPlatformMesh(
            float diameter,
            float thickness,
            int sides = PlatformSides)
        {
            int safeSides = Mathf.Max(3, sides);
            float radius = Mathf.Max(0.01f, diameter) * 0.5f;
            float halfThickness = Mathf.Max(0.01f, thickness) * 0.5f;
            int ringVertexCount = safeSides + 1;
            int topCenter = 0;
            int topRing = 1;
            int bottomCenter = topRing + ringVertexCount;
            int bottomRing = bottomCenter + 1;
            int sideRing = bottomRing + ringVertexCount;
            var vertices = new Vector3[
                sideRing + ringVertexCount * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[safeSides * 12];

            vertices[topCenter] = Vector3.up * halfThickness;
            vertices[bottomCenter] = Vector3.down * halfThickness;
            uvs[topCenter] = new Vector2(0.5f, 0.5f);
            uvs[bottomCenter] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i <= safeSides; i++)
            {
                float ratio = i / (float)safeSides;
                float angle = ratio * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                Vector3 top = new Vector3(x, halfThickness, z);
                Vector3 bottom = new Vector3(x, -halfThickness, z);
                Vector2 capUv = new Vector2(
                    x / (radius * 2f) + 0.5f,
                    z / (radius * 2f) + 0.5f);

                vertices[topRing + i] = top;
                vertices[bottomRing + i] = bottom;
                vertices[sideRing + i * 2] = bottom;
                vertices[sideRing + i * 2 + 1] = top;
                uvs[topRing + i] = capUv;
                uvs[bottomRing + i] = capUv;
                uvs[sideRing + i * 2] = new Vector2(ratio, 0f);
                uvs[sideRing + i * 2 + 1] = new Vector2(ratio, 1f);
            }

            int triangle = 0;
            for (int i = 0; i < safeSides; i++)
            {
                triangles[triangle++] = topCenter;
                triangles[triangle++] = topRing + i + 1;
                triangles[triangle++] = topRing + i;

                triangles[triangle++] = bottomCenter;
                triangles[triangle++] = bottomRing + i;
                triangles[triangle++] = bottomRing + i + 1;

                int sideBottom = sideRing + i * 2;
                triangles[triangle++] = sideBottom;
                triangles[triangle++] = sideBottom + 1;
                triangles[triangle++] = sideBottom + 3;
                triangles[triangle++] = sideBottom;
                triangles[triangle++] = sideBottom + 3;
                triangles[triangle++] = sideBottom + 2;
            }

            var mesh = new Mesh
            {
                name = "SolidGun Platform Mesh",
                vertices = vertices,
                triangles = triangles,
                uv = uvs,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public bool DestroyByMining()
        {
            if (destroyedByMining) return false;
            destroyedByMining = true;
            gameObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
            return true;
        }

        private void Initialize(
            float diameter,
            float thickness,
            float growthDuration,
            Material material)
        {
            worldDiameter = Mathf.Max(0.01f, diameter);
            worldThickness = Mathf.Max(0.01f, thickness);
            generatedMesh = BuildPlatformMesh(
                worldDiameter,
                worldThickness,
                PlatformSides);
            generatedMesh.hideFlags = HideFlags.DontSave;

            MeshFilter filter = GetComponent<MeshFilter>();
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            MeshCollider meshCollider = GetComponent<MeshCollider>();
            filter.sharedMesh = generatedMesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            meshCollider.sharedMesh = generatedMesh;

            if (Application.isPlaying)
            {
                transform.localScale = new Vector3(0.02f, 1f, 0.02f);
                StartCoroutine(Grow(Mathf.Max(0.01f, growthDuration)));
            }
            else
            {
                transform.localScale = Vector3.one;
                IsGrowthComplete = true;
                RegisterNavigationSupport();
            }
        }

        private IEnumerator Grow(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float radialScale = Mathf.SmoothStep(0.02f, 1f, progress);
                transform.localScale =
                    new Vector3(radialScale, 1f, radialScale);
                yield return null;
            }

            transform.localScale = Vector3.one;
            IsGrowthComplete = true;
            RegisterNavigationSupport();
        }

        private void OnEnable()
        {
            if (IsGrowthComplete && generatedMesh != null)
            {
                RegisterNavigationSupport();
            }
        }

        private void OnDisable()
        {
            UnregisterNavigationSupport();
        }

        private void RegisterNavigationSupport()
        {
            if (navigationRegistered)
            {
                return;
            }

            navigationWorld = navigationWorld != null
                ? navigationWorld
                : FindObjectOfType<MinecraftCaveInfiniteWorld>();
            if (navigationWorld == null)
            {
                return;
            }

            DynamicCreatureNavigation.RegisterPlatform(
                navigationWorld,
                this,
                transform.position,
                worldDiameter * 0.5f,
                worldThickness * 0.5f);
            navigationRegistered = true;
        }

        private void UnregisterNavigationSupport()
        {
            if (!navigationRegistered)
            {
                return;
            }

            DynamicCreatureNavigation.UnregisterPlatform(
                navigationWorld,
                this);
            navigationRegistered = false;
        }

        private void OnDestroy()
        {
            UnregisterNavigationSupport();
            if (generatedMesh == null) return;
            if (Application.isPlaying)
                Destroy(generatedMesh);
            else
                DestroyImmediate(generatedMesh);
            generatedMesh = null;
        }
    }
}
