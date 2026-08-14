using System;
using System.IO;
using Supernova.Gameplay;
using Supernova.Voxels;
using Supernova.WorldGeneration;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Supernova.Voxels.Editor
{
    [InitializeOnLoad]
    public static class SpawnShelterStoneTestSceneBuilder
    {
        private const string MenuPath =
            "Tools/Supernova/Voxels/Rebuild SpawnShelter Stone Test Scene";
        private const string UpgradeMenuPath =
            "Tools/Supernova/Voxels/Refresh SpawnShelter Weapon Pickups";
        private const string WeaponPickupRootName = "Weapon Pickups";
        private const string TryMorePropsText = "Try more props!";
        private static bool waitingForEditMode;

        static SpawnShelterStoneTestSceneBuilder()
        {
            EditorApplication.delayCall += CreateSceneWhenMissing;
        }

        [MenuItem(MenuPath)]
        public static void RebuildScene()
        {
            BuildScene();
        }

        [MenuItem(UpgradeMenuPath)]
        public static void UpgradeScene()
        {
            UpgradeExistingScene();
        }

        private static void CreateSceneWhenMissing()
        {
            string absolutePath =
                ProjectAssetPaths.ToAbsoluteFileSystemPath(
                    ProjectAssetPaths.Scenes.SpawnShelterStoneTest);
            bool sceneExists = File.Exists(absolutePath);
            if (sceneExists
                && SceneHasConfiguredPlayer()
                && SceneHasWeaponPickups())
            {
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!waitingForEditMode)
                {
                    waitingForEditMode = true;
                    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                }
                return;
            }

            if (sceneExists && SceneHasConfiguredPlayer())
                UpgradeExistingScene();
            else
                BuildScene();
        }

        private static bool SceneHasConfiguredPlayer()
        {
            string playerGuid = AssetDatabase.AssetPathToGUID(
                ProjectAssetPaths.Prefabs.Player);
            string absoluteScenePath =
                ProjectAssetPaths.ToAbsoluteFileSystemPath(
                    ProjectAssetPaths.Scenes.SpawnShelterStoneTest);
            return !string.IsNullOrEmpty(playerGuid)
                && File.ReadAllText(absoluteScenePath).Contains(playerGuid);
        }

        private static bool SceneHasWeaponPickups()
        {
            string absoluteScenePath =
                ProjectAssetPaths.ToAbsoluteFileSystemPath(
                    ProjectAssetPaths.Scenes.SpawnShelterStoneTest);
            return File.ReadAllText(absoluteScenePath).Contains(
                "m_Name: " + WeaponPickupRootName);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            waitingForEditMode = false;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.delayCall += CreateSceneWhenMissing;
        }

        private static void BuildScene()
        {
            VoxelStructureAsset structure =
                AssetDatabase.LoadAssetAtPath<VoxelStructureAsset>(
                    ProjectAssetPaths.Structures.SpawnShelter);
            DenseJigsawWorldConfiguration denseConfiguration =
                AssetDatabase.LoadAssetAtPath<DenseJigsawWorldConfiguration>(
                    ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
            if (structure == null)
            {
                throw new InvalidOperationException(
                    "SpawnShelter could not be loaded from the global path table.");
            }
            if (denseConfiguration == null
                || denseConfiguration.InfiniteCavesLevelSource == null)
            {
                throw new InvalidOperationException(
                    "DenseJigsawRegion shared runtime configuration could not be "
                    + "loaded from the global path table.");
            }

            EnsureFolder(ProjectAssetPaths.Folders.TestScenes);
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = FindLoadedTestScene();
            bool createdTemporaryScene = !scene.IsValid();
            if (createdTemporaryScene)
            {
                scene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Additive);
            }
            else
            {
                GameObject[] existingRoots = scene.GetRootGameObjects();
                for (int i = 0; i < existingRoots.Length; i++)
                {
                    UnityEngine.Object.DestroyImmediate(existingRoots[i]);
                }
            }
            scene.name = "SpawnShelterStoneTest";
            SceneManager.SetActiveScene(scene);

            try
            {
                var worldObject = new GameObject("SpawnShelter Minimal Stone World");
                VoxelStructureStoneTestWorld testWorld =
                    worldObject.AddComponent<VoxelStructureStoneTestWorld>();
                float runtimeVoxelSize = denseConfiguration
                    .InfiniteCavesGenerationSource.VoxelSize;
                worldObject.transform.localScale = Vector3.one
                    / Mathf.Max(0.01f, runtimeVoxelSize);
                testWorld.Configure(
                    structure,
                    denseConfiguration.InfiniteCavesLevelSource);
                testWorld.Rebuild();

                GameObject player = CreatePlayer(structure);
                ConfigurePlayerSession(player);
                CreateWeaponPickups(scene, testWorld, structure);
                CreateLighting();

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(
                        scene,
                        ProjectAssetPaths.Scenes.SpawnShelterStoneTest))
                {
                    throw new InvalidOperationException(
                        "Failed to save the SpawnShelter Stone test scene.");
                }
            }
            finally
            {
                if (previousActiveScene.IsValid()
                    && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
                if (createdTemporaryScene && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            AssetDatabase.Refresh();
            Vector3Int chunkGrid =
                VoxelStructureStoneTestWorld.CalculateMinimumChunkGrid(
                    structure.Size);
            Vector3Int worldSize =
                VoxelStructureStoneTestWorld.GetWorldVoxelSize(chunkGrid);
            Debug.Log(
                "Created SpawnShelter Stone test scene at "
                + ProjectAssetPaths.Scenes.SpawnShelterStoneTest
                + $". Layout: {chunkGrid.x}x{chunkGrid.z} columns, "
                + $"{chunkGrid.y} vertical section(s), "
                + $"{worldSize.x}x{worldSize.y}x{worldSize.z} voxels.");
        }

        private static Scene FindLoadedTestScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (loaded.path == ProjectAssetPaths.Scenes.SpawnShelterStoneTest)
                {
                    return loaded;
                }
            }
            return default;
        }

        private static GameObject CreatePlayer(VoxelStructureAsset structure)
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);
            if (playerPrefab == null)
            {
                throw new InvalidOperationException(
                    "The configured Player prefab could not be loaded from the "
                    + "global path table.");
            }

            GameObject player =
                (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
            if (player.GetComponent<VoxelPlayerController>() == null)
            {
                UnityEngine.Object.DestroyImmediate(player);
                throw new InvalidOperationException(
                    "SpawnShelter test scene requires the configured Player "
                    + "prefab with VoxelPlayerController.");
            }
            player.transform.position = structure.GetPlayerSpawnVoxel(
                structure.Anchor,
                Vector3Int.zero);
            player.transform.rotation = Quaternion.identity;
            return player;
        }

        private static void UpgradeExistingScene()
        {
            VoxelStructureAsset structure =
                AssetDatabase.LoadAssetAtPath<VoxelStructureAsset>(
                    ProjectAssetPaths.Structures.SpawnShelter);
            if (structure == null)
            {
                throw new InvalidOperationException(
                    "SpawnShelter could not be loaded from the global path table.");
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = FindLoadedTestScene();
            bool openedForUpgrade = !scene.IsValid();
            if (openedForUpgrade)
            {
                scene = EditorSceneManager.OpenScene(
                    ProjectAssetPaths.Scenes.SpawnShelterStoneTest,
                    OpenSceneMode.Additive);
            }
            SceneManager.SetActiveScene(scene);

            try
            {
                GameObject player = FindScenePlayer(scene);
                if (player == null)
                {
                    throw new InvalidOperationException(
                        "SpawnShelter Stone test scene has no configured player.");
                }

                ConfigurePlayerSession(player);
                VoxelStructureStoneTestWorld testWorld = FindSceneWorld(scene);
                if (testWorld == null)
                {
                    throw new InvalidOperationException(
                        "SpawnShelter Stone test scene has no configured voxel world.");
                }
                DenseJigsawWorldConfiguration denseConfiguration =
                    AssetDatabase.LoadAssetAtPath<DenseJigsawWorldConfiguration>(
                        ProjectAssetPaths.Config
                            .DenseJigsawRegionWorldGeneration);
                if (denseConfiguration == null
                    || denseConfiguration.InfiniteCavesLevelSource == null)
                {
                    throw new InvalidOperationException(
                        "DenseJigsawRegion shared runtime configuration is missing.");
                }
                testWorld.transform.localScale = Vector3.one
                    / Mathf.Max(
                        0.01f,
                        denseConfiguration.InfiniteCavesGenerationSource
                            .VoxelSize);
                testWorld.Configure(
                    structure,
                    denseConfiguration.InfiniteCavesLevelSource);
                testWorld.Rebuild();
                DestroySceneRoot(scene, WeaponPickupRootName);
                CreateWeaponPickups(scene, testWorld, structure);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new InvalidOperationException(
                        "Failed to save the upgraded SpawnShelter Stone test scene.");
                }
            }
            finally
            {
                if (previousActiveScene.IsValid()
                    && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
                if (openedForUpgrade && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static GameObject FindScenePlayer(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                PlayerToolController controller =
                    roots[i].GetComponentInChildren<PlayerToolController>(true);
                if (controller != null)
                    return controller.gameObject;
            }
            return null;
        }

        private static VoxelStructureStoneTestWorld FindSceneWorld(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                VoxelStructureStoneTestWorld testWorld =
                    roots[i].GetComponentInChildren<
                        VoxelStructureStoneTestWorld>(true);
                if (testWorld != null)
                    return testWorld;
            }
            return null;
        }

        private static void DestroySceneRoot(Scene scene, string rootName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == rootName)
                    UnityEngine.Object.DestroyImmediate(roots[i]);
            }
        }

        private static void ConfigurePlayerSession(GameObject player)
        {
            PlayerInventorySessionSettings settings =
                player.GetComponent<PlayerInventorySessionSettings>();
            if (settings == null)
                settings = player.AddComponent<PlayerInventorySessionSettings>();
            settings.ConfigurePickaxeOnly();
            EditorUtility.SetDirty(settings);
        }

        private static void CreateWeaponPickups(
            Scene scene,
            VoxelStructureStoneTestWorld testWorld,
            VoxelStructureAsset structure)
        {
            string[] definitionPaths =
            {
                ProjectAssetPaths.Config.BombTool,
                ProjectAssetPaths.Config.SmgTool,
                ProjectAssetPaths.Config.SolidGunTool,
                ProjectAssetPaths.Config.PortalGunTool,
            };
            int[] xOffsets = { -6, -2, 3, 7 };
            var root = new GameObject(WeaponPickupRootName);
            Transform roomMarker = FindTryMorePropsMarker(scene);
            Vector3Int markerVoxel = roomMarker != null
                ? testWorld.WorldPositionToVoxel(roomMarker.position)
                : new Vector3Int(
                    structure.Size.x / 2,
                    structure.Size.y - 2,
                    structure.Size.z - 2);

            for (int i = 0; i < definitionPaths.Length; i++)
            {
                PlayerToolDefinition definition =
                    AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                        definitionPaths[i]);
                if (definition == null
                    || (!definition.IsFirearm
                        && definition.Item != PlayerInventoryItem.Bomb))
                {
                    throw new InvalidOperationException(
                        $"Weapon definition '{definitionPaths[i]}' is missing "
                        + "or is not configured as a supported pickup.");
                }

                Vector3 position = FindTryMorePropsRoomFloor(
                    structure,
                    testWorld,
                    markerVoxel,
                    xOffsets[i]);
                CreateWeaponPickup(root.transform, definition, position);
            }
        }

        private static Transform FindTryMorePropsMarker(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                TMP_Text[] labels =
                    roots[i].GetComponentsInChildren<TMP_Text>(true);
                for (int labelIndex = 0;
                    labelIndex < labels.Length;
                    labelIndex++)
                {
                    TMP_Text label = labels[labelIndex];
                    if (label != null
                        && string.Equals(
                            label.text?.Trim(),
                            TryMorePropsText,
                            StringComparison.Ordinal))
                    {
                        return label.transform;
                    }
                }
            }
            return null;
        }

        private static Vector3 FindTryMorePropsRoomFloor(
            VoxelStructureAsset structure,
            VoxelStructureStoneTestWorld testWorld,
            Vector3Int markerVoxel,
            int layoutXOffset)
        {
            int targetX = Mathf.Clamp(
                markerVoxel.x + layoutXOffset,
                1,
                structure.Size.x - 2);
            int targetZ = Mathf.Clamp(
                markerVoxel.z - 2,
                1,
                structure.Size.z - 2);

            for (int radius = 0; radius <= 4; radius++)
            {
                for (int zOffset = -radius; zOffset <= radius; zOffset++)
                {
                    for (int xOffset = -radius; xOffset <= radius; xOffset++)
                    {
                        int x = Mathf.Clamp(
                            targetX + xOffset,
                            1,
                            structure.Size.x - 2);
                        int z = Mathf.Clamp(
                            targetZ + zOffset,
                            1,
                            structure.Size.z - 2);
                        if (TryFindWalkableFloor(structure, x, z, out float y))
                        {
                            Vector3 localPosition =
                                new Vector3(x, y, z) * testWorld.VoxelSize;
                            return testWorld.transform.TransformPoint(
                                localPosition);
                        }
                    }
                }
            }

            throw new InvalidOperationException(
                "Could not find a walkable floor near 'Try more props!'.");
        }

        private static bool TryFindWalkableFloor(
            VoxelStructureAsset structure,
            int x,
            int z,
            out float floorY)
        {
            for (int y = structure.Size.y - 3; y >= 1; y--)
            {
                bool solidBelow = structure.GetSample(x, y - 1, z).Density >= 0f;
                bool clear = structure.GetSample(x, y, z).Density < 0f
                    && structure.GetSample(x, y + 1, z).Density < 0f
                    && structure.GetSample(x, y + 2, z).Density < 0f;
                if (!solidBelow || !clear)
                    continue;

                floorY = y - 0.5f;
                return true;
            }

            floorY = 0f;
            return false;
        }

        private static void CreateWeaponPickup(
            Transform parent,
            PlayerToolDefinition definition,
            Vector3 position)
        {
            string label = GetWeaponLabel(definition.Item);
            var pickupObject = new GameObject(label + " Pickup");
            pickupObject.transform.SetParent(parent, false);
            pickupObject.transform.position = position;

            GameObject pedestal = GameObject.CreatePrimitive(
                PrimitiveType.Cylinder);
            pedestal.name = "Pedestal";
            pedestal.transform.SetParent(pickupObject.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            pedestal.transform.localScale = new Vector3(0.7f, 0.08f, 0.7f);
            Collider pedestalCollider = pedestal.GetComponent<Collider>();
            if (pedestalCollider != null)
                UnityEngine.Object.DestroyImmediate(pedestalCollider);

            var displayObject = new GameObject("Weapon Display");
            displayObject.transform.SetParent(pickupObject.transform, false);
            displayObject.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            GameObject modelPrefab = ResolvePickupModel(definition);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(
                modelPrefab);
            model.name = modelPrefab.name;
            model.transform.SetParent(displayObject.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            CenterAndScaleVisual(displayObject.transform, model);

            var lightObject = new GameObject("Pickup Light");
            lightObject.transform.SetParent(pickupObject.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.15f, 0.75f, 1f);
            light.range = 2.5f;
            light.intensity = 0.8f;
            light.shadows = LightShadows.None;

            var promptObject = new GameObject("Pickup Prompt");
            promptObject.transform.SetParent(pickupObject.transform, false);
            promptObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            TextMesh prompt = promptObject.AddComponent<TextMesh>();
            prompt.text = "E  PICK UP\n" + label;
            prompt.anchor = TextAnchor.MiddleCenter;
            prompt.alignment = TextAlignment.Center;
            prompt.fontSize = 64;
            prompt.characterSize = 0.025f;
            prompt.color = new Color(0.45f, 0.9f, 1f);

            WeaponPickup pickup = pickupObject.AddComponent<WeaponPickup>();
            pickup.Configure(
                definition,
                displayObject.transform,
                promptObject);
        }

        private static GameObject ResolvePickupModel(
            PlayerToolDefinition definition)
        {
            if (definition.HeldModelPrefab != null)
                return definition.HeldModelPrefab;

            string fallbackPath = definition.Item == PlayerInventoryItem.Gun
                || definition.Item == PlayerInventoryItem.SMG
                    ? ProjectAssetPaths.Prefabs.Smg
                    : ProjectAssetPaths.Prefabs.SolidGun;
            GameObject fallback = AssetDatabase.LoadAssetAtPath<GameObject>(
                fallbackPath);
            if (fallback == null)
            {
                throw new InvalidOperationException(
                    $"No pickup model is available for {definition.Item}.");
            }
            return fallback;
        }

        private static void CenterAndScaleVisual(
            Transform displayRoot,
            GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 localCenter = displayRoot.InverseTransformPoint(bounds.center);
            model.transform.localPosition -= localCenter;
            float maximumSize = Mathf.Max(
                bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            if (maximumSize > 0.001f)
            {
                displayRoot.localScale =
                    Vector3.one * Mathf.Min(1.4f / maximumSize, 2f);
            }
        }

        private static string GetWeaponLabel(PlayerInventoryItem item)
        {
            switch (item)
            {
                case PlayerInventoryItem.Gun:
                    return "RIFLE";
                case PlayerInventoryItem.SMG:
                    return "SMG";
                case PlayerInventoryItem.SolidGun:
                    return "SOLID GUN";
                case PlayerInventoryItem.PortalGun:
                    return "PORTAL GUN";
                default:
                    return item.ToString().ToUpperInvariant();
            }
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.18f, 0.2f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.1f, 0.12f);
            RenderSettings.ambientGroundColor =
                new Color(0.025f, 0.025f, 0.03f);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)
                ?.Replace(Path.DirectorySeparatorChar, '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException(
                    $"Cannot create asset folder '{path}'.");
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
