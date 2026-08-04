using Supernova.Portals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class PortalSceneBuilder
{
    private const string PortalShaderPath =
        ProjectAssetPaths.Folders.PortalShaders + "/PortalSurface.shader";
    private const string BluePortalMaterialPath =
        ProjectAssetPaths.Folders.PortalMaterials + "/PortalBlue.mat";
    private const string OrangePortalMaterialPath =
        ProjectAssetPaths.Folders.PortalMaterials + "/PortalOrange.mat";

    [MenuItem("Tools/Supernova/Portals/Build Portal Demo Scene")]
    public static void Build()
    {
        EnsureFolder(ProjectAssetPaths.Folders.PortalMaterials);
        Material blueMaterial = CreatePortalMaterial(
            BluePortalMaterialPath,
            new Color(0.05f, 0.8f, 6f, 1f));
        Material orangeMaterial = CreatePortalMaterial(
            OrangePortalMaterialPath,
            new Color(6f, 0.65f, 0.04f, 1f));

        Material floorMaterial = CreateLitMaterial(
            ProjectAssetPaths.Folders.PortalMaterials + "/Floor.mat",
            new Color(0.13f, 0.15f, 0.18f));
        Material wallMaterial = CreateLitMaterial(
            ProjectAssetPaths.Folders.PortalMaterials + "/Wall.mat",
            new Color(0.4f, 0.43f, 0.48f));
        Material redMaterial = CreateLitMaterial(
            ProjectAssetPaths.Folders.PortalMaterials + "/RedMarker.mat",
            new Color(0.85f, 0.12f, 0.08f));
        Material greenMaterial = CreateLitMaterial(
            ProjectAssetPaths.Folders.PortalMaterials + "/GreenMarker.mat",
            new Color(0.08f, 0.75f, 0.22f));

        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Portal");
        if (!scene.IsValid())
        {
            scene = UnityEngine.SceneManagement.SceneManager.CreateScene("Portal");
        }
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            Object.DestroyImmediate(rootObject);
        }

        CreateEnvironment(floorMaterial, wallMaterial, redMaterial, greenMaterial);
        Portal blue = CreatePortal(
            "Blue Portal",
            new Vector3(0f, 1.8f, -0.06f),
            Quaternion.identity,
            blueMaterial);
        Portal orange = CreatePortal(
            "Orange Portal",
            new Vector3(8.94f, 1.8f, 7f),
            Quaternion.Euler(0f, 90f, 0f),
            orangeMaterial);
        SetPairedPortal(blue, orange);
        SetPairedPortal(orange, blue);

        CreatePlayer();
        CreateLighting();
        CreateInstructions();

        const string temporaryScenePath = "Assets/Scenes/Portal.unity";
        EditorSceneManager.SaveScene(scene, temporaryScenePath);
        if (AssetDatabase.LoadAssetAtPath<Object>(ProjectAssetPaths.Scenes.Portal) != null)
        {
            AssetDatabase.DeleteAsset(ProjectAssetPaths.Scenes.Portal);
        }
        string moveError = AssetDatabase.MoveAsset(
            temporaryScenePath,
            ProjectAssetPaths.Scenes.Portal);
        if (!string.IsNullOrEmpty(moveError))
        {
            throw new System.InvalidOperationException(moveError);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(
            ProjectAssetPaths.Scenes.Portal);
        Debug.Log($"Portal demo scene created: {ProjectAssetPaths.Scenes.Portal}");
    }

    private static Portal CreatePortal(
        string name,
        Vector3 position,
        Quaternion rotation,
        Material material)
    {
        GameObject root = new GameObject(name);
        root.transform.SetPositionAndRotation(position, rotation);

        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        surface.name = "Surface";
        surface.transform.SetParent(root.transform, false);
        surface.transform.localScale = new Vector3(2.2f, 3.6f, 1f);
        Object.DestroyImmediate(surface.GetComponent<Collider>());
        Renderer renderer = surface.GetComponent<Renderer>();
        renderer.sharedMaterial = material;

        GameObject trigger = new GameObject("Trigger");
        trigger.transform.SetParent(root.transform, false);
        trigger.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        triggerCollider.size = new Vector3(2.05f, 3.45f, 0.7f);

        GameObject cameraObject = new GameObject("Portal Camera");
        cameraObject.transform.SetParent(root.transform, false);
        Camera portalCamera = cameraObject.AddComponent<Camera>();
        portalCamera.enabled = false;
        portalCamera.allowHDR = true;
        portalCamera.allowMSAA = false;
        cameraObject.AddComponent<UniversalAdditionalCameraData>();

        Portal portal = root.AddComponent<Portal>();
        SerializedObject serializedPortal = new SerializedObject(portal);
        serializedPortal.FindProperty("surfaceRenderer").objectReferenceValue = renderer;
        serializedPortal.FindProperty("portalCamera").objectReferenceValue = portalCamera;
        serializedPortal.ApplyModifiedPropertiesWithoutUndo();
        return portal;
    }

    private static void SetPairedPortal(Portal portal, Portal pairedPortal)
    {
        SerializedObject serializedPortal = new SerializedObject(portal);
        serializedPortal.FindProperty("pairedPortal").objectReferenceValue = pairedPortal;
        serializedPortal.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreatePlayer()
    {
        GameObject player = new GameObject("Portal Player");
        player.transform.position = new Vector3(0f, 1.1f, -7f);
        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.35f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        player.AddComponent<PortalTraveller>();
        PortalDemoController demoController = player.AddComponent<PortalDemoController>();

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        camera.fieldOfView = 70f;
        camera.allowHDR = true;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<UniversalAdditionalCameraData>();

        SerializedObject serializedController = new SerializedObject(demoController);
        serializedController.FindProperty("view").objectReferenceValue = cameraObject.transform;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateEnvironment(
        Material floorMaterial,
        Material wallMaterial,
        Material redMaterial,
        Material greenMaterial)
    {
        CreateCube("Floor", new Vector3(2f, -0.25f, 3f),
            new Vector3(24f, 0.5f, 24f), floorMaterial);
        CreateCube("Blue Wall", new Vector3(0f, 2.5f, 0.5f),
            new Vector3(12f, 5f, 1f), wallMaterial);
        CreateCube("Orange Wall", new Vector3(8.5f, 2.5f, 7f),
            new Vector3(1f, 5f, 12f), wallMaterial);

        CreateCube("Red Exit Marker", new Vector3(8f, 0.5f, 7f),
            Vector3.one, redMaterial);
        CreateCube("Green View Marker", new Vector3(-2.5f, 0.75f, 3f),
            new Vector3(1.5f, 1.5f, 1.5f), greenMaterial);
        CreateCube("Tall Pillar", new Vector3(5f, 2f, 5f),
            new Vector3(1.2f, 4f, 1.2f), wallMaterial);
    }

    private static void CreateLighting()
    {
        GameObject lightObject = new GameObject("Sun");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.color = new Color(0.92f, 0.96f, 1f);
        lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.22f, 0.28f, 0.38f);
        RenderSettings.ambientEquatorColor = new Color(0.12f, 0.14f, 0.18f);
        RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.05f);
    }

    private static void CreateInstructions()
    {
        GameObject canvasObject = new GameObject("Instructions");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(canvasObject.transform, false);
        UnityEngine.UI.Text text = textObject.AddComponent<UnityEngine.UI.Text>();
        text.text = "WASD move  |  Mouse look  |  Walk through the glowing portal  |  R reset";
        text.alignment = TextAnchor.UpperCenter;
        text.color = Color.white;
        text.fontSize = 22;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -18f);
        rect.sizeDelta = new Vector2(0f, 50f);
    }

    private static GameObject CreateCube(
        string name,
        Vector3 position,
        Vector3 scale,
        Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = position;
        cube.transform.localScale = scale;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        return cube;
    }

    private static Material CreatePortalMaterial(string path, Color edgeColor)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(PortalShaderPath);
        if (shader == null)
        {
            throw new MissingReferenceException($"Missing portal shader: {PortalShaderPath}");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_EdgeColor", edgeColor);
        material.SetFloat("_EdgeWidth", 0.16f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateLitMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.SetFloat("_Smoothness", 0.35f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = System.IO.Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent))
        {
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
