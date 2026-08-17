using System.Collections.Generic;
using Supernova.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Supernova.Editor
{
    public static class TreasureFractureVariantBuilder
    {
        private const string OutputRoot =
            ProjectAssetPaths.Folders.TreasureFractureVariants;
        private const string SpawnTablePath =
            ProjectAssetPaths.Config.TreasureSpawnTable;
        private const int VariantCount = 3;

        [MenuItem("Tools/Supernova/Rebuild Treasure Fracture Variants")]
        public static void BuildAll()
        {
            TreasureSpawnTable table =
                AssetDatabase.LoadAssetAtPath<TreasureSpawnTable>(
                    SpawnTablePath);
            if (table == null)
            {
                Debug.LogError(
                    "Cannot build treasure fracture variants: spawn table "
                    + "was not found.");
                return;
            }

            BuildDefinitions(table.Treasures);
            Debug.Log("Rebuilt treasure fracture variants.");
        }

        public static void BuildDefinitions(
            IEnumerable<TreasureDefinition> definitions)
        {
            EnsureFolder(ProjectAssetPaths.Folders.TreasurePrefabs);
            EnsureFolder(OutputRoot);
            if (definitions == null)
            {
                return;
            }

            foreach (TreasureDefinition definition in definitions)
            {
                if (definition == null || definition.Prefab == null)
                {
                    continue;
                }

                List<GameObject> variants = BuildDefinition(definition);
                definition.ConfigureFractureVariants(variants);
                EditorUtility.SetDirty(definition);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static List<GameObject> BuildDefinition(
            TreasureDefinition definition)
        {
            var variants = new List<GameObject>();
            List<SourceMesh> sources = CollectSources(definition.Prefab);
            if (sources.Count == 0)
            {
                Debug.LogWarning(
                    $"{definition.name} has no mesh available for fracture.");
                return variants;
            }

            string definitionFolder = $"{OutputRoot}/{definition.name}";
            EnsureFolder(definitionFolder);
            for (int variantIndex = 0;
                 variantIndex < VariantCount;
                 variantIndex++)
            {
                int fragmentCount = 5 + variantIndex;
                int seed = unchecked(
                    StableHash(definition.name)
                    + variantIndex * 104729);
                var sourceFragments = new List<SourceFragments>();
                for (int sourceIndex = 0;
                    sourceIndex < sources.Count;
                    sourceIndex++)
                {
                    SourceMesh source = sources[sourceIndex];
                    IReadOnlyList<MeshFragmentBuilder.Fragment> fragments =
                        MeshFragmentBuilder.Build(
                            source.Mesh,
                            fragmentCount,
                            unchecked(seed + sourceIndex * 130363));
                    if (fragments.Count > 0)
                    {
                        sourceFragments.Add(
                            new SourceFragments(source, fragments));
                    }
                }
                GameObject variant = BuildVariantPrefab(
                    definition,
                    sourceFragments,
                    definitionFolder,
                    variantIndex);
                if (variant != null)
                {
                    variants.Add(variant);
                }
            }

            return variants;
        }

        private static GameObject BuildVariantPrefab(
            TreasureDefinition definition,
            IReadOnlyList<SourceFragments> sources,
            string definitionFolder,
            int variantIndex)
        {
            if (sources.Count == 0)
            {
                return null;
            }

            string variantName =
                $"{definition.name}_Fracture_{variantIndex + 1:00}";
            string variantFolder =
                $"{definitionFolder}/Variant_{variantIndex + 1:00}";
            EnsureFolder(variantFolder);

            var root = new GameObject(variantName);
            try
            {
                int pieceIndex = 0;
                for (int sourceIndex = 0;
                    sourceIndex < sources.Count;
                    sourceIndex++)
                {
                    SourceFragments source = sources[sourceIndex];
                    for (int i = 0; i < source.Fragments.Count; i++)
                    {
                        MeshFragmentBuilder.Fragment fragment =
                            source.Fragments[i];
                        pieceIndex++;
                        fragment.Mesh.name =
                            $"{variantName}_Piece_{pieceIndex:00}";
                        string meshPath =
                            $"{variantFolder}/{fragment.Mesh.name}.asset";
                        Mesh storedMesh =
                            AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                        if (storedMesh == null)
                        {
                            storedMesh = fragment.Mesh;
                            AssetDatabase.CreateAsset(storedMesh, meshPath);
                        }
                        else
                        {
                            EditorUtility.CopySerialized(
                                fragment.Mesh,
                                storedMesh);
                            Object.DestroyImmediate(fragment.Mesh);
                        }

                        var piece = new GameObject(
                            $"Piece_{pieceIndex:00}");
                        piece.transform.SetParent(root.transform, false);
                        piece.transform.localPosition =
                            source.Source.LocalPosition
                            + source.Source.LocalRotation
                            * Vector3.Scale(
                                fragment.LocalPosition,
                                source.Source.LocalScale);
                        piece.transform.localRotation =
                            source.Source.LocalRotation;
                        piece.transform.localScale =
                            source.Source.LocalScale;

                        MeshFilter filter =
                            piece.AddComponent<MeshFilter>();
                        filter.sharedMesh = storedMesh;
                        MeshRenderer renderer =
                            piece.AddComponent<MeshRenderer>();
                        renderer.sharedMaterials =
                            source.Source.Materials;

                        BoxCollider collider =
                            piece.AddComponent<BoxCollider>();
                        collider.center = storedMesh.bounds.center;
                        collider.size = Vector3.Max(
                            storedMesh.bounds.size,
                            Vector3.one * 0.04f);
                    }
                }

                string prefabPath =
                    $"{variantFolder}/{variantName}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static List<SourceMesh> CollectSources(GameObject prefab)
        {
            var result = new List<SourceMesh>();
            MeshFilter[] filters =
                prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                MeshRenderer renderer =
                    filter != null
                        ? filter.GetComponent<MeshRenderer>()
                        : null;
                if (filter == null
                    || filter.sharedMesh == null
                    || renderer == null)
                {
                    continue;
                }

                Matrix4x4 relative = prefab.transform.worldToLocalMatrix
                    * filter.transform.localToWorldMatrix;
                Vector3 right = relative.GetColumn(0);
                Vector3 up = relative.GetColumn(1);
                Vector3 forward = relative.GetColumn(2);
                Vector3 scale = new Vector3(
                    right.magnitude,
                    up.magnitude,
                    forward.magnitude);
                Quaternion rotation = forward.sqrMagnitude > 0f
                    && up.sqrMagnitude > 0f
                        ? Quaternion.LookRotation(
                            forward.normalized,
                            up.normalized)
                        : Quaternion.identity;
                result.Add(new SourceMesh(
                    filter.sharedMesh,
                    renderer.sharedMaterials,
                    relative.GetColumn(3),
                    rotation,
                    scale));
            }
            return result;
        }

        private readonly struct SourceMesh
        {
            public SourceMesh(
                Mesh mesh,
                Material[] materials,
                Vector3 localPosition,
                Quaternion localRotation,
                Vector3 localScale)
            {
                Mesh = mesh;
                Materials = materials;
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
            }

            public Mesh Mesh { get; }
            public Material[] Materials { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }
        }

        private readonly struct SourceFragments
        {
            public SourceFragments(
                SourceMesh source,
                IReadOnlyList<MeshFragmentBuilder.Fragment> fragments)
            {
                Source = source;
                Fragments = fragments;
            }

            public SourceMesh Source { get; }
            public IReadOnlyList<MeshFragmentBuilder.Fragment> Fragments
            {
                get;
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            string folderName = path.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < value.Length; i++)
                {
                    hash = hash * 31 + value[i];
                }
                return hash;
            }
        }
    }
}
