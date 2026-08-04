#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Supernova.Gameplay;
using Supernova.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EquipmentIconBaker
{
    private const int IconSize = 384;
    private const float FramingPadding = 1.16f;

    [MenuItem("Tools/Supernova/UI/Bake Equipment Icons")]
    public static void BakeAllIcons()
    {
        EnsureAssetFolder(ProjectAssetPaths.Folders.EquipmentIconTextures);
        EquipmentIconCatalog catalog = EnsureIconCatalogAsset();
        IReadOnlyList<PlayerInventoryItem> items = GetEquipmentItems();
        Dictionary<int, PlayerToolDefinition> definitions = LoadDefinitions();
        var bakedIcons = new Dictionary<int, Sprite>();

        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                PlayerInventoryItem item = items[i];
                EditorUtility.DisplayProgressBar(
                    "Bake Equipment Icons",
                    "Rendering " + item,
                    i / (float)items.Count);

                definitions.TryGetValue((int)item, out PlayerToolDefinition definition);
                GameObject sourcePrefab = ResolveSourcePrefab(item, definition);
                if (sourcePrefab == null)
                {
                    Debug.LogError("No thumbnail source prefab was found for " + item + ".");
                    continue;
                }

                string assetPath = GetIconAssetPath(item);
                BakePrefab(item, sourcePrefab, assetPath);
                ConfigureSpriteImporter(assetPath);
                Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (icon != null)
                    bakedIcons[(int)item] = icon;
            }

            WriteCatalogEntries(catalog, items, bakedIcons);
            GameAssetCatalogBuilder.EnsureCatalog();
            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
            Debug.Log(
                $"Baked {bakedIcons.Count} equipment thumbnails into "
                + ProjectAssetPaths.Folders.EquipmentIconTextures,
                catalog);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    public static EquipmentIconCatalog EnsureIconCatalogAsset()
    {
        EnsureAssetFolder(ProjectAssetPaths.Folders.UiConfig);
        EquipmentIconCatalog catalog =
            AssetDatabase.LoadAssetAtPath<EquipmentIconCatalog>(
                ProjectAssetPaths.Config.EquipmentIconCatalog);
        if (catalog != null)
            return catalog;

        catalog = ScriptableObject.CreateInstance<EquipmentIconCatalog>();
        AssetDatabase.CreateAsset(
            catalog,
            ProjectAssetPaths.Config.EquipmentIconCatalog);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    private static void BakePrefab(
        PlayerInventoryItem item,
        GameObject sourcePrefab,
        string assetPath)
    {
        Scene originalActiveScene = SceneManager.GetActiveScene();
        Scene previewScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Additive);
        if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
            SceneManager.SetActiveScene(originalActiveScene);
        RenderTexture renderTexture = null;
        Texture2D texture = null;
        try
        {
            int previewLayer = LayerMask.NameToLayer(UiLayerNames.PausePortrait);
            if (previewLayer < 0)
                throw new InvalidOperationException(
                    "The equipment icon baker requires the "
                    + UiLayerNames.PausePortrait + " layer.");

            GameObject instance = PrefabUtility.InstantiatePrefab(
                sourcePrefab,
                previewScene) as GameObject;
            if (instance == null)
                throw new InvalidOperationException(
                    "Could not instantiate thumbnail source: "
                    + AssetDatabase.GetAssetPath(sourcePrefab));

            instance.name = item + " Thumbnail Source";
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            SetLayerRecursively(instance.transform, previewLayer);
            PrepareInstance(instance);

            Renderer[] renderers = GetVisibleRenderers(instance);
            if (renderers.Length == 0)
                throw new InvalidOperationException(
                    "Thumbnail source contains no visible mesh renderers: "
                    + AssetDatabase.GetAssetPath(sourcePrefab));

            Bounds bounds = GetBounds(renderers);
            Camera camera = CreateCamera(previewScene, bounds, previewLayer);
            CreateLight(
                previewScene,
                "Equipment Icon Key",
                new Vector3(42f, -34f, 0f),
                1.35f,
                previewLayer);
            CreateLight(
                previewScene,
                "Equipment Icon Fill",
                new Vector3(18f, 148f, 0f),
                0.72f,
                previewLayer);
            CreateLight(
                previewScene,
                "Equipment Icon Rim",
                new Vector3(-28f, 42f, 0f),
                0.48f,
                previewLayer);

            renderTexture = new RenderTexture(
                IconSize,
                IconSize,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = item + " Equipment Icon",
                antiAliasing = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            renderTexture.Create();
            camera.targetTexture = renderTexture;

            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.Render();
                RenderTexture.active = renderTexture;
                texture = new Texture2D(
                    IconSize,
                    IconSize,
                    TextureFormat.RGBA32,
                    false,
                    false);
                texture.ReadPixels(new Rect(0f, 0f, IconSize, IconSize), 0, 0);
                texture.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
            }

            ConvertToHudMonochrome(texture);
            string absolutePath = ProjectAssetPaths.ToAbsoluteFileSystemPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }
        finally
        {
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
            EditorSceneManager.CloseScene(previewScene, true);
            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                SceneManager.SetActiveScene(originalActiveScene);
        }
    }

    private static Camera CreateCamera(
        Scene previewScene,
        Bounds bounds,
        int previewLayer)
    {
        GameObject cameraObject = new GameObject("Equipment Icon Camera");
        SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.allowHDR = false;
        camera.allowMSAA = true;
        camera.useOcclusionCulling = false;
        camera.cullingMask = 1 << previewLayer;

        Vector3 center = bounds.center;
        float radius = Mathf.Max(0.1f, bounds.extents.magnitude);
        Vector3 viewDirection = new Vector3(1.1f, 0.62f, -1.35f).normalized;
        float distance = radius * 3.2f + 1f;
        camera.transform.position = center + viewDirection * distance;
        camera.transform.LookAt(center, Vector3.up);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = distance + radius * 4f + 2f;

        float halfWidth = 0f;
        float halfHeight = 0f;
        foreach (Vector3 corner in GetBoundsCorners(bounds))
        {
            Vector3 offset = corner - center;
            halfWidth = Mathf.Max(
                halfWidth,
                Mathf.Abs(Vector3.Dot(offset, camera.transform.right)));
            halfHeight = Mathf.Max(
                halfHeight,
                Mathf.Abs(Vector3.Dot(offset, camera.transform.up)));
        }

        camera.orthographicSize = Mathf.Max(halfHeight, halfWidth)
            * FramingPadding;
        return camera;
    }

    private static void CreateLight(
        Scene scene,
        string objectName,
        Vector3 eulerAngles,
        float intensity,
        int previewLayer)
    {
        GameObject lightObject = new GameObject(objectName);
        SceneManager.MoveGameObjectToScene(lightObject, scene);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        light.cullingMask = 1 << previewLayer;
        lightObject.transform.rotation = Quaternion.Euler(eulerAngles);
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private static void PrepareInstance(GameObject instance)
    {
        Behaviour[] behaviours = instance.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
            behaviours[i].enabled = false;

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            bool isMesh = renderers[i] is MeshRenderer
                || renderers[i] is SkinnedMeshRenderer;
            renderers[i].enabled = isMesh;
        }
    }

    private static Renderer[] GetVisibleRenderers(GameObject instance)
    {
        return instance.GetComponentsInChildren<Renderer>(true)
            .Where(renderer =>
                renderer.enabled
                && renderer.gameObject.activeInHierarchy
                && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
            .ToArray();
    }

    private static Bounds GetBounds(IReadOnlyList<Renderer> renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static IEnumerable<Vector3> GetBoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        yield return new Vector3(min.x, min.y, min.z);
        yield return new Vector3(min.x, min.y, max.z);
        yield return new Vector3(min.x, max.y, min.z);
        yield return new Vector3(min.x, max.y, max.z);
        yield return new Vector3(max.x, min.y, min.z);
        yield return new Vector3(max.x, min.y, max.z);
        yield return new Vector3(max.x, max.y, min.z);
        yield return new Vector3(max.x, max.y, max.z);
    }

    private static void ConvertToHudMonochrome(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        float minimum = 1f;
        float maximum = 0f;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].a <= 0.02f)
                continue;
            float luminance = GetLuminance(pixels[i]);
            minimum = Mathf.Min(minimum, luminance);
            maximum = Mathf.Max(maximum, luminance);
        }

        float range = Mathf.Max(0.08f, maximum - minimum);
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            if (pixel.a <= 0.003f)
            {
                pixels[i] = Color.clear;
                continue;
            }

            float normalized = Mathf.Clamp01(
                (GetLuminance(pixel) - minimum) / range);
            float gray = Mathf.Lerp(
                0.5f,
                1f,
                Mathf.Pow(normalized, 0.72f));
            pixels[i] = new Color(gray, gray, gray, pixel.a);
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
    }

    private static float GetLuminance(Color color)
    {
        return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
    }

    private static void ConfigureSpriteImporter(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("Could not import equipment icon: " + assetPath);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = IconSize;
        importer.spritePixelsPerUnit = 100f;
        importer.SaveAndReimport();
    }

    private static void WriteCatalogEntries(
        EquipmentIconCatalog catalog,
        IReadOnlyList<PlayerInventoryItem> items,
        IReadOnlyDictionary<int, Sprite> icons)
    {
        SerializedObject serialized = new SerializedObject(catalog);
        SerializedProperty entries = serialized.FindProperty("entries");
        entries.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            PlayerInventoryItem item = items[i];
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("item").intValue = (int)item;
            entry.FindPropertyRelative("icon").objectReferenceValue =
                icons.TryGetValue((int)item, out Sprite icon) ? icon : null;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
    }

    private static Dictionary<int, PlayerToolDefinition> LoadDefinitions()
    {
        var definitions = new Dictionary<int, PlayerToolDefinition>();
        string[] guids = AssetDatabase.FindAssets(
            "t:PlayerToolDefinition",
            new[] { ProjectAssetPaths.Folders.Tools });
        foreach (string path in guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(path);
            if (definition != null && definition.Item != PlayerInventoryItem.Empty)
                definitions[(int)definition.Item] = definition;
        }

        return definitions;
    }

    private static GameObject ResolveSourcePrefab(
        PlayerInventoryItem item,
        PlayerToolDefinition definition)
    {
        if (definition != null)
        {
            if (definition.HeldModelPrefab != null)
                return definition.HeldModelPrefab;
            if (definition.GrabHookProjectileModelPrefab != null)
                return definition.GrabHookProjectileModelPrefab;
        }

        switch (item)
        {
            case PlayerInventoryItem.Magnet:
                return AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.Prefabs.AttractionModuleDisplay);
            case PlayerInventoryItem.Cart:
                return AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.ThirdParty.EmptyCart);
            case PlayerInventoryItem.GrabHook:
                return AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.Prefabs.GrabHook);
            default:
                return null;
        }
    }

    private static IReadOnlyList<PlayerInventoryItem> GetEquipmentItems()
    {
        return Enum.GetValues(typeof(PlayerInventoryItem))
            .Cast<PlayerInventoryItem>()
            .Where(item => item != PlayerInventoryItem.Empty)
            .GroupBy(item => (int)item)
            .Select(group => group.First())
            .OrderBy(item => (int)item)
            .ToArray();
    }

    private static string GetIconAssetPath(PlayerInventoryItem item)
    {
        return ProjectAssetPaths.Folders.EquipmentIconTextures
            + "/" + item + ".png";
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
