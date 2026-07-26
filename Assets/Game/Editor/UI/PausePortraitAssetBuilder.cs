using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Supernova.UI.Editor
{
    public static class PausePortraitAssetBuilder
    {
        private const string SourcePrefabPath =
            "Assets/3rd/P05_Aki & Mika/Model_DATA/Prefab/NoPhysics_Plain/P05_ASTRO_Aki_Plain Variant.prefab";
        private static readonly string[] DefaultPosePaths =
        {
            "Assets/3rd/Suriyun/Animations/Anim@Angpose.fbx",
            "Assets/3rd/Suriyun/Animations/Anim@CastspellC.fbx",
            "Assets/3rd/Suriyun/Animations/Anim@Atk4.fbx",
            "Assets/3rd/Suriyun/Animations/Anim@Thinking.fbx"
        };
        private static readonly float[] DefaultHoldTimes = { 0.995f, 0.72f, 0.48f, 0.78f };
        private static readonly float[] DefaultYawValues = { -8f, -12f, -18f, 10f };
        private const string ShaderName = "Supernova/UI/PauseSilhouette";
        private const string ResourceFolder = "Assets/Game/Resources/Pause";
        private const string PortraitPath = ResourceFolder + "/PausePortrait.prefab";
        private const string ControllerPath = ResourceFolder + "/PausePortrait.controller";
        private const string BodyMaterialPath = ResourceFolder + "/PauseSilhouetteBody.mat";
        private const string BackgroundMaterialPath =
            ResourceFolder + "/PauseSilhouetteBackground.mat";
        private const string SettingsPath = ResourceFolder + "/PausePortraitSettings.asset";
        private const string PreviewScenePath =
            "Assets/Game/Scenes/PausePortraitPreview.unity";

        [MenuItem("Tools/Supernova/UI/Rebuild Pause Portrait Assets")]
        public static void Rebuild()
        {
            EnsureFolder("Assets/Game", "Resources");
            EnsureFolder("Assets/Game/Resources", "Pause");

            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            AnimationClip[] defaultPoses = DefaultPosePaths
                .Select(LoadFirstClip)
                .ToArray();
            AnimationClip pose = defaultPoses[0];
            Shader shader = Shader.Find(ShaderName);
            if (sourcePrefab == null || pose == null || shader == null)
            {
                Debug.LogError(
                    "Pause portrait build failed. Check the Aki prefab, Angpose clip, and silhouette shader.");
                return;
            }

            ReplaceAssetCopy(SourcePrefabPath, PortraitPath);
            UnityEditor.Animations.AnimatorController controller = CreateController(pose);
            CreateMaterial(
                BodyMaterialPath,
                shader,
                new Color32(247, 237, 218, 255),
                new Color32(10, 7, 11, 255),
                0.008f);
            CreateMaterial(
                BackgroundMaterialPath,
                shader,
                new Color32(22, 0, 13, 255),
                new Color32(22, 0, 13, 255),
                0f);
            CreateOrUpdateSettings(controller, defaultPoses);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Rebuilt pause portrait resources with Suriyun Angpose.");
        }

        [MenuItem("Tools/Supernova/UI/Select Pause Portrait Settings")]
        public static void SelectSettings()
        {
            Selection.activeObject =
                AssetDatabase.LoadAssetAtPath<PausePortraitSettings>(SettingsPath);
        }

        [MenuItem("Tools/Supernova/UI/Rebuild Pause Portrait Preview Scene")]
        public static void RebuildPreviewScene()
        {
            EnsureFolder("Assets/Game", "Scenes");
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(22, 0, 13, 255);
            cameraObject.transform.position = new Vector3(0f, 1f, -10f);

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            GameObject previewObject = new GameObject("Pause Portrait Preview");
            previewObject.AddComponent<PausePortraitPreviewController>();

            EditorSceneManager.SaveScene(scene, PreviewScenePath);
            Selection.activeGameObject = previewObject;
            Debug.Log(
                "Rebuilt pause portrait preview scene. Enter Play Mode; press Space to replay.");
        }

        [MenuItem("Tools/Supernova/UI/Open Pause Portrait Preview Scene")]
        public static void OpenPreviewScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath) == null)
            {
                RebuildPreviewScene();
                return;
            }
            EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);
        }

        private static UnityEditor.Animations.AnimatorController CreateController(
            AnimationClip pose)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            UnityEditor.Animations.AnimatorController controller =
                UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(
                    ControllerPath);
            UnityEditor.Animations.AnimatorStateMachine stateMachine =
                controller.layers[0].stateMachine;
            UnityEditor.Animations.AnimatorState state =
                stateMachine.AddState("PausePose");
            state.motion = pose;
            state.writeDefaultValues = true;
            stateMachine.defaultState = state;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void CreateOrUpdateSettings(
            RuntimeAnimatorController controller,
            AnimationClip[] defaultPoses)
        {
            PausePortraitSettings settings =
                AssetDatabase.LoadAssetAtPath<PausePortraitSettings>(SettingsPath);
            bool created = settings == null;
            if (created)
            {
                settings = ScriptableObject.CreateInstance<PausePortraitSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            SerializedObject serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("portraitPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(PortraitPath);
            serializedSettings.FindProperty("poseController").objectReferenceValue = controller;
            SerializedProperty posesProperty = serializedSettings.FindProperty("pausePoses");
            if (posesProperty.arraySize == 0)
            {
                posesProperty.arraySize = defaultPoses.Length;
                for (int i = 0; i < defaultPoses.Length; i++)
                {
                    SerializedProperty entry = posesProperty.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("displayName").stringValue =
                        defaultPoses[i] != null ? defaultPoses[i].name : $"Pause Pose {i + 1}";
                    entry.FindPropertyRelative("clip").objectReferenceValue = defaultPoses[i];
                    entry.FindPropertyRelative("holdNormalizedTime").floatValue =
                        DefaultHoldTimes[i];
                    entry.FindPropertyRelative("portraitYaw").floatValue =
                        DefaultYawValues[i];
                }
            }
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        private static AnimationClip LoadFirstClip(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__"));
        }

        private static void CreateMaterial(
            string path,
            Shader shader,
            Color color,
            Color outlineColor,
            float outlineWidth)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_Color", color);
            material.SetColor("_OutlineColor", outlineColor);
            material.SetFloat("_OutlineWidth", outlineWidth);
            EditorUtility.SetDirty(material);
        }

        private static void ReplaceAssetCopy(string sourcePath, string destinationPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(destinationPath) != null)
                AssetDatabase.DeleteAsset(destinationPath);
            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                Debug.LogError($"Could not copy pause portrait prefab to {destinationPath}.");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string fullPath = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(fullPath))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
