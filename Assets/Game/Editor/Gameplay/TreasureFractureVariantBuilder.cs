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

            EnsureFolder(ProjectAssetPaths.Folders.TreasurePrefabs);
            EnsureFolder(OutputRoot);

            foreach (TreasureDefinition definition in table.Treasures)
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
            Debug.Log("Rebuilt treasure fracture variants.");
        }

        private static List<GameObject> BuildDefinition(
            TreasureDefinition definition)
        {
            MeshFilter sourceFilter =
                definition.Prefab.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer sourceRenderer =
                definition.Prefab.GetComponentInChildren<MeshRenderer>(true);
            var variants = new List<GameObject>();
            if (sourceFilter == null
                || sourceFilter.sharedMesh == null
                || sourceRenderer == null)
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
                IReadOnlyList<MeshFragmentBuilder.Fragment> fragments =
                    MeshFragmentBuilder.Build(
                        sourceFilter.sharedMesh,
                        fragmentCount,
                        seed);
                GameObject variant = BuildVariantPrefab(
                    definition,
                    sourceFilter,
                    sourceRenderer,
                    fragments,
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
            MeshFilter sourceFilter,
            MeshRenderer sourceRenderer,
            IReadOnlyList<MeshFragmentBuilder.Fragment> fragments,
            string definitionFolder,
            int variantIndex)
        {
            if (fragments.Count == 0)
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
                for (int i = 0; i < fragments.Count; i++)
                {
                    MeshFragmentBuilder.Fragment fragment = fragments[i];
                    fragment.Mesh.name =
                        $"{variantName}_Piece_{i + 1:00}";
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

                    var piece = new GameObject($"Piece_{i + 1:00}");
                    piece.transform.SetParent(root.transform, false);
                    piece.transform.localPosition =
                        sourceFilter.transform.localPosition
                        + sourceFilter.transform.localRotation
                        * Vector3.Scale(
                            fragment.LocalPosition,
                            sourceFilter.transform.localScale);
                    piece.transform.localRotation =
                        sourceFilter.transform.localRotation;
                    piece.transform.localScale =
                        sourceFilter.transform.localScale;

                    MeshFilter filter = piece.AddComponent<MeshFilter>();
                    filter.sharedMesh = storedMesh;
                    MeshRenderer renderer =
                        piece.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials =
                        sourceRenderer.sharedMaterials;

                    BoxCollider collider =
                        piece.AddComponent<BoxCollider>();
                    collider.center = storedMesh.bounds.center;
                    collider.size = Vector3.Max(
                        storedMesh.bounds.size,
                        Vector3.one * 0.04f);
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
