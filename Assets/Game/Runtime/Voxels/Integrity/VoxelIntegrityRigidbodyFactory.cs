using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Voxels.Integrity
{
    /// <summary>
    /// Builds one rigid body whose rendered surface follows its voxel set.
    /// Concave dynamic bodies use a bounded compound of CoACD convex
    /// MeshColliders instead of primitives or one inaccurate convex hull.
    /// </summary>
    public static class VoxelIntegrityRigidbodyFactory
    {
        private static readonly Vector3Int[] FaceNeighbours =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1),
        };

        private static readonly Vector3[,] FaceCorners =
        {
            {
                new Vector3(1, -1, -1), new Vector3(1, 1, -1),
                new Vector3(1, 1, 1), new Vector3(1, -1, 1),
            },
            {
                new Vector3(-1, -1, 1), new Vector3(-1, 1, 1),
                new Vector3(-1, 1, -1), new Vector3(-1, -1, -1),
            },
            {
                new Vector3(-1, 1, -1), new Vector3(-1, 1, 1),
                new Vector3(1, 1, 1), new Vector3(1, 1, -1),
            },
            {
                new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
                new Vector3(1, -1, -1), new Vector3(1, -1, 1),
            },
            {
                new Vector3(1, -1, 1), new Vector3(1, 1, 1),
                new Vector3(-1, 1, 1), new Vector3(-1, -1, 1),
            },
            {
                new Vector3(-1, -1, -1), new Vector3(-1, 1, -1),
                new Vector3(1, 1, -1), new Vector3(1, -1, -1),
            },
        };

        public static GameObject Create(
            IReadOnlyList<Vector3Int> component,
            float voxelSize,
            Transform terrainTransform,
            Material material,
            float massPerVoxel = 0.25f)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));
            if (component.Count == 0)
                throw new ArgumentException(
                    "A rigid voxel component cannot be empty.",
                    nameof(component));
            if (voxelSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(voxelSize));

            var occupancy = new HashSet<Vector3Int>(component);
            Vector3 pivot = CalculatePivot(component);
            Mesh mesh = BuildSurfaceMesh(occupancy, pivot, voxelSize);

            var root = new GameObject(
                $"IntegrityRigidBody_{component.Count}Voxels");
            Vector3 localPivot = pivot * voxelSize;
            if (terrainTransform != null)
            {
                root.transform.SetPositionAndRotation(
                    terrainTransform.TransformPoint(localPivot),
                    terrainTransform.rotation);
                root.transform.localScale = terrainTransform.lossyScale;
            }
            else
            {
                root.transform.position = localPivot;
            }

            MeshFilter filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;

            List<VoxelConvexColliderMeshData> colliderMeshes =
                VoxelConvexDecomposer.Decompose(
                    mesh.vertices,
                    mesh.triangles,
                    Vector3.zero,
                    VoxelConvexDecompositionSettings.Default);
            AddConvexMeshColliders(
                root,
                colliderMeshes,
                component.Count);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.01f, component.Count * massPerVoxel);
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            return root;
        }

        /// <summary>
        /// Creates the physical component from the world's extracted Marching
        /// Cubes surface. The render mesh is only recentered; no vertex is
        /// regenerated or snapped to voxel corners.
        /// </summary>
        public static GameObject CreateFromMarchingCubes(
            IReadOnlyList<Vector3Int> component,
            VoxelMeshData meshData,
            float voxelSize,
            Transform terrainTransform,
            Material[] materials,
            float mass,
            VoxelMeshMassProperties? precomputedProperties = null,
            IReadOnlyList<VoxelConvexColliderMeshData> convexColliderMeshes = null)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }
            if (component.Count == 0)
            {
                throw new ArgumentException(
                    "A rigid voxel component cannot be empty.",
                    nameof(component));
            }
            if (meshData == null)
            {
                throw new ArgumentNullException(nameof(meshData));
            }
            if (meshData.Vertices.Count == 0
                || meshData.Triangles.Count == 0)
            {
                throw new ArgumentException(
                    "A rigid voxel component requires a non-empty surface.",
                    nameof(meshData));
            }
            if (voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(voxelSize));
            }

            VoxelMeshMassProperties properties =
                precomputedProperties
                    ?? CalculateMassProperties(
                        meshData.Vertices,
                        meshData.Triangles);
            Vector3 pivot = properties.Volume > 0.000001f
                ? properties.Centroid
                : CalculatePivot(component) * voxelSize;
            Mesh mesh = meshData.CreateMesh(
                $"IntegrityMarchingComponent_{component.Count}Voxels");
            mesh.hideFlags = HideFlags.DontSave;

            var vertices = new List<Vector3>(mesh.vertexCount);
            mesh.GetVertices(vertices);
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] -= pivot;
            }
            mesh.SetVertices(vertices);
            mesh.RecalculateBounds();

            var root = new GameObject(
                $"IntegrityRigidBody_{component.Count}Voxels");
            if (terrainTransform != null)
            {
                root.transform.SetPositionAndRotation(
                    terrainTransform.TransformPoint(pivot),
                    terrainTransform.rotation);
                root.transform.localScale = terrainTransform.lossyScale;
            }
            else
            {
                root.transform.position = pivot;
            }

            MeshFilter filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials != null
                && materials.Length > 0
                    ? materials
                    : new Material[1];
            renderer.shadowCastingMode = ShadowCastingMode.On;

            // Dynamic rigidbodies cannot use one concave MeshCollider. CoACD
            // supplies a small compound of convex meshes which preserves the
            // MC surface's concavities without per-voxel primitive colliders.
            if (convexColliderMeshes == null
                || convexColliderMeshes.Count == 0)
            {
                var centeredVertices = new Vector3[meshData.Vertices.Count];
                for (int i = 0; i < centeredVertices.Length; i++)
                {
                    centeredVertices[i] = meshData.Vertices[i] - pivot;
                }
                convexColliderMeshes = new[]
                {
                    new VoxelConvexColliderMeshData(
                        centeredVertices,
                        meshData.Triangles.ToArray()),
                };
            }
            AddConvexMeshColliders(
                root,
                convexColliderMeshes,
                component.Count);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.01f, mass);
            body.centerOfMass = Vector3.zero;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            return root;
        }

        internal static void AddConvexMeshColliders(
            GameObject root,
            IReadOnlyList<VoxelConvexColliderMeshData> colliderMeshes,
            int voxelCount)
        {
            MeshColliderCookingOptions cookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation
                | MeshColliderCookingOptions.EnableMeshCleaning
                | MeshColliderCookingOptions.WeldColocatedVertices
                | MeshColliderCookingOptions.UseFastMidphase;
            for (int i = 0; i < colliderMeshes.Count; i++)
            {
                Mesh colliderMesh = colliderMeshes[i].CreateMesh(
                    $"IntegrityConvexCollider_{voxelCount}Voxels_{i}");
                Physics.BakeMesh(
                    colliderMesh.GetInstanceID(),
                    true,
                    cookingOptions);
                MeshCollider collider = root.AddComponent<MeshCollider>();
                collider.cookingOptions = cookingOptions;
                collider.convex = true;
                collider.sharedMesh = colliderMesh;
            }
        }

        public static VoxelMeshMassProperties CalculateMassProperties(
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> triangles)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }
            if (triangles == null)
            {
                throw new ArgumentNullException(nameof(triangles));
            }
            if (triangles.Count % 3 != 0)
            {
                throw new ArgumentException(
                    "Triangle indices must be a multiple of three.",
                    nameof(triangles));
            }

            double signedVolume = 0d;
            double weightedX = 0d;
            double weightedY = 0d;
            double weightedZ = 0d;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                double tetrahedronVolume =
                    ((double)a.x * (b.y * c.z - b.z * c.y)
                    + (double)a.y * (b.z * c.x - b.x * c.z)
                    + (double)a.z * (b.x * c.y - b.y * c.x))
                    / 6d;
                signedVolume += tetrahedronVolume;
                weightedX +=
                    (a.x + b.x + c.x) * 0.25d * tetrahedronVolume;
                weightedY +=
                    (a.y + b.y + c.y) * 0.25d * tetrahedronVolume;
                weightedZ +=
                    (a.z + b.z + c.z) * 0.25d * tetrahedronVolume;
            }

            if (Math.Abs(signedVolume) <= 0.000000001d)
            {
                return new VoxelMeshMassProperties(0f, Vector3.zero);
            }

            var centroid = new Vector3(
                (float)(weightedX / signedVolume),
                (float)(weightedY / signedVolume),
                (float)(weightedZ / signedVolume));
            return new VoxelMeshMassProperties(
                (float)Math.Abs(signedVolume),
                centroid);
        }


        private static Vector3 CalculatePivot(
            IReadOnlyList<Vector3Int> component)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < component.Count; i++)
                sum += (Vector3)component[i];
            return sum / component.Count;
        }

        private static Mesh BuildSurfaceMesh(
            HashSet<Vector3Int> occupancy,
            Vector3 pivot,
            float voxelSize)
        {
            var vertices = new List<Vector3>(occupancy.Count * 16);
            var triangles = new List<int>(occupancy.Count * 24);
            var uvs = new List<Vector2>(occupancy.Count * 16);
            float halfSize = voxelSize * 0.5f;

            foreach (Vector3Int coordinate in occupancy)
            {
                Vector3 centre = ((Vector3)coordinate - pivot) * voxelSize;
                for (int face = 0; face < FaceNeighbours.Length; face++)
                {
                    if (occupancy.Contains(coordinate + FaceNeighbours[face]))
                        continue;

                    int firstVertex = vertices.Count;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        vertices.Add(
                            centre + FaceCorners[face, corner] * halfSize);
                    }
                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(0f, 1f));
                    uvs.Add(new Vector2(1f, 1f));
                    uvs.Add(new Vector2(1f, 0f));
                    triangles.Add(firstVertex);
                    triangles.Add(firstVertex + 1);
                    triangles.Add(firstVertex + 2);
                    triangles.Add(firstVertex);
                    triangles.Add(firstVertex + 2);
                    triangles.Add(firstVertex + 3);
                }
            }

            var mesh = new Mesh
            {
                name = $"IntegrityComponent_{occupancy.Count}Voxels",
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
