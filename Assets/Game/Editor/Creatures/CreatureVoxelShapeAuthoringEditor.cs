using System;
using System.Collections.Generic;
using Supernova.MinecraftCaves.Creatures;
using UnityEditor;
using UnityEngine;

namespace Supernova.MinecraftCaves.Editor.Creatures
{
    [CustomEditor(typeof(CreatureVoxelShapeAuthoring))]
    public sealed class CreatureVoxelShapeAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            var authoring = (CreatureVoxelShapeAuthoring)target;
            using (new EditorGUI.DisabledScope(
                       authoring.SourceCollider == null
                       || authoring.SourceCollider.sharedMesh == null))
            {
                if (GUILayout.Button("Bake Occupied Voxels"))
                {
                    Bake(authoring);
                }
            }

            CreatureVoxelShape shape = authoring.Shape;
            if (shape != null)
            {
                EditorGUILayout.HelpBox(
                    $"Baked voxels: {shape.OccupiedVoxels.Count}\n"
                    + $"Bounds: {shape.OccupiedBounds}",
                    MessageType.Info);
            }
        }

        private static void Bake(CreatureVoxelShapeAuthoring authoring)
        {
            try
            {
                List<Vector3Int> voxels = MeshColliderVoxelBaker.Bake(
                    authoring.transform,
                    authoring.SourceCollider,
                    authoring.BakeVoxelSize);
                if (voxels.Count == 0)
                {
                    EditorUtility.DisplayDialog(
                        "Creature Voxel Bake",
                        "The MeshCollider did not occupy any voxels.",
                        "OK");
                    return;
                }

                CreatureVoxelShape shape = authoring.Shape;
                if (shape == null)
                {
                    string path = EditorUtility.SaveFilePanelInProject(
                        "Save Creature Voxel Shape",
                        authoring.name + "VoxelShape",
                        "asset",
                        "Choose where to save the prebaked voxel occupancy asset.",
                        "Assets/Game");
                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }

                    shape = CreateInstance<CreatureVoxelShape>();
                    shape.SetBakedData(authoring.BakeVoxelSize, voxels);
                    AssetDatabase.CreateAsset(shape, path);
                    Undo.RecordObject(authoring, "Assign Creature Voxel Shape");
                    authoring.SetShape(shape);
                }
                else
                {
                    Undo.RecordObject(shape, "Bake Creature Voxel Shape");
                    shape.SetBakedData(authoring.BakeVoxelSize, voxels);
                    EditorUtility.SetDirty(shape);
                }

                EditorUtility.SetDirty(authoring);
                AssetDatabase.SaveAssets();
                SceneView.RepaintAll();

                int minimumY = int.MaxValue;
                for (int i = 0; i < voxels.Count; i++)
                {
                    minimumY = Mathf.Min(minimumY, voxels[i].y);
                }

                if (minimumY < 0)
                {
                    Debug.LogWarning(
                        $"{authoring.name}: baked voxels extend below foot origin y=0. "
                        + "Verify that the prefab origin is at the feet.",
                        authoring);
                }

                Debug.Log(
                    $"Baked {voxels.Count} occupied creature voxels into {shape.name}.",
                    authoring);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, authoring);
                EditorUtility.DisplayDialog(
                    "Creature Voxel Bake Failed",
                    exception.Message,
                    "OK");
            }
        }
    }

    internal static class MeshColliderVoxelBaker
    {
        private const int MaximumVoxelCount = 65536;
        private static readonly Vector3 InteriorRayDirection =
            new Vector3(1f, 0.37139f, 0.12721f).normalized;

        public static List<Vector3Int> Bake(
            Transform footOrigin,
            MeshCollider meshCollider,
            float voxelSize)
        {
            if (footOrigin == null)
            {
                throw new ArgumentNullException(nameof(footOrigin));
            }

            if (meshCollider == null || meshCollider.sharedMesh == null)
            {
                throw new InvalidOperationException("A MeshCollider with a shared mesh is required.");
            }

            if (voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(voxelSize));
            }

            Mesh mesh = meshCollider.sharedMesh;
            Vector3[] sourceVertices;
            int[] indices;
            try
            {
                sourceVertices = mesh.vertices;
                indices = mesh.triangles;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "The collider mesh must be readable for editor prebaking. "
                    + "Enable Read/Write on its model importer.",
                    exception);
            }

            if (sourceVertices.Length == 0 || indices.Length < 3)
            {
                throw new InvalidOperationException("The collider mesh has no triangles.");
            }

            var vertices = new Vector3[sourceVertices.Length];
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector3 world = meshCollider.transform.TransformPoint(sourceVertices[i]);
                vertices[i] = footOrigin.InverseTransformPoint(world);
            }

            var triangles = new Triangle[indices.Length / 3];
            Vector3 minimum = vertices[0];
            Vector3 maximum = vertices[0];
            for (int i = 0; i < vertices.Length; i++)
            {
                minimum = Vector3.Min(minimum, vertices[i]);
                maximum = Vector3.Max(maximum, vertices[i]);
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = new Triangle(
                    vertices[indices[i * 3]],
                    vertices[indices[i * 3 + 1]],
                    vertices[indices[i * 3 + 2]]);
            }

            Vector3Int minimumCell = FloorToInt(minimum / voxelSize);
            Vector3Int maximumCell = CeilToInt(maximum / voxelSize) - Vector3Int.one;
            Vector3Int size = maximumCell - minimumCell + Vector3Int.one;
            long candidateCount = (long)size.x * size.y * size.z;
            if (size.x <= 0 || size.y <= 0 || size.z <= 0 || candidateCount > MaximumVoxelCount)
            {
                throw new InvalidOperationException(
                    $"Bake bounds contain {candidateCount} candidate voxels. "
                    + $"The editor limit is {MaximumVoxelCount}; check the voxel size and prefab scale.");
            }

            var occupied = new List<Vector3Int>();
            Vector3 halfExtents = Vector3.one * voxelSize * 0.5f;
            for (int y = minimumCell.y; y <= maximumCell.y; y++)
            {
                for (int z = minimumCell.z; z <= maximumCell.z; z++)
                {
                    for (int x = minimumCell.x; x <= maximumCell.x; x++)
                    {
                        var cell = new Vector3Int(x, y, z);
                        Vector3 centre = ((Vector3)cell + Vector3.one * 0.5f) * voxelSize;
                        if (IntersectsSurface(centre, halfExtents, triangles)
                            || IsInsideClosedMesh(centre, triangles))
                        {
                            occupied.Add(cell);
                        }
                    }
                }
            }

            occupied.Sort(CompareCells);
            return occupied;
        }

        private static bool IntersectsSurface(
            Vector3 centre,
            Vector3 halfExtents,
            IReadOnlyList<Triangle> triangles)
        {
            for (int i = 0; i < triangles.Count; i++)
            {
                if (TriangleIntersectsBox(triangles[i], centre, halfExtents))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TriangleIntersectsBox(
            Triangle triangle,
            Vector3 centre,
            Vector3 half)
        {
            Vector3 v0 = triangle.A - centre;
            Vector3 v1 = triangle.B - centre;
            Vector3 v2 = triangle.C - centre;
            Vector3 e0 = v1 - v0;
            Vector3 e1 = v2 - v1;
            Vector3 e2 = v0 - v2;

            if (!OverlapsOnAxis(v0, v1, v2, Vector3.right, half)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.up, half)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.forward, half)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e0, e1), half))
            {
                return false;
            }

            Vector3[] edges = { e0, e1, e2 };
            Vector3[] boxAxes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                for (int axisIndex = 0; axisIndex < boxAxes.Length; axisIndex++)
                {
                    Vector3 axis = Vector3.Cross(edges[edgeIndex], boxAxes[axisIndex]);
                    if (!OverlapsOnAxis(v0, v1, v2, axis, half))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool OverlapsOnAxis(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 axis,
            Vector3 half)
        {
            if (axis.sqrMagnitude < 0.0000000001f)
            {
                return true;
            }

            float p0 = Vector3.Dot(v0, axis);
            float p1 = Vector3.Dot(v1, axis);
            float p2 = Vector3.Dot(v2, axis);
            float minimum = Mathf.Min(p0, Mathf.Min(p1, p2));
            float maximum = Mathf.Max(p0, Mathf.Max(p1, p2));
            float radius = half.x * Mathf.Abs(axis.x)
                + half.y * Mathf.Abs(axis.y)
                + half.z * Mathf.Abs(axis.z);
            return minimum <= radius && maximum >= -radius;
        }

        private static bool IsInsideClosedMesh(
            Vector3 point,
            IReadOnlyList<Triangle> triangles)
        {
            var distances = new List<float>();
            for (int i = 0; i < triangles.Count; i++)
            {
                if (RayIntersectsTriangle(point, InteriorRayDirection, triangles[i], out float distance))
                {
                    distances.Add(distance);
                }
            }

            if (distances.Count == 0)
            {
                return false;
            }

            distances.Sort();
            int uniqueIntersections = 1;
            for (int i = 1; i < distances.Count; i++)
            {
                if (Mathf.Abs(distances[i] - distances[i - 1]) > 0.0001f)
                {
                    uniqueIntersections++;
                }
            }

            return (uniqueIntersections & 1) == 1;
        }

        private static bool RayIntersectsTriangle(
            Vector3 origin,
            Vector3 direction,
            Triangle triangle,
            out float distance)
        {
            const float epsilon = 0.000001f;
            Vector3 edge1 = triangle.B - triangle.A;
            Vector3 edge2 = triangle.C - triangle.A;
            Vector3 h = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, h);
            if (Mathf.Abs(determinant) < epsilon)
            {
                distance = 0f;
                return false;
            }

            float inverse = 1f / determinant;
            Vector3 s = origin - triangle.A;
            float u = inverse * Vector3.Dot(s, h);
            if (u < 0f || u > 1f)
            {
                distance = 0f;
                return false;
            }

            Vector3 q = Vector3.Cross(s, edge1);
            float v = inverse * Vector3.Dot(direction, q);
            if (v < 0f || u + v > 1f)
            {
                distance = 0f;
                return false;
            }

            distance = inverse * Vector3.Dot(edge2, q);
            return distance > epsilon;
        }

        private static Vector3Int FloorToInt(Vector3 value)
        {
            return new Vector3Int(
                Mathf.FloorToInt(value.x),
                Mathf.FloorToInt(value.y),
                Mathf.FloorToInt(value.z));
        }

        private static Vector3Int CeilToInt(Vector3 value)
        {
            return new Vector3Int(
                Mathf.CeilToInt(value.x),
                Mathf.CeilToInt(value.y),
                Mathf.CeilToInt(value.z));
        }

        private static int CompareCells(Vector3Int left, Vector3Int right)
        {
            int y = left.y.CompareTo(right.y);
            if (y != 0)
            {
                return y;
            }

            int z = left.z.CompareTo(right.z);
            return z != 0 ? z : left.x.CompareTo(right.x);
        }

        private readonly struct Triangle
        {
            public Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                A = a;
                B = b;
                C = c;
            }

            public Vector3 A { get; }
            public Vector3 B { get; }
            public Vector3 C { get; }
        }
    }
}
