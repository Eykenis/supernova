using System;
using System.Collections.Generic;
using Supernova.MinecraftCaves.Creatures;
using UnityEditor;
using UnityEngine;

namespace Supernova.MinecraftCaves.Editor.Creatures
{
    public static class CreatureNavigationValidator
    {
        [MenuItem("Tools/Minecraft Caves/Validate Creature Navigation")]
        public static void Validate()
        {
            ValidateMeshColliderBake();
            CreatureVoxelShape shape = ScriptableObject.CreateInstance<CreatureVoxelShape>();
            try
            {
                shape.SetBakedData(1f, new[] { Vector3Int.zero });
                var query = new ValidationVoxelQuery();
                var settings = new CreatureNavigationSettings
                {
                    safeFallHeight = 3,
                    maximumJumpHeight = 1,
                    maximumSingleMoveCost = 100,
                    maximumExpandedNodes = 2048,
                };
                var path = new List<Vector3Int>();
                Vector3Int start = new Vector3Int(0, 0, 0);
                Vector3Int target = new Vector3Int(7, 0, 0);

                Require(
                    CreatureVoxelNavigation.TryFindPath(
                        query,
                        shape,
                        settings,
                        start,
                        target,
                        path,
                        out int expanded),
                    "A* did not find the route around the two-voxel-high wall.");
                Require(path.Count > 1, "A* returned an empty route.");
                Require(path[0] == start && path[path.Count - 1] == target,
                    "Path reconstruction lost its start or target.");

                for (int i = 1; i < path.Count; i++)
                {
                    Vector3Int horizontal = path[i] - path[i - 1];
                    Require(Mathf.Abs(horizontal.x) <= 1 && Mathf.Abs(horizontal.z) <= 1,
                        "A* expanded a node outside the horizontal eight-neighbour set.");
                    Require(
                        CreatureVoxelNavigation.TryResolveTransition(
                            query,
                            shape,
                            settings,
                            path[i - 1],
                            horizontal,
                            out Vector3Int destination,
                            out int stepCost)
                        && destination == path[i]
                        && stepCost <= settings.maximumSingleMoveCost,
                        "A reconstructed step failed transition validation.");
                }

                Require(
                    path.Exists(node => node.z != 0),
                    "The route did not detour around the blocked cells.");
                Debug.Log(
                    $"Creature navigation validation passed: {path.Count} path nodes, "
                    + $"{expanded} expanded nodes.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shape);
            }
        }

        private static void ValidateMeshColliderBake()
        {
            var root = new GameObject("CreatureVoxelBakeValidation");
            var mesh = new Mesh { name = "UnitFootCube" };
            try
            {
                mesh.vertices = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(1f, 1f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 0f, 1f),
                    new Vector3(1f, 0f, 1f),
                    new Vector3(1f, 1f, 1f),
                    new Vector3(0f, 1f, 1f),
                };
                mesh.triangles = new[]
                {
                    0, 2, 1, 0, 3, 2,
                    4, 5, 6, 4, 6, 7,
                    0, 1, 5, 0, 5, 4,
                    3, 7, 6, 3, 6, 2,
                    0, 4, 7, 0, 7, 3,
                    1, 2, 6, 1, 6, 5,
                };
                mesh.RecalculateBounds();
                MeshCollider collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;

                List<Vector3Int> baked = MeshColliderVoxelBaker.Bake(
                    root.transform,
                    collider,
                    1f);
                Require(
                    baked.Count == 1 && baked[0] == Vector3Int.zero,
                    "A one-voxel closed MeshCollider did not prebake to exactly one voxel.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class ValidationVoxelQuery : ICreatureVoxelQuery
        {
            public bool TryGetSolid(Vector3Int voxel, out bool isSolid)
            {
                if (voxel.x < -16 || voxel.x > 16
                    || voxel.y < -8 || voxel.y > 8
                    || voxel.z < -16 || voxel.z > 16)
                {
                    isSolid = false;
                    return false;
                }

                bool wall = voxel.x >= 2 && voxel.x <= 5
                    && voxel.z == 0
                    && (voxel.y == 1 || voxel.y == 2);
                isSolid = voxel.y <= 0 || wall;
                return true;
            }
        }
    }
}
