#if UNITY_EDITOR
using System.IO;
using System.Text;
using Supernova.Inputs;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public static class GameInputAssetBuilder
{
    [MenuItem("Tools/Supernova/Input/Rebuild Game Input Actions")]
    public static void RebuildInputActionsAsset()
    {
        WriteInputActionsAsset();
        InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
            ProjectAssetPaths.Config.GameInputActions);
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
        Debug.Log(
            "Rebuilt the centralized Input Actions asset at "
            + ProjectAssetPaths.Config.GameInputActions,
            asset);
    }

    public static InputActionAsset EnsureInputActionsAsset()
    {
        InputActionAsset existing =
            AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                ProjectAssetPaths.Config.GameInputActions);
        if (existing != null)
            return existing;

        WriteInputActionsAsset();
        existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
            ProjectAssetPaths.Config.GameInputActions);
        if (existing == null)
            throw new InvalidDataException(
                "Failed to create the centralized Input Actions asset.");
        return existing;
    }

    private static void WriteInputActionsAsset()
    {
        string directory = ProjectAssetPaths.ToAbsoluteFileSystemPath(
            ProjectAssetPaths.Folders.InputConfig);
        Directory.CreateDirectory(directory);

        InputActionAsset generated = GameInputDefinitions.CreateAsset();
        try
        {
            string absolutePath = ProjectAssetPaths.ToAbsoluteFileSystemPath(
                ProjectAssetPaths.Config.GameInputActions);
            File.WriteAllText(
                absolutePath,
                generated.ToJson(),
                new UTF8Encoding(false));
        }
        finally
        {
            Object.DestroyImmediate(generated);
        }

        AssetDatabase.ImportAsset(
            ProjectAssetPaths.Config.GameInputActions,
            ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();
    }
}
#endif
