using System;
using Supernova.PortalExample;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Supernova.PortalExample.Editor
{
    public static class PortalExampleSceneBuilder
    {
        private const string MenuPath =
            "Tools/Supernova/Portal Example/Build Isolated Demo Scene";

        [MenuItem(MenuPath)]
        public static void Build()
        {
            EnsurePortalAssets(
                out Material bluePortal,
                out Material orangePortal,
                out Mesh portalRing);
            EnsureFolder(ProjectAssetPaths.Folders.PortalExampleScenes);
            Material whitePanel = CreateLitMaterial(
                ProjectAssetPaths.Materials.PortalExampleWhitePanel,
                new Color(0.72f, 0.75f, 0.77f),
                0f,
                0.48f);
            Material darkPanel = CreateLitMaterial(
                ProjectAssetPaths.Materials.PortalExampleDarkPanel,
                new Color(0.025f, 0.035f, 0.045f),
                0.15f,
                0.32f);
            Material metal = CreateLitMaterial(
                ProjectAssetPaths.Materials.PortalExampleMetal,
                new Color(0.14f, 0.17f, 0.2f),
                0.78f,
                0.72f);
            Material button = CreateLitMaterial(
                ProjectAssetPaths.Materials.PortalExampleButton,
                new Color(0.52f, 0.025f, 0.018f),
                0.2f,
                0.5f,
                new Color(1.8f, 0.035f, 0.015f));
            Material goal = CreateLitMaterial(
                ProjectAssetPaths.Materials.PortalExampleGoal,
                new Color(0.03f, 0.42f, 0.18f),
                0.1f,
                0.45f,
                new Color(0.02f, 2.5f, 0.6f));
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try
            {
                GameObject root = new GameObject("PortalExample_Isolated");
                CreateEnvironment(
                    root.transform,
                    whitePanel,
                    darkPanel,
                    metal,
                    goal);

                PortalExampleGate blueGate = CreatePortal(
                    root.transform,
                    "Blue Portal / 蓝色入口",
                    new Vector3(0f, 2.05f, -0.19f),
                    Quaternion.Euler(0f, 180f, 0f),
                    bluePortal,
                    portalRing);
                PortalExampleGate orangeGate = CreatePortal(
                    root.transform,
                    "Orange Portal / 橙色出口",
                    new Vector3(9.19f, 2.05f, 7f),
                    Quaternion.Euler(0f, -90f, 0f),
                    orangePortal,
                    portalRing);
                LinkPortals(blueGate, orangeGate);
                LinkPortals(orangeGate, blueGate);

                Transform playerReset = CreateMarker(
                    root.transform,
                    "Player Reset Point",
                    new Vector3(0f, 0.05f, -7.2f),
                    Quaternion.identity);
                Transform cubeReset = CreateMarker(
                    root.transform,
                    "Cube Reset Point",
                    new Vector3(-3.3f, 1.35f, -5.2f),
                    Quaternion.identity);
                PortalExampleFirstPersonController player = CreatePlayer(
                    root.transform,
                    playerReset);
                CreateTestCube(
                    root.transform,
                    cubeReset,
                    whitePanel,
                    metal);
                PortalExampleDoor door = CreateExitDoor(
                    root.transform,
                    metal,
                    goal);
                PortalExampleFloorButton floorButton = CreateFloorButton(
                    root.transform,
                    button,
                    metal,
                    door);
                CreateHud(player.gameObject, floorButton);
                CreateLighting(root.transform);
                ConfigureRenderSettings();

                if (!EditorSceneManager.SaveScene(
                        scene,
                        ProjectAssetPaths.Scenes.PortalExample))
                {
                    throw new InvalidOperationException(
                        "Failed to save portal example scene at "
                        + ProjectAssetPaths.Scenes.PortalExample);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    ProjectAssetPaths.Scenes.PortalExample);
                Debug.Log(
                    "Built isolated Portal example scene: "
                    + ProjectAssetPaths.Scenes.PortalExample);
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                if (previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }
        }

        private static void CreateEnvironment(
            Transform parent,
            Material whitePanel,
            Material darkPanel,
            Material metal,
            Material goal)
        {
            GameObject environment = new GameObject("Aperture Test Chamber");
            environment.transform.SetParent(parent, false);

            CreateCube(
                environment.transform,
                "Dark Structural Floor",
                new Vector3(2.5f, -0.32f, 3.3f),
                new Vector3(23f, 0.5f, 27.4f),
                darkPanel);
            CreateFloorTiles(environment.transform, whitePanel, darkPanel);

            CreateCube(environment.transform, "South Wall",
                new Vector3(2.5f, 2.7f, -10.5f),
                new Vector3(23f, 5.8f, 0.35f), whitePanel);
            CreateCube(environment.transform, "West Wall",
                new Vector3(-9f, 2.7f, 3.3f),
                new Vector3(0.35f, 5.8f, 27.4f), whitePanel);
            CreateCube(environment.transform, "East Wall",
                new Vector3(14f, 2.7f, 3.3f),
                new Vector3(0.35f, 5.8f, 27.4f), whitePanel);
            CreateCube(environment.transform, "North Wall",
                new Vector3(2.5f, 2.7f, 17f),
                new Vector3(23f, 5.8f, 0.35f), whitePanel);

            CreatePortalWallAlongX(
                environment.transform,
                "Blue Portal Divider",
                0f,
                -9f,
                9f,
                0f,
                whitePanel,
                darkPanel);
            CreatePortalWallAlongZ(
                environment.transform,
                "Orange Portal Divider",
                9f,
                0f,
                13f,
                7f,
                whitePanel,
                darkPanel);
            CreateDoorWall(
                environment.transform,
                13f,
                -9f,
                9f,
                5f,
                whitePanel,
                darkPanel);

            CreateCube(environment.transform, "Cube Pedestal",
                new Vector3(-3.3f, 0.35f, -5.2f),
                new Vector3(2.1f, 0.7f, 2.1f), metal);
            CreateCube(environment.transform, "Observation Plinth",
                new Vector3(11.4f, 0.45f, 7f),
                new Vector3(4.2f, 0.9f, 5.4f), darkPanel);
            CreateCube(environment.transform, "Goal Path",
                new Vector3(5f, 0.015f, 14.9f),
                new Vector3(2.6f, 0.03f, 3.6f), goal, false);

            CreateSign(environment.transform, "Chamber 01 Sign",
                "01  SPATIAL TRANSFER",
                new Vector3(-5.7f, 3.5f, -10.29f),
                Quaternion.identity,
                new Color(0.05f, 0.16f, 0.22f));
            CreateSign(environment.transform, "Blue Route Sign",
                "BLUE ENTRY  →",
                new Vector3(-3.8f, 4.4f, -0.21f),
                Quaternion.identity,
                new Color(0.05f, 0.36f, 0.72f));
            CreateSign(environment.transform, "Orange Route Sign",
                "ORANGE EXIT",
                new Vector3(9.21f, 4.45f, 4.3f),
                Quaternion.Euler(0f, 90f, 0f),
                new Color(0.85f, 0.28f, 0.02f));
        }

        private static void CreateFloorTiles(
            Transform parent,
            Material whitePanel,
            Material darkPanel)
        {
            GameObject tiles = new GameObject("Modular Floor Panels");
            tiles.transform.SetParent(parent, false);
            const float tileSize = 2.42f;
            for (int x = 0; x < 9; x++)
            {
                for (int z = 0; z < 11; z++)
                {
                    Vector3 position = new Vector3(
                        -7.2f + x * tileSize,
                        -0.055f,
                        -8.5f + z * tileSize);
                    Material material = (x + z) % 5 == 0
                        ? darkPanel
                        : whitePanel;
                    CreateCube(
                        tiles.transform,
                        "Floor Panel " + x + "-" + z,
                        position,
                        new Vector3(tileSize - 0.045f, 0.04f, tileSize - 0.045f),
                        material,
                        false);
                }
            }
        }

        private static void CreatePortalWallAlongX(
            Transform parent,
            string name,
            float z,
            float minimumX,
            float maximumX,
            float portalX,
            Material panel,
            Material frame)
        {
            GameObject wall = new GameObject(name);
            wall.transform.SetParent(parent, false);
            const float halfOpeningWidth = 1.32f;
            float leftWidth = portalX - halfOpeningWidth - minimumX;
            float rightWidth = maximumX - portalX - halfOpeningWidth;
            CreateCube(wall.transform, "Left Panels",
                new Vector3(minimumX + leftWidth * 0.5f, 2.7f, z),
                new Vector3(leftWidth, 5.8f, 0.38f), panel);
            CreateCube(wall.transform, "Right Panels",
                new Vector3(portalX + halfOpeningWidth + rightWidth * 0.5f, 2.7f, z),
                new Vector3(rightWidth, 5.8f, 0.38f), panel);
            CreateCube(wall.transform, "Portal Header",
                new Vector3(portalX, 5.05f, z),
                new Vector3(halfOpeningWidth * 2f, 1.1f, 0.38f), frame);
        }

        private static void CreatePortalWallAlongZ(
            Transform parent,
            string name,
            float x,
            float minimumZ,
            float maximumZ,
            float portalZ,
            Material panel,
            Material frame)
        {
            GameObject wall = new GameObject(name);
            wall.transform.SetParent(parent, false);
            const float halfOpeningWidth = 1.32f;
            float lowerLength = portalZ - halfOpeningWidth - minimumZ;
            float upperLength = maximumZ - portalZ - halfOpeningWidth;
            CreateCube(wall.transform, "South Panels",
                new Vector3(x, 2.7f, minimumZ + lowerLength * 0.5f),
                new Vector3(0.38f, 5.8f, lowerLength), panel);
            CreateCube(wall.transform, "North Panels",
                new Vector3(x, 2.7f,
                    portalZ + halfOpeningWidth + upperLength * 0.5f),
                new Vector3(0.38f, 5.8f, upperLength), panel);
            CreateCube(wall.transform, "Portal Header",
                new Vector3(x, 5.05f, portalZ),
                new Vector3(0.38f, 1.1f, halfOpeningWidth * 2f), frame);
        }

        private static void CreateDoorWall(
            Transform parent,
            float z,
            float minimumX,
            float maximumX,
            float doorX,
            Material panel,
            Material frame)
        {
            GameObject wall = new GameObject("Exit Door Wall");
            wall.transform.SetParent(parent, false);
            const float halfDoorWidth = 1.2f;
            float leftWidth = doorX - halfDoorWidth - minimumX;
            float rightWidth = maximumX - doorX - halfDoorWidth;
            CreateCube(wall.transform, "Left Wall",
                new Vector3(minimumX + leftWidth * 0.5f, 2.7f, z),
                new Vector3(leftWidth, 5.8f, 0.38f), panel);
            CreateCube(wall.transform, "Right Wall",
                new Vector3(doorX + halfDoorWidth + rightWidth * 0.5f, 2.7f, z),
                new Vector3(rightWidth, 5.8f, 0.38f), panel);
            CreateCube(wall.transform, "Door Header",
                new Vector3(doorX, 4.65f, z),
                new Vector3(halfDoorWidth * 2f, 1.9f, 0.38f), frame);
        }

        internal static PortalExampleGate CreatePortal(
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation,
            Material material,
            Mesh ringMesh)
        {
            GameObject portalObject = new GameObject(name);
            portalObject.transform.SetParent(parent, false);
            portalObject.transform.SetPositionAndRotation(position, rotation);

            GameObject surfaceObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surfaceObject.name = "Live Portal View";
            surfaceObject.transform.SetParent(portalObject.transform, false);
            surfaceObject.transform.localPosition = Vector3.zero;
            surfaceObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            surfaceObject.transform.localScale = new Vector3(2.15f, 2.15f, 1f);
            UnityEngine.Object.DestroyImmediate(surfaceObject.GetComponent<Collider>());
            Renderer surfaceRenderer = surfaceObject.GetComponent<Renderer>();
            surfaceRenderer.sharedMaterial = material;
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = false;

            GameObject ringObject = new GameObject("Emissive Circular Frame");
            ringObject.transform.SetParent(portalObject.transform, false);
            ringObject.transform.localPosition = new Vector3(0f, 0f, 0.025f);
            MeshFilter filter = ringObject.AddComponent<MeshFilter>();
            filter.sharedMesh = ringMesh;
            MeshRenderer ringRenderer = ringObject.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = material;
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;

            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(portalObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.05f, 2.05f, 1.2f);
            PortalExampleTriggerRelay triggerRelay =
                triggerObject.AddComponent<PortalExampleTriggerRelay>();

            GameObject cameraObject = new GameObject("Portal Camera");
            cameraObject.transform.SetParent(portalObject.transform, false);
            Camera portalCamera = cameraObject.AddComponent<Camera>();
            portalCamera.enabled = false;
            portalCamera.allowHDR = true;
            portalCamera.allowMSAA = false;
            cameraObject.AddComponent<UniversalAdditionalCameraData>();

            PortalExampleGate gate = portalObject.AddComponent<PortalExampleGate>();
            triggerRelay.Configure(gate);
            Shader clippedLitShader = AssetDatabase.LoadAssetAtPath<Shader>(
                ProjectAssetPaths.Shaders.PortalExampleClippedLit);
            if (clippedLitShader == null)
            {
                throw new MissingReferenceException(
                    "Missing clipped portal traveller shader at registered "
                    + "path: "
                    + ProjectAssetPaths.Shaders.PortalExampleClippedLit);
            }
            SerializedObject serializedGate = new SerializedObject(gate);
            serializedGate.FindProperty("surfaceRenderer").objectReferenceValue =
                surfaceRenderer;
            serializedGate.FindProperty("portalCamera").objectReferenceValue =
                portalCamera;
            serializedGate.FindProperty("seamlessClipShader")
                .objectReferenceValue = clippedLitShader;
            serializedGate.ApplyModifiedPropertiesWithoutUndo();
            return gate;
        }

        internal static void LinkPortals(
            PortalExampleGate source,
            PortalExampleGate destination)
        {
            SerializedObject serializedGate = new SerializedObject(source);
            serializedGate.FindProperty("linkedGate").objectReferenceValue =
                destination;
            serializedGate.ApplyModifiedPropertiesWithoutUndo();
        }

        private static PortalExampleFirstPersonController CreatePlayer(
            Transform parent,
            Transform resetPoint)
        {
            GameObject player = new GameObject("Portal Test Subject");
            player.transform.SetParent(parent, false);
            player.transform.SetPositionAndRotation(
                resetPoint.position,
                resetPoint.rotation);

            CharacterController controller =
                player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.34f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.skinWidth = 0.04f;
            controller.stepOffset = 0.3f;
            player.AddComponent<PortalExampleTraveller>();
            PortalExampleResettable resettable =
                player.AddComponent<PortalExampleResettable>();

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 160f;
            camera.fieldOfView = 72f;
            camera.allowHDR = true;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();

            PortalExampleFirstPersonController firstPerson =
                player.AddComponent<PortalExampleFirstPersonController>();
            PortalExampleGrabber grabber =
                player.AddComponent<PortalExampleGrabber>();

            SerializedObject serializedResettable = new SerializedObject(resettable);
            serializedResettable.FindProperty("resetPoint").objectReferenceValue =
                resetPoint;
            serializedResettable.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject serializedController = new SerializedObject(firstPerson);
            serializedController.FindProperty("view").objectReferenceValue =
                cameraObject.transform;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject serializedGrabber = new SerializedObject(grabber);
            serializedGrabber.FindProperty("view").objectReferenceValue =
                cameraObject.transform;
            serializedGrabber.ApplyModifiedPropertiesWithoutUndo();
            return firstPerson;
        }

        private static void CreateTestCube(
            Transform parent,
            Transform resetPoint,
            Material panel,
            Material metal)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Weighted Storage Cube";
            cube.transform.SetParent(parent, false);
            cube.transform.SetPositionAndRotation(
                resetPoint.position,
                resetPoint.rotation);
            cube.transform.localScale = Vector3.one * 0.82f;
            cube.GetComponent<Renderer>().sharedMaterial = panel;
            Rigidbody body = cube.AddComponent<Rigidbody>();
            body.mass = 2.5f;
            body.drag = 0.15f;
            body.angularDrag = 0.65f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            cube.AddComponent<PortalExamplePickup>();
            cube.AddComponent<PortalExampleTraveller>();
            PortalExampleResettable resettable =
                cube.AddComponent<PortalExampleResettable>();

            SerializedObject serializedResettable = new SerializedObject(resettable);
            serializedResettable.FindProperty("resetPoint").objectReferenceValue =
                resetPoint;
            serializedResettable.ApplyModifiedPropertiesWithoutUndo();

            for (int side = 0; side < 4; side++)
            {
                GameObject corner = GameObject.CreatePrimitive(PrimitiveType.Cube);
                corner.name = "Metal Corner " + side;
                corner.transform.SetParent(cube.transform, false);
                float x = side % 2 == 0 ? -0.43f : 0.43f;
                float z = side < 2 ? -0.43f : 0.43f;
                corner.transform.localPosition = new Vector3(x, 0f, z);
                corner.transform.localScale = new Vector3(0.12f, 0.82f, 0.12f);
                corner.GetComponent<Renderer>().sharedMaterial = metal;
                UnityEngine.Object.DestroyImmediate(corner.GetComponent<Collider>());
            }
        }

        private static PortalExampleDoor CreateExitDoor(
            Transform parent,
            Material metal,
            Material goal)
        {
            GameObject root = new GameObject("Test Chamber Exit");
            root.transform.SetParent(parent, false);

            CreateCube(root.transform, "Left Door Frame",
                new Vector3(3.68f, 1.85f, 12.75f),
                new Vector3(0.24f, 3.7f, 0.7f), metal);
            CreateCube(root.transform, "Right Door Frame",
                new Vector3(6.32f, 1.85f, 12.75f),
                new Vector3(0.24f, 3.7f, 0.7f), metal);
            CreateCube(root.transform, "Top Door Frame",
                new Vector3(5f, 3.58f, 12.75f),
                new Vector3(2.88f, 0.24f, 0.7f), metal);

            GameObject movingDoor = CreateCube(root.transform, "Moving Door",
                new Vector3(5f, 1.72f, 12.78f),
                new Vector3(2.4f, 3.42f, 0.42f), metal);
            CreateCube(movingDoor.transform, "Exit Indicator",
                new Vector3(0f, 0.32f, -0.54f),
                new Vector3(0.65f, 0.16f, 0.08f), goal, false, true);

            PortalExampleDoor door = root.AddComponent<PortalExampleDoor>();
            SerializedObject serializedDoor = new SerializedObject(door);
            serializedDoor.FindProperty("movingPart").objectReferenceValue =
                movingDoor.transform;
            serializedDoor.ApplyModifiedPropertiesWithoutUndo();
            return door;
        }

        private static PortalExampleFloorButton CreateFloorButton(
            Transform parent,
            Material button,
            Material metal,
            PortalExampleDoor door)
        {
            GameObject root = new GameObject("Weighted Floor Button");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(5f, 0f, 8.2f);

            CreateCylinder(root.transform, "Button Base",
                new Vector3(0f, 0.08f, 0f),
                new Vector3(1.25f, 0.08f, 1.25f), metal);
            GameObject top = CreateCylinder(root.transform, "Button Top",
                new Vector3(0f, 0.23f, 0f),
                new Vector3(0.92f, 0.11f, 0.92f), button);

            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.7f, 0f);
            trigger.size = new Vector3(1.8f, 1.4f, 1.8f);

            PortalExampleFloorButton floorButton =
                root.AddComponent<PortalExampleFloorButton>();
            SerializedObject serializedButton = new SerializedObject(floorButton);
            serializedButton.FindProperty("buttonTop").objectReferenceValue =
                top.transform;
            serializedButton.FindProperty("controlledDoor").objectReferenceValue =
                door;
            serializedButton.ApplyModifiedPropertiesWithoutUndo();
            return floorButton;
        }

        private static void CreateHud(
            GameObject player,
            PortalExampleFloorButton floorButton)
        {
            PortalExampleHud hud = player.AddComponent<PortalExampleHud>();
            SerializedObject serializedHud = new SerializedObject(hud);
            serializedHud.FindProperty("floorButton").objectReferenceValue =
                floorButton;
            serializedHud.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateLighting(Transform parent)
        {
            GameObject sunObject = new GameObject("Soft Directional Light");
            sunObject.transform.SetParent(parent, false);
            sunObject.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.72f, 0.82f, 1f);
            sun.intensity = 0.72f;
            sun.shadows = LightShadows.Soft;

            CreatePointLight(parent, "Entry Cool Light",
                new Vector3(-2f, 4.5f, -5f),
                new Color(0.55f, 0.78f, 1f), 7f, 8f);
            CreatePointLight(parent, "Portal Chamber Light",
                new Vector3(4f, 4.7f, 6f),
                new Color(0.65f, 0.82f, 1f), 8f, 9f);
            CreatePointLight(parent, "Goal Warm Light",
                new Vector3(5f, 4.2f, 14.6f),
                new Color(0.7f, 1f, 0.78f), 6f, 8f);

            GameObject probeObject = new GameObject("Chamber Reflection Probe");
            probeObject.transform.SetParent(parent, false);
            probeObject.transform.position = new Vector3(2.5f, 2.4f, 4f);
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(22f, 6f, 26f);
            probe.intensity = 0.7f;
        }

        private static void CreatePointLight(
            Transform parent,
            string name,
            Vector3 position,
            Color color,
            float intensity,
            float range)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.position = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
        }

        private static void ConfigureRenderSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.17f, 0.23f, 0.3f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.12f, 0.15f);
            RenderSettings.ambientGroundColor = new Color(0.025f, 0.03f, 0.035f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.06f, 0.085f, 0.11f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.008f;
        }

        private static Transform CreateMarker(
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation)
        {
            GameObject marker = new GameObject(name);
            marker.transform.SetParent(parent, false);
            marker.transform.SetPositionAndRotation(position, rotation);
            return marker.transform;
        }

        private static GameObject CreateCube(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 scale,
            Material material,
            bool collider = true,
            bool localCoordinates = false)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            if (localCoordinates)
            {
                cube.transform.localPosition = position;
            }
            else
            {
                cube.transform.position = position;
            }

            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider)
            {
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            }
            else
            {
                GameObjectUtility.SetStaticEditorFlags(
                    cube,
                    StaticEditorFlags.BatchingStatic
                    | StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic
                    | StaticEditorFlags.ReflectionProbeStatic);
            }

            return cube;
        }

        private static GameObject CreateCylinder(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localScale = localScale;
            cylinder.GetComponent<Renderer>().sharedMaterial = material;
            return cylinder;
        }

        private static void CreateSign(
            Transform parent,
            string name,
            string text,
            Vector3 position,
            Quaternion rotation,
            Color color)
        {
            GameObject sign = new GameObject(name);
            sign.transform.SetParent(parent, false);
            sign.transform.SetPositionAndRotation(position, rotation);
            TextMesh textMesh = sign.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.16f;
            textMesh.fontSize = 48;
            textMesh.color = color;
        }

        private static Material CreatePortalMaterial(
            string path,
            Shader shader,
            Color edgeColor,
            Color interiorTint)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_EdgeColor", edgeColor);
            material.SetColor("_InteriorTint", interiorTint);
            material.SetFloat("_EdgeWidth", 0.16f);
            material.SetFloat("_PulseSpeed", 2.5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static void EnsurePortalAssets(
            out Material bluePortal,
            out Material orangePortal,
            out Mesh portalRing)
        {
            EnsureFolder(ProjectAssetPaths.Folders.PortalExampleMaterials);
            EnsureFolder(ProjectAssetPaths.Folders.PortalExampleModels);

            Shader portalShader = AssetDatabase.LoadAssetAtPath<Shader>(
                ProjectAssetPaths.Shaders.PortalExampleSurface);
            if (portalShader == null)
            {
                throw new MissingReferenceException(
                    "Missing portal shader at registered path: "
                    + ProjectAssetPaths.Shaders.PortalExampleSurface);
            }
            Shader clippedLitShader = AssetDatabase.LoadAssetAtPath<Shader>(
                ProjectAssetPaths.Shaders.PortalExampleClippedLit);
            if (clippedLitShader == null)
            {
                throw new MissingReferenceException(
                    "Missing clipped portal traveller shader at registered "
                    + "path: "
                    + ProjectAssetPaths.Shaders.PortalExampleClippedLit);
            }

            bluePortal = CreatePortalMaterial(
                ProjectAssetPaths.Materials.PortalExampleBlue,
                portalShader,
                new Color(0.02f, 0.55f, 4.5f, 1f),
                new Color(0.72f, 0.9f, 1f, 1f));
            orangePortal = CreatePortalMaterial(
                ProjectAssetPaths.Materials.PortalExampleOrange,
                portalShader,
                new Color(5f, 0.42f, 0.015f, 1f),
                new Color(1f, 0.82f, 0.58f, 1f));
            portalRing = CreatePortalRingMesh();
        }

        private static Material CreateLitMaterial(
            string path,
            Color baseColor,
            float metallic,
            float smoothness,
            Color? emission = null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new MissingReferenceException(
                    "Universal Render Pipeline/Lit shader is unavailable.");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh CreatePortalRingMesh()
        {
            const int segmentCount = 64;
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                ProjectAssetPaths.Models.PortalExampleRing);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Portal Example Circular Ring" };
                AssetDatabase.CreateAsset(
                    mesh,
                    ProjectAssetPaths.Models.PortalExampleRing);
            }

            Vector3[] vertices = new Vector3[segmentCount * 2];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[segmentCount * 6];
            for (int index = 0; index < segmentCount; index++)
            {
                float angle = Mathf.PI * 2f * index / segmentCount;
                Vector2 direction = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));
                int vertex = index * 2;
                vertices[vertex] = new Vector3(
                    direction.x * 1.24f,
                    direction.y * 1.24f,
                    0f);
                vertices[vertex + 1] = new Vector3(
                    direction.x * 1.08f,
                    direction.y * 1.08f,
                    0f);
                normals[vertex] = Vector3.forward;
                normals[vertex + 1] = Vector3.forward;
                uvs[vertex] = direction * 0.5f + Vector2.one * 0.5f;
                uvs[vertex + 1] = direction * 0.43f + Vector2.one * 0.5f;

                int nextVertex = ((index + 1) % segmentCount) * 2;
                int triangle = index * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = nextVertex;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = nextVertex;
                triangles[triangle + 4] = nextVertex + 1;
                triangles[triangle + 5] = vertex + 1;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(folder)
                ?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    "Cannot create asset folder without a parent: " + folder);
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
