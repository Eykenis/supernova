using System;
using System.Linq;
using Supernova.MinecraftCaves;
using Supernova.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Supernova.Voxels.Editor
{
    [CustomEditor(typeof(VoxelStructureAuthoring))]
    public sealed class VoxelStructureAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            var authoring = (VoxelStructureAuthoring)target;
            DrawFeatureTemplateControls(authoring);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load Structure"))
                {
                    LoadStructure(authoring);
                }
                if (GUILayout.Button("Save Structure"))
                {
                    SaveStructure(authoring);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Voxel At Anchor"))
                {
                    CreateCell(
                        authoring,
                        authoring.Anchor,
                        authoring.PaintType,
                        authoring.PaintDensity,
                        true);
                }
                if (GUILayout.Button("Clear Voxels"))
                {
                    ClearCells(authoring);
                }
            }
            DrawSocketControls(authoring);
        }

        /// <summary>
        /// Template sockets travel with the asset, so a jigsaw piece that uses
        /// the template inherits them instead of restating each connection.
        /// </summary>
        private static void DrawSocketControls(VoxelStructureAuthoring authoring)
        {
            VoxelStructureAsset asset = authoring.StructureToEdit;
            if (asset == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Jigsaw Template Sockets",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Authored sockets: {asset.Sockets.Count}");
            if (asset.Sockets.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Without sockets this template can only be used as a start piece or by a piece that authors its own connectors.",
                    MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Socket At Anchor"))
                {
                    Undo.RecordObject(asset, "Add Template Socket");
                    var socket = new VoxelStructureSocket();
                    socket.Configure(
                        $"socket_{asset.Sockets.Count}",
                        authoring.Anchor,
                        JigsawConnectorDefinition.Face.Forward,
                        JigsawConnectorDefinition.Role.Bidirectional,
                        "*",
                        "*",
                        "main");
                    asset.AddSocket(socket);
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
                }
                using (new EditorGUI.DisabledScope(asset.Sockets.Count == 0))
                {
                    if (GUILayout.Button("Clear Sockets"))
                    {
                        Undo.RecordObject(asset, "Clear Template Sockets");
                        asset.SetSockets(null);
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssets();
                    }
                }
            }
        }

        /// <summary>Draws each template socket and the wall it faces.</summary>
        private void OnSceneGUI()
        {
            var authoring = (VoxelStructureAuthoring)target;
            DrawSocketHandles(authoring);
            HandlePaintInput(authoring);
        }

        private static void DrawSocketHandles(VoxelStructureAuthoring authoring)
        {
            VoxelStructureAsset asset = authoring.StructureToEdit;
            if (asset == null)
            {
                return;
            }
            for (int i = 0; i < asset.Sockets.Count; i++)
            {
                VoxelStructureSocket socket = asset.Sockets[i];
                if (socket == null)
                {
                    continue;
                }
                Vector3 world = authoring.transform.TransformPoint(
                    socket.LocalPosition);
                Handles.color = new Color(0.2f, 0.9f, 1f, 0.85f);
                Handles.DrawWireCube(world, Vector3.one);
                Handles.Label(world + Vector3.up, socket.StableId);
                Handles.ArrowHandleCap(
                    0,
                    world,
                    Quaternion.LookRotation(
                        FaceDirection(socket.Face),
                        Vector3.up),
                    1.5f,
                    EventType.Repaint);
            }
        }

        private static Vector3 FaceDirection(
            JigsawConnectorDefinition.Face face)
        {
            switch (face)
            {
                case JigsawConnectorDefinition.Face.Right:
                    return Vector3.right;
                case JigsawConnectorDefinition.Face.Back:
                    return Vector3.back;
                case JigsawConnectorDefinition.Face.Left:
                    return Vector3.left;
                default:
                    return Vector3.forward;
            }
        }

        private static void HandlePaintInput(VoxelStructureAuthoring authoring)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown
                || current.button != 0
                || (!current.shift && !current.control))
            {
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 10000f))
            {
                return;
            }

            VoxelStructureCellAuthoring hitCell =
                hit.collider.GetComponent<VoxelStructureCellAuthoring>();
            if (hitCell == null
                || !hitCell.transform.IsChildOf(authoring.transform))
            {
                return;
            }

            if (current.control)
            {
                Undo.DestroyObjectImmediate(hitCell.gameObject);
            }
            else
            {
                Vector3 local = authoring.transform.InverseTransformPoint(
                    hit.point + hit.normal * 0.51f);
                var coordinate = new Vector3Int(
                    Mathf.RoundToInt(local.x),
                    Mathf.RoundToInt(local.y),
                    Mathf.RoundToInt(local.z));
                CreateCell(
                    authoring,
                    coordinate,
                    authoring.PaintType,
                    authoring.PaintDensity,
                    true);
            }

            current.Use();
            EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
        }

        private static void DrawFeatureTemplateControls(
            VoxelStructureAuthoring authoring)
        {
            VoxelStructureFeatureDefinition feature =
                authoring.StructureFeatureToEdit;
            if (feature == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Random Structure Feature", EditorStyles.boldLabel);
            if (feature.StructureTemplate == null)
            {
                EditorGUILayout.HelpBox(
                    "The selected feature has no editable voxel template. Assign the current structure to connect it.",
                    MessageType.Warning);
            }
            else if (authoring.StructureToEdit != feature.StructureTemplate)
            {
                EditorGUILayout.HelpBox(
                    "Structure To Edit is not the template used by the selected random feature.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Saving this structure updates the template used by random world generation.",
                    MessageType.None);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(feature.StructureTemplate == null))
                {
                    if (GUILayout.Button("Use Feature Template"))
                    {
                        Undo.RecordObject(authoring, "Use Structure Feature Template");
                        authoring.ConfigureFeature(feature, authoring.TypeCatalog);
                        LoadStructure(authoring);
                    }
                }
                using (new EditorGUI.DisabledScope(authoring.StructureToEdit == null))
                {
                    if (GUILayout.Button("Assign Current To Feature"))
                    {
                        Undo.RecordObject(feature, "Assign Structure Feature Template");
                        feature.SetStructureTemplate(authoring.StructureToEdit);
                        EditorUtility.SetDirty(feature);
                        AssetDatabase.SaveAssets();
                    }
                }
            }
            EditorGUILayout.Space();
        }

        internal static void LoadStructure(VoxelStructureAuthoring authoring)
        {
            VoxelStructureAsset asset = authoring.StructureToEdit;
            if (asset == null)
            {
                Debug.LogWarning("Assign a VoxelStructureAsset before loading.", authoring);
                return;
            }

            ClearCells(authoring);
            authoring.Configure(
                asset,
                authoring.TypeCatalog,
                asset.Size,
                asset.Anchor,
                asset.PlayerSpawnOffset);

            for (int z = 0; z < asset.Size.z; z++)
            {
                for (int y = 0; y < asset.Size.y; y++)
                {
                    for (int x = 0; x < asset.Size.x; x++)
                    {
                        VoxelSample sample = asset.GetSample(x, y, z);
                        if (sample.Density < 0f)
                        {
                            continue;
                        }

                        CreateCell(
                            authoring,
                            new Vector3Int(x, y, z),
                            sample.Type,
                            sample.Density,
                            false);
                    }
                }
            }

            EditorUtility.SetDirty(authoring);
            EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
        }

        internal static void SaveStructure(VoxelStructureAuthoring authoring)
        {
            if (!authoring.TryBuildData(
                    out float[] densities,
                    out ushort[] types,
                    out string error))
            {
                EditorUtility.DisplayDialog("Cannot Save Structure", error, "OK");
                return;
            }

            VoxelStructureAsset asset = authoring.StructureToEdit;
            if (asset == null)
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "Save Voxel Structure",
                    authoring.DefaultAssetName,
                    "asset",
                    "Choose a persistent asset location.");
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                asset = CreateInstance<VoxelStructureAsset>();
                AssetDatabase.CreateAsset(asset, path);
            }

            asset.SetData(
                authoring.Size,
                authoring.Anchor,
                authoring.PlayerSpawnOffset,
                densities,
                types);
            authoring.Configure(
                asset,
                authoring.TypeCatalog,
                asset.Size,
                asset.Anchor,
                asset.PlayerSpawnOffset);
            VoxelStructureFeatureDefinition feature =
                authoring.StructureFeatureToEdit;
            if (feature != null && feature.StructureTemplate != asset)
            {
                feature.SetStructureTemplate(asset);
                EditorUtility.SetDirty(feature);
            }
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(authoring);
            AssetDatabase.SaveAssets();
        }

        internal static VoxelStructureCellAuthoring CreateCell(
            VoxelStructureAuthoring authoring,
            Vector3Int coordinate,
            VoxelTypeId type,
            float density,
            bool registerUndo)
        {
            if (!authoring.IsInBounds(coordinate))
            {
                Debug.LogWarning(
                    $"Voxel coordinate {coordinate} is outside {authoring.Size}.",
                    authoring);
                return null;
            }

            VoxelStructureCellAuthoring[] existing =
                authoring.GetComponentsInChildren<VoxelStructureCellAuthoring>(false);
            foreach (VoxelStructureCellAuthoring cell in existing)
            {
                Vector3Int cellCoordinate = Vector3Int.RoundToInt(cell.transform.localPosition);
                if (cellCoordinate == coordinate)
                {
                    return cell;
                }
            }

            GameObject cellObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cellObject.name = $"Voxel_{coordinate.x}_{coordinate.y}_{coordinate.z}";
            if (registerUndo)
            {
                Undo.RegisterCreatedObjectUndo(cellObject, "Add Structure Voxel");
            }
            cellObject.transform.SetParent(authoring.transform, false);
            cellObject.transform.localPosition = coordinate;
            cellObject.transform.localScale = Vector3.one * 0.92f;
            VoxelStructureCellAuthoring cellAuthoring =
                cellObject.AddComponent<VoxelStructureCellAuthoring>();
            cellAuthoring.Configure(density, type);

            VoxelTypeDefinition definition =
                authoring.TypeCatalog != null ? authoring.TypeCatalog.Find(type) : null;
            if (definition != null && definition.Material != null)
            {
                cellObject.GetComponent<MeshRenderer>().sharedMaterial = definition.Material;
            }
            return cellAuthoring;
        }

        internal static void ClearCells(VoxelStructureAuthoring authoring)
        {
            VoxelStructureCellAuthoring[] cells =
                authoring.GetComponentsInChildren<VoxelStructureCellAuthoring>(true);
            for (int i = cells.Length - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(cells[i].gameObject);
            }
            EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
        }
    }

    public static class VoxelStructureWorkflowBuilder
    {
        private const string CatalogPath =
            ProjectAssetPaths.Config.VoxelCatalog;
        private const string VoxelTypeFolder =
            ProjectAssetPaths.Folders.VoxelTypes;
        private const string DefaultVoxelTypePath =
            VoxelTypeFolder + "/Default.asset";
        private const string StoneVoxelTypePath =
            VoxelTypeFolder + "/Stone.asset";
        private const string OreVoxelTypePath =
            VoxelTypeFolder + "/Ore.asset";
        private const string OreFeatureFolder =
            ProjectAssetPaths.Folders.OreFeatures;
        private const string OreFeaturePath =
            OreFeatureFolder + "/Ore.asset";
        private const string StructurePath =
            ProjectAssetPaths.Structures.SpawnShelter;
        private const string WorldGenerationPath =
            ProjectAssetPaths.Config.WorldGeneration;
        private const string AuthoringScenePath =
            ProjectAssetPaths.Scenes.VoxelStructureEditor;
        private const string InfiniteScenePath =
            ProjectAssetPaths.Scenes.InfiniteCaves;

        [MenuItem("Tools/Supernova/Voxels/Build Fixed Structure Workflow")]
        public static void Build()
        {
            EnsureFolder(ProjectAssetPaths.Folders.Game, "Config");
            EnsureFolder(ProjectAssetPaths.Folders.Config, "VoxelTypes");
            EnsureFolder(ProjectAssetPaths.Folders.Config, "OreFeatures");
            EnsureFolder(ProjectAssetPaths.Folders.Config, "Worlds");
            EnsureFolder(ProjectAssetPaths.Folders.Game, "Structures");

            VoxelTypeDefinition[] definitions =
            {
                EnsureVoxelTypeDefinition(
                    DefaultVoxelTypePath,
                    1,
                    "Default",
                    1),
                EnsureVoxelTypeDefinition(
                    StoneVoxelTypePath,
                    2,
                    "Stone",
                    4),
                EnsureVoxelTypeDefinition(
                    OreVoxelTypePath,
                    3,
                    "Ore",
                    8),
            };
            VoxelTypeCatalog catalog =
                AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var catalogDefinitions = new System.Collections.Generic.List<
                VoxelTypeDefinition>(catalog.Definitions);
            for (int i = 0; i < definitions.Length; i++)
            {
                VoxelTypeDefinition replacement = definitions[i];
                catalogDefinitions.RemoveAll(
                    item => item == null || item.TypeId == replacement.TypeId);
                catalogDefinitions.Add(replacement);
            }
            catalog.SetDefinitions(
                catalogDefinitions.OrderBy(item => item.TypeId.Value));
            EditorUtility.SetDirty(catalog);

            MinecraftCaves.VoxelOreFeatureDefinition oreFeature =
                EnsureOreFeature(definitions[2], definitions[1]);
            VoxelStructureAsset structure =
                AssetDatabase.LoadAssetAtPath<VoxelStructureAsset>(StructurePath);
            if (structure == null)
            {
                structure = ScriptableObject.CreateInstance<VoxelStructureAsset>();
                CreateDefaultSpawnShelter(structure);
                AssetDatabase.CreateAsset(structure, StructurePath);
            }

            AssetDatabase.SaveAssets();
            CreateAuthoringScene(catalog, structure);
            ConfigureInfiniteCaves(catalog, structure, oreFeature);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"Fixed voxel structure workflow ready. Edit {AuthoringScenePath} "
                 + $"and save into {StructurePath}.");
        }

        private static VoxelTypeDefinition EnsureVoxelTypeDefinition(
            string path,
            ushort type,
            string displayName,
            int durability)
        {
            VoxelTypeDefinition definition =
                AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(path);
            if (definition != null)
            {
                return definition;
            }

            definition = ScriptableObject.CreateInstance<VoxelTypeDefinition>();
            definition.Configure(type, displayName, durability);
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        private static MinecraftCaves.VoxelOreFeatureDefinition EnsureOreFeature(
            VoxelTypeDefinition ore,
            VoxelTypeDefinition stone)
        {
            MinecraftCaves.VoxelOreFeatureDefinition feature =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftCaves.VoxelOreFeatureDefinition>(OreFeaturePath);
            if (feature != null)
            {
                return feature;
            }

            feature = ScriptableObject.CreateInstance<
                MinecraftCaves.VoxelOreFeatureDefinition>();
            feature.Configure(
                ore,
                new[] { stone },
                3109,
                8,
                1f,
                MinecraftCaves.MinecraftOreFeatureSettings.HeightDistribution
                    .Trapezoid,
                -64,
                64,
                0,
                8,
                0.5f);
            AssetDatabase.CreateAsset(feature, OreFeaturePath);
            return feature;
        }

        private static void CreateDefaultSpawnShelter(VoxelStructureAsset asset)
        {
            var size = new Vector3Int(11, 6, 11);
            var anchor = new Vector3Int(5, 1, 5);
            int count = size.x * size.y * size.z;
            var densities = new float[count];
            var types = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                densities[i] = -1f;
            }

            for (int z = 0; z < size.z; z++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        bool floor = y == 0;
                        bool roof = y == size.y - 1;
                        bool wall = x == 0 || x == size.x - 1
                            || z == 0 || z == size.z - 1;
                        bool doorway = (z == 0 || z == size.z - 1)
                            && x >= anchor.x - 1
                            && x <= anchor.x + 1
                            && y >= 1
                            && y <= 3;
                        if ((floor || roof || wall) && !doorway)
                        {
                            int index = x + size.x * (y + size.y * z);
                            densities[index] = 1f;
                            types[index] = 2;
                        }
                    }
                }
            }

            asset.SetData(
                size,
                anchor,
                new Vector3(0f, 1.25f, 0f),
                densities,
                types);
            EditorUtility.SetDirty(asset);
        }

        private static void CreateAuthoringScene(
            VoxelTypeCatalog catalog,
            VoxelStructureAsset structure)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            // A single-scene switch can invalidate transient object handles while
            // newly created assets are still being integrated. Reload persistent
            // references before authoring any scene objects.
            catalog = AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(CatalogPath);
            structure = AssetDatabase.LoadAssetAtPath<VoxelStructureAsset>(StructurePath);
            if (catalog == null || structure == null)
            {
                throw new InvalidOperationException(
                    "Voxel type catalog or spawn structure could not be reloaded.");
            }

            var authoringObject = new GameObject("Voxel Structure Authoring");
            VoxelStructureAuthoring authoring =
                authoringObject.AddComponent<VoxelStructureAuthoring>();
            authoring.Configure(
                structure,
                catalog,
                structure.Size,
                structure.Anchor,
                structure.PlayerSpawnOffset);
            VoxelStructureAuthoringEditor.LoadStructure(authoring);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            VoxelStructurePlayModeEditor playModeEditor =
                cameraObject.AddComponent<VoxelStructurePlayModeEditor>();
            playModeEditor.Configure(authoring, camera);
            cameraObject.transform.position = new Vector3(16f, 12f, -16f);
            cameraObject.transform.LookAt(
                ((Vector3)structure.Size - Vector3.one) * 0.5f);
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.05f;

            var hudObject = new GameObject("Game HUD");
            GameHudController hud = hudObject.AddComponent<GameHudController>();
            hud.RebuildDefaultView();

            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Selection.activeGameObject = authoringObject;
            EditorSceneManager.SaveScene(scene, AuthoringScenePath);
        }

        private static void ConfigureInfiniteCaves(
            VoxelTypeCatalog catalog,
            VoxelStructureAsset structure,
            MinecraftCaves.VoxelOreFeatureDefinition oreFeature)
        {
            Scene scene = EditorSceneManager.OpenScene(
                InfiniteScenePath,
                OpenSceneMode.Single);
            MinecraftCaves.MinecraftCaveInfiniteWorld world =
                UnityEngine.Object.FindObjectOfType<
                    MinecraftCaves.MinecraftCaveInfiniteWorld>();
            VoxelPlayerController player =
                UnityEngine.Object.FindObjectOfType<VoxelPlayerController>();
            if (world == null || player == null)
            {
                throw new InvalidOperationException(
                    "InfiniteCaves must contain MinecraftCaveInfiniteWorld and VoxelPlayerController.");
            }

            var serializedWorld = new SerializedObject(world);
            serializedWorld.FindProperty("viewer").objectReferenceValue = player.transform;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(world);
            EditorSceneManager.SaveScene(scene);

            MinecraftCaves.MinecraftWorldGenerationConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftCaves.MinecraftWorldGenerationConfiguration>(
                    WorldGenerationPath);
            if (configuration == null)
            {
                configuration = ScriptableObject.CreateInstance<
                    MinecraftCaves.MinecraftWorldGenerationConfiguration>();
                AssetDatabase.CreateAsset(configuration, WorldGenerationPath);
            }

            var serializedConfiguration = new SerializedObject(configuration);
            serializedConfiguration.FindProperty("placeViewerInCave")
                .boolValue = true;
            serializedConfiguration.FindProperty("voxelTypeCatalog")
                .objectReferenceValue = catalog;
            serializedConfiguration.FindProperty("baseSolidVoxelType")
                .objectReferenceValue =
                catalog.Find(new VoxelTypeId(2));
            SerializedProperty oreFeatures =
                serializedConfiguration.FindProperty("oreFeatures");
            oreFeatures.arraySize = 1;
            oreFeatures.GetArrayElementAtIndex(0).objectReferenceValue = oreFeature;
            SerializedProperty rule =
                serializedConfiguration.FindProperty("spawnPointStructureRule");
            rule.FindPropertyRelative("enabled").boolValue = true;
            rule.FindPropertyRelative("structure").objectReferenceValue = structure;
            rule.FindPropertyRelative("offset").vector3IntValue = Vector3Int.zero;
            serializedConfiguration.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(configuration);
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = $"{parent}/{name}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
