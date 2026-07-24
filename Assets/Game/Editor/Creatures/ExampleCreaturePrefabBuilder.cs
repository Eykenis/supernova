using System;
using Supernova.MinecraftCaves.Creatures;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.MinecraftCaves.Editor.Creatures
{
    public static class ExampleCreaturePrefabBuilder
    {
        private const float VoxelSize = 0.42f;
        private const string AssetFolder = "Assets/Game/CreatureAssets";
        private const string PrefabPath = AssetFolder + "/ExampleCaveCreature.prefab";
        private const string ShapePath = AssetFolder + "/ExampleCaveCreatureVoxelShape.asset";
        private const string BodyMaterialPath = AssetFolder + "/ExampleCreatureBody.mat";
        private const string DarkMaterialPath = AssetFolder + "/ExampleCreatureDark.mat";
        private const string FaceMaterialPath = AssetFolder + "/ExampleCreatureFace.mat";
        private const string AccentMaterialPath = AssetFolder + "/ExampleCreatureAccent.mat";
        private const string EyeMaterialPath = AssetFolder + "/ExampleCreatureEyes.mat";

        [MenuItem("Tools/Minecraft Caves/Rebuild Example Creature Prefab")]
        public static void Rebuild()
        {
            EnsureAssetFolder();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                throw new InvalidOperationException("No compatible lit shader is available.");
            }

            Material body = CreateOrUpdateMaterial(
                BodyMaterialPath,
                shader,
                new Color(0.18f, 0.46f, 0.38f),
                0.24f);
            Material dark = CreateOrUpdateMaterial(
                DarkMaterialPath,
                shader,
                new Color(0.075f, 0.09f, 0.10f),
                0.15f);
            Material face = CreateOrUpdateMaterial(
                FaceMaterialPath,
                shader,
                new Color(0.72f, 0.76f, 0.65f),
                0.3f);
            Material accent = CreateOrUpdateMaterial(
                AccentMaterialPath,
                shader,
                new Color(0.62f, 0.19f, 0.13f),
                0.2f);
            Material eyes = CreateOrUpdateMaterial(
                EyeMaterialPath,
                shader,
                new Color(1f, 0.63f, 0.12f),
                0.4f,
                true);

            GameObject root;
            bool editingExistingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            if (editingExistingPrefab)
            {
                root = PrefabUtility.LoadPrefabContents(PrefabPath);
            }
            else
            {
                root = new GameObject("Example Cave Creature");
            }

            try
            {
                ConfigureRuntimeComponents(root, editingExistingPrefab);
                RebuildVisuals(root.transform, body, dark, face, accent, eyes);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    PrefabPath,
                    out bool success);
                if (!success || prefab == null)
                {
                    throw new InvalidOperationException($"Failed to save {PrefabPath}.");
                }

                AssetDatabase.SaveAssets();
                Selection.activeObject = prefab;
                Debug.Log(
                    $"Created example cave creature at {PrefabPath}. "
                    + "Collider setup was preserved and remains editor-authored.",
                    prefab);
            }
            finally
            {
                if (editingExistingPrefab)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static void ConfigureRuntimeComponents(
            GameObject root,
            bool editingExistingPrefab)
        {
            Rigidbody rigidbody = root.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = root.AddComponent<Rigidbody>();
                rigidbody.mass = 1f;
                rigidbody.useGravity = true;
                rigidbody.isKinematic = false;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.constraints = RigidbodyConstraints.FreezeRotationX
                    | RigidbodyConstraints.FreezeRotationZ;
            }

            CreatureVoxelShapeAuthoring authoring =
                root.GetComponent<CreatureVoxelShapeAuthoring>();
            if (authoring == null)
            {
                authoring = root.AddComponent<CreatureVoxelShapeAuthoring>();
                CreatureVoxelShape existingShape =
                    AssetDatabase.LoadAssetAtPath<CreatureVoxelShape>(ShapePath);
                authoring.Configure(null, existingShape, VoxelSize);
            }

            CreaturePhysicsMotor motor = root.GetComponent<CreaturePhysicsMotor>();
            if (motor == null)
            {
                motor = root.AddComponent<CreaturePhysicsMotor>();
            }
            motor.Configure(rigidbody, VoxelSize);

            if (root.GetComponent<CreatureBehaviorAgent>() == null)
            {
                root.AddComponent<CreatureBehaviorAgent>();
            }

            if (!editingExistingPrefab && root.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                Debug.LogWarning(
                    $"{root.name} was created without a Collider. Configure one in the "
                    + "Prefab editor before using the dynamic Rigidbody.",
                    root);
            }
        }

        private static void RebuildVisuals(
            Transform root,
            Material body,
            Material dark,
            Material face,
            Material accent,
            Material eyes)
        {
            string[] visualNames =
            {
                "Left Foot", "Right Foot", "Torso", "Left Arm", "Right Arm",
                "Scarf", "Head", "Face Plate", "Left Eye", "Right Eye", "Mouth",
            };
            for (int i = 0; i < visualNames.Length; i++)
            {
                Transform existing = root.Find(visualNames[i]);
                if (existing != null)
                {
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }
            }

            CreateCube(root, "Left Foot", new Vector3(-0.18f, 0.12f, 0f),
                new Vector3(0.22f, 0.24f, 0.34f), dark);
            CreateCube(root, "Right Foot", new Vector3(0.18f, 0.12f, 0f),
                new Vector3(0.22f, 0.24f, 0.34f), dark);
            CreateCube(root, "Torso", new Vector3(0f, 0.68f, 0f),
                new Vector3(0.58f, 0.9f, 0.46f), body);
            CreateCube(root, "Left Arm", new Vector3(-0.35f, 0.72f, 0f),
                new Vector3(0.14f, 0.68f, 0.20f), dark);
            CreateCube(root, "Right Arm", new Vector3(0.35f, 0.72f, 0f),
                new Vector3(0.14f, 0.68f, 0.20f), dark);
            CreateCube(root, "Scarf", new Vector3(0f, 1.08f, 0f),
                new Vector3(0.66f, 0.13f, 0.52f), accent);
            CreateCube(root, "Head", new Vector3(0f, 1.36f, 0f),
                new Vector3(0.62f, 0.50f, 0.52f), body);
            CreateCube(root, "Face Plate", new Vector3(0f, 1.36f, 0.275f),
                new Vector3(0.42f, 0.27f, 0.05f), face);
            CreateCube(root, "Left Eye", new Vector3(-0.115f, 1.41f, 0.307f),
                new Vector3(0.075f, 0.075f, 0.035f), eyes, false);
            CreateCube(root, "Right Eye", new Vector3(0.115f, 1.41f, 0.307f),
                new Vector3(0.075f, 0.075f, 0.035f), eyes, false);
            CreateCube(root, "Mouth", new Vector3(0f, 1.29f, 0.309f),
                new Vector3(0.16f, 0.035f, 0.03f), dark, false);
        }

        private static void CreateCube(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool castShadows = true)
        {
            var cube = new GameObject(name);
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;

            Mesh cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (cubeMesh == null)
            {
                throw new InvalidOperationException("Unity built-in Cube mesh is unavailable.");
            }

            MeshFilter filter = cube.AddComponent<MeshFilter>();
            filter.sharedMesh = cubeMesh;
            MeshRenderer renderer = cube.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private static Material CreateOrUpdateMaterial(
            string path,
            Shader shader,
            Color color,
            float smoothness,
            bool emission = false)
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

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.8f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureAssetFolder()
        {
            if (!AssetDatabase.IsValidFolder(AssetFolder))
            {
                AssetDatabase.CreateFolder("Assets/Game", "CreatureAssets");
            }
        }
    }
}
