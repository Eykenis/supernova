#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using Supernova.Voxels.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RandomStructureExampleBuilder
{
    [MenuItem("Tools/Supernova/World Generation/Build Random Trial Chamber Example")]
    public static void Build()
    {
        EnsureFolder(ProjectAssetPaths.Folders.StructureFeatures);

        VoxelTypeDefinition structureBrick = EnsureStructureBrick();
        EnsureVoxelCatalogContains(structureBrick);
        VoxelStructureAsset template = EnsureTrialChamberTemplate(structureBrick);
        VoxelStructureFeatureDefinition feature = EnsureTrialChamber(
            structureBrick,
            template);
        AttachFeatureToDefaultWorld(feature);
        ConfigureAuthoringScene(feature);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = feature;
        EditorGUIUtility.PingObject(feature);
        Debug.Log(
            "Built the deterministic random Trial Chamber structure feature and attached it to DefaultWorldGeneration.",
            feature);
    }

    [MenuItem("Tools/Supernova/Voxels/Edit Random Trial Chamber Template")]
    public static void OpenTrialChamberEditor()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        VoxelStructureFeatureDefinition feature =
            AssetDatabase.LoadAssetAtPath<VoxelStructureFeatureDefinition>(
                ProjectAssetPaths.Config.TrialChamberFeature);
        if (feature == null || feature.StructureTemplate == null)
        {
            throw new InvalidOperationException(
                "Build the Random Trial Chamber Example before opening its editor.");
        }

        OpenAndConfigureAuthoringScene(feature);
    }

    private static VoxelTypeDefinition EnsureStructureBrick()
    {
        VoxelTypeDefinition definition =
            AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                ProjectAssetPaths.Config.StructureBrickVoxel);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<VoxelTypeDefinition>();
            AssetDatabase.CreateAsset(
                definition,
                ProjectAssetPaths.Config.StructureBrickVoxel);
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(
            ProjectAssetPaths.Materials.Marble);
        if (material == null)
        {
            throw new InvalidOperationException(
                $"Missing structure material: {ProjectAssetPaths.Materials.Marble}");
        }

        definition.Configure(5, "Structure Brick", 3, material);
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static void EnsureVoxelCatalogContains(
        VoxelTypeDefinition structureBrick)
    {
        VoxelTypeCatalog catalog = AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(
            ProjectAssetPaths.Config.VoxelCatalog);
        if (catalog == null)
        {
            throw new InvalidOperationException(
                $"Missing voxel type catalog: {ProjectAssetPaths.Config.VoxelCatalog}");
        }

        var definitions = new List<VoxelTypeDefinition>(catalog.Definitions);
        definitions.RemoveAll(item => item == null || item.TypeId == structureBrick.TypeId);
        definitions.Add(structureBrick);
        catalog.SetDefinitions(definitions.OrderBy(item => item.TypeId.Value));
        EditorUtility.SetDirty(catalog);
    }

    private static VoxelStructureFeatureDefinition EnsureTrialChamber(
        VoxelTypeDefinition structureBrick,
        VoxelStructureAsset template)
    {
        VoxelStructureFeatureDefinition feature =
            AssetDatabase.LoadAssetAtPath<VoxelStructureFeatureDefinition>(
                ProjectAssetPaths.Config.TrialChamberFeature);
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<
                VoxelStructureFeatureDefinition>();
            AssetDatabase.CreateAsset(
                feature,
                ProjectAssetPaths.Config.TrialChamberFeature);
        }

        feature.Configure(
            "trial_chamber_example",
            structureBrick,
            7919,
            6,
            0.7f,
            72,
            188,
            new Vector3Int(21, 10, 17),
            1,
            2,
            3,
            4,
            14,
            template);
        EditorUtility.SetDirty(feature);
        return feature;
    }

    private static VoxelStructureAsset EnsureTrialChamberTemplate(
        VoxelTypeDefinition structureBrick)
    {
        EnsureFolder(ProjectAssetPaths.Folders.Structures);
        VoxelStructureAsset template =
            AssetDatabase.LoadAssetAtPath<VoxelStructureAsset>(
                ProjectAssetPaths.Structures.TrialChamberTemplate);
        if (template != null)
        {
            return template;
        }

        template = ScriptableObject.CreateInstance<VoxelStructureAsset>();
        var size = new Vector3Int(21, 12, 17);
        var anchor = new Vector3Int(10, 2, 8);
        var densities = new float[size.x * size.y * size.z];
        var types = new ushort[densities.Length];
        for (int i = 0; i < densities.Length; i++)
        {
            densities[i] = -1f;
        }

        for (int z = 0; z < size.z; z++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    int localX = x - anchor.x;
                    int localY = y - anchor.y;
                    int localZ = z - anchor.z;
                    bool foundation = localY < 0;
                    bool floor = localY == 0;
                    bool roof = localY == 9;
                    bool wall = Mathf.Abs(localX) == 10
                        || Mathf.Abs(localZ) == 8;
                    bool doorway = localZ == 8
                        && Mathf.Abs(localX) <= 1
                        && localY >= 1
                        && localY <= 4;
                    bool pillar = Mathf.Abs(localX) == 7
                        && Mathf.Abs(localZ) == 5
                        && localY > 0
                        && localY < 9;
                    bool centralDais = Mathf.Abs(localX) <= 2
                        && Mathf.Abs(localZ) <= 2
                        && localY == 1;
                    if ((foundation || floor || roof || wall || pillar || centralDais)
                        && !doorway)
                    {
                        int index = x + size.x * (y + size.y * z);
                        densities[index] = 1f;
                        types[index] = structureBrick.TypeId.Value;
                    }
                }
            }
        }

        template.SetData(
            size,
            anchor,
            new Vector3(0f, 1.25f, 0f),
            densities,
            types);
        AssetDatabase.CreateAsset(
            template,
            ProjectAssetPaths.Structures.TrialChamberTemplate);
        return template;
    }

    private static void AttachFeatureToDefaultWorld(
        VoxelStructureFeatureDefinition feature)
    {
        MinecraftWorldGenerationConfiguration configuration =
            AssetDatabase.LoadAssetAtPath<MinecraftWorldGenerationConfiguration>(
                ProjectAssetPaths.Config.WorldGeneration);
        if (configuration == null)
        {
            throw new InvalidOperationException(
                $"Missing world generation configuration: {ProjectAssetPaths.Config.WorldGeneration}");
        }

        var features = configuration.StructureFeatures
            .Where(item => item != null && item != feature)
            .ToList();
        features.Add(feature);
        configuration.SetStructureFeatures(features);
        EditorUtility.SetDirty(configuration);
    }

    private static void ConfigureAuthoringScene(
        VoxelStructureFeatureDefinition feature)
    {
        Scene previousScene = SceneManager.GetActiveScene();
        if (previousScene.IsValid() && previousScene.isDirty)
        {
            Debug.LogWarning(
                "Skipped automatic VoxelStructureEditor scene configuration because the active scene has unsaved changes. Run the builder again after saving.");
            return;
        }

        string previousPath = previousScene.path;
        OpenAndConfigureAuthoringScene(feature);

        if (!string.IsNullOrEmpty(previousPath)
            && previousPath != ProjectAssetPaths.Scenes.VoxelStructureEditor)
        {
            EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
        }
    }

    private static void OpenAndConfigureAuthoringScene(
        VoxelStructureFeatureDefinition feature)
    {
        Scene authoringScene = EditorSceneManager.OpenScene(
            ProjectAssetPaths.Scenes.VoxelStructureEditor,
            OpenSceneMode.Single);
        VoxelStructureAuthoring authoring =
            UnityEngine.Object.FindObjectOfType<VoxelStructureAuthoring>();
        if (authoring == null)
        {
            throw new InvalidOperationException(
                "VoxelStructureEditor scene has no VoxelStructureAuthoring component.");
        }

        VoxelTypeCatalog catalog = AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(
            ProjectAssetPaths.Config.VoxelCatalog);
        authoring.ConfigureFeature(feature, catalog);
        VoxelStructureAuthoringEditor.LoadStructure(authoring);
        EditorUtility.SetDirty(authoring);
        EditorSceneManager.SaveScene(authoringScene);
        Selection.activeGameObject = authoring.gameObject;
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
        {
            return;
        }

        string normalized = assetFolder.Replace('\\', '/');
        int separator = normalized.LastIndexOf('/');
        if (separator <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid asset folder path: {assetFolder}");
        }
        string parent = normalized.Substring(0, separator);
        string name = normalized.Substring(separator + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
