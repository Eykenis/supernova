using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    public enum CaveSurfacePlaceholderKind
    {
        Grass,
        Vine,
    }

    /// <summary>
    /// Lightweight generated geometry used only by the initial placeholder prefabs.
    /// Replacing a brush's prefab removes the need for this component entirely.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveSurfacePlaceholderVisual : MonoBehaviour
    {
        [SerializeField] private CaveSurfacePlaceholderKind kind;
        [SerializeField] private Color color = new Color(0.15f, 0.5f, 0.1f);

        private static readonly Dictionary<CaveSurfacePlaceholderKind, Mesh>
            Meshes = new Dictionary<CaveSurfacePlaceholderKind, Mesh>();
        private static readonly Dictionary<CaveSurfacePlaceholderKind, Material>
            Materials = new Dictionary<CaveSurfacePlaceholderKind, Material>();

        private void Awake()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();

            filter.sharedMesh = GetMesh(kind);
            renderer.sharedMaterial = GetMaterial(kind, color);
        }

        private static Mesh GetMesh(CaveSurfacePlaceholderKind placeholderKind)
        {
            if (Meshes.TryGetValue(placeholderKind, out Mesh mesh)
                && mesh != null)
            {
                return mesh;
            }

            mesh = CreatePlaceholderMesh(placeholderKind);
            mesh.name = placeholderKind + " Placeholder Mesh";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            Meshes[placeholderKind] = mesh;
            return mesh;
        }

        public static Mesh CreatePlaceholderMesh(
            CaveSurfacePlaceholderKind placeholderKind)
        {
            return placeholderKind == CaveSurfacePlaceholderKind.Grass
                ? BuildGrassMesh()
                : BuildVineMesh();
        }

        private static Material GetMaterial(
            CaveSurfacePlaceholderKind placeholderKind,
            Color materialColor)
        {
            if (Materials.TryGetValue(placeholderKind, out Material material)
                && material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            material = new Material(shader)
            {
                name = placeholderKind + " Placeholder Material",
                color = materialColor,
                enableInstancing = true,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", materialColor);
            }
            Materials[placeholderKind] = material;
            return material;
        }

        private static Mesh BuildGrassMesh()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            AddBlade(vertices, triangles, -18f, 0.24f, 0.035f);
            AddBlade(vertices, triangles, 42f, 0.32f, 0.03f);
            AddBlade(vertices, triangles, 96f, 0.2f, 0.04f);
            return CreateMesh(vertices, triangles);
        }

        private static void AddBlade(
            List<Vector3> vertices,
            List<int> triangles,
            float yaw,
            float height,
            float halfWidth)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 bottomLeft = rotation * new Vector3(-halfWidth, 0f, 0f);
            Vector3 bottomRight = rotation * new Vector3(halfWidth, 0f, 0f);
            Vector3 topRight = rotation * new Vector3(halfWidth * 0.2f, height, 0f);
            Vector3 topLeft = rotation * new Vector3(-halfWidth * 0.2f, height, 0f);
            AddDoubleSidedQuad(
                vertices,
                triangles,
                bottomLeft,
                bottomRight,
                topRight,
                topLeft);
        }

        private static Mesh BuildVineMesh()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            AddVineStrip(vertices, triangles, 0f);
            AddVineStrip(vertices, triangles, 90f);
            return CreateMesh(vertices, triangles);
        }

        private static void AddVineStrip(
            List<Vector3> vertices,
            List<int> triangles,
            float yaw)
        {
            const int segments = 6;
            const float height = 1.2f;
            const float halfWidth = 0.025f;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            for (int segment = 0; segment < segments; segment++)
            {
                float firstY = height * segment / segments;
                float secondY = height * (segment + 1) / segments;
                float firstWave = Mathf.Sin(segment * 1.4f) * 0.035f;
                float secondWave = Mathf.Sin((segment + 1) * 1.4f) * 0.035f;
                AddDoubleSidedQuad(
                    vertices,
                    triangles,
                    rotation * new Vector3(firstWave - halfWidth, firstY, 0f),
                    rotation * new Vector3(firstWave + halfWidth, firstY, 0f),
                    rotation * new Vector3(secondWave + halfWidth, secondY, 0f),
                    rotation * new Vector3(secondWave - halfWidth, secondY, 0f));
            }
        }

        private static void AddDoubleSidedQuad(
            List<Vector3> vertices,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth)
        {
            int front = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            vertices.Add(fourth);
            triangles.Add(front);
            triangles.Add(front + 2);
            triangles.Add(front + 1);
            triangles.Add(front);
            triangles.Add(front + 3);
            triangles.Add(front + 2);

            // Back faces use separate vertices so recalculated normals do not
            // cancel the front-face normals on the thin placeholder blades.
            int back = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            vertices.Add(fourth);
            triangles.Add(back);
            triangles.Add(back + 1);
            triangles.Add(back + 2);
            triangles.Add(back);
            triangles.Add(back + 2);
            triangles.Add(back + 3);
        }

        private static Mesh CreateMesh(
            List<Vector3> vertices,
            List<int> triangles)
        {
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
