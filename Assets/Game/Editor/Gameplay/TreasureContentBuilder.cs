using System.Collections.Generic;
using Supernova.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Supernova.Editor
{
    public static class TreasureContentBuilder
    {
        private const float SphinxTargetHeight = 1.5f;

        [MenuItem("Tools/Supernova/Rebuild Treasure Content")]
        public static void BuildAll()
        {
            EnsureFolder(ProjectAssetPaths.Folders.Treasures);
            NormalizeStatuePrefab();
            ConfigureSphinxPrefab();

            PlayerToolDefinition bombTool =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.BombTool);
            TreasureDefinition statue = CreateOrUpdateDefinition(
                ProjectAssetPaths.Config.StatueTreasure,
                ProjectAssetPaths.Prefabs.StatueTreasure,
                "雕像",
                900,
                12f,
                0.35f,
                0.25f,
                1,
                null);
            TreasureDefinition sphinx = CreateOrUpdateDefinition(
                ProjectAssetPaths.Config.SphinxTreasure,
                ProjectAssetPaths.Prefabs.SphinxTreasure,
                "人面像",
                1800,
                30f,
                0.25f,
                0.15f,
                1,
                null);
            TreasureDefinition mysticCore = CreateOrUpdateDefinition(
                ProjectAssetPaths.Config.MysticCoreTreasure,
                ProjectAssetPaths.Prefabs.MysticCoreTreasure,
                "神秘核心",
                2500,
                6f,
                0.7f,
                0.1f,
                1,
                bombTool);

            UpdateSpawnTable(statue, sphinx, mysticCore);
            AssetDatabase.SaveAssets();
            TreasureFractureVariantBuilder.BuildDefinitions(
                new[] { statue, sphinx, mysticCore });
            Debug.Log("Rebuilt treasure configs, prototypes, and fragments.");
        }

        private static TreasureDefinition CreateOrUpdateDefinition(
            string definitionPath,
            string prefabPath,
            string displayName,
            int value,
            float weight,
            float fragility,
            float spawnChance,
            int attemptsPerChunk,
            PlayerToolDefinition destructionExplosionTool)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new MissingReferenceException(
                    "Treasure prototype was not found: " + prefabPath);
            }

            TreasureDefinition definition =
                AssetDatabase.LoadAssetAtPath<TreasureDefinition>(
                    definitionPath);
            if (definition == null)
            {
                definition =
                    ScriptableObject.CreateInstance<TreasureDefinition>();
                AssetDatabase.CreateAsset(definition, definitionPath);
            }

            definition.Configure(
                prefab,
                value,
                weight,
                spawnChance,
                attemptsPerChunk,
                12f,
                fragility);
            definition.ConfigureDisplayName(displayName);
            definition.ConfigureDestructionExplosion(
                destructionExplosionTool);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void UpdateSpawnTable(
            params TreasureDefinition[] additions)
        {
            TreasureSpawnTable table =
                AssetDatabase.LoadAssetAtPath<TreasureSpawnTable>(
                    ProjectAssetPaths.Config.TreasureSpawnTable);
            if (table == null)
            {
                throw new MissingReferenceException(
                    "Treasure spawn table was not found.");
            }

            var treasures =
                new List<TreasureDefinition>(table.Treasures);
            for (int i = 0; i < additions.Length; i++)
            {
                TreasureDefinition addition = additions[i];
                if (addition != null && !treasures.Contains(addition))
                {
                    treasures.Add(addition);
                }
            }
            table.Configure(treasures, table.SpawnExclusionRadius);
            EditorUtility.SetDirty(table);
        }

        private static void NormalizeStatuePrefab()
        {
            string path = ProjectAssetPaths.Prefabs.StatueTreasure;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                MeshFilter sourceFilter = root.GetComponent<MeshFilter>();
                MeshRenderer sourceRenderer =
                    root.GetComponent<MeshRenderer>();
                if (sourceFilter == null || sourceRenderer == null)
                {
                    return;
                }

                var model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = root.transform.localPosition;
                model.transform.localRotation = root.transform.localRotation;
                model.transform.localScale = root.transform.localScale;

                MeshFilter filter = model.AddComponent<MeshFilter>();
                EditorUtility.CopySerialized(sourceFilter, filter);
                MeshRenderer renderer = model.AddComponent<MeshRenderer>();
                EditorUtility.CopySerialized(sourceRenderer, renderer);

                Collider sourceCollider = root.GetComponent<Collider>();
                if (sourceCollider != null)
                {
                    Collider collider = model.AddComponent(
                        sourceCollider.GetType()) as Collider;
                    if (collider != null)
                    {
                        EditorUtility.CopySerialized(
                            sourceCollider,
                            collider);
                    }
                    Object.DestroyImmediate(sourceCollider);
                }
                Object.DestroyImmediate(sourceRenderer);
                Object.DestroyImmediate(sourceFilter);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureSphinxPrefab()
        {
            string path = ProjectAssetPaths.Prefabs.SphinxTreasure;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Renderer[] renderers =
                    root.GetComponentsInChildren<Renderer>(true);
                if (!TryCalculateWorldBounds(
                        renderers,
                        out Bounds worldBounds)
                    || worldBounds.size.y <= 0.0001f)
                {
                    throw new MissingReferenceException(
                        "Sphinx prototype has no renderable bounds.");
                }

                float scaleFactor =
                    SphinxTargetHeight / worldBounds.size.y;
                root.transform.localScale *= scaleFactor;
                renderers =
                    root.GetComponentsInChildren<Renderer>(true);
                TryCalculateLocalBounds(
                    root.transform,
                    renderers,
                    out Bounds localBounds);
                BoxCollider collider = root.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    collider = root.AddComponent<BoxCollider>();
                }
                collider.center = localBounds.center;
                collider.size = localBounds.size;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool TryCalculateWorldBounds(
            IReadOnlyList<Renderer> renderers,
            out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private static bool TryCalculateLocalBounds(
            Transform root,
            IReadOnlyList<Renderer> renderers,
            out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }
                Bounds rendererBounds = renderer.bounds;
                Vector3 minimum = rendererBounds.min;
                Vector3 maximum = rendererBounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldPoint = new Vector3(
                        (corner & 1) == 0 ? minimum.x : maximum.x,
                        (corner & 2) == 0 ? minimum.y : maximum.y,
                        (corner & 4) == 0 ? minimum.z : maximum.z);
                    Vector3 localPoint =
                        root.InverseTransformPoint(worldPoint);
                    if (!found)
                    {
                        bounds = new Bounds(localPoint, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localPoint);
                    }
                }
            }
            return found;
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
    }
}
