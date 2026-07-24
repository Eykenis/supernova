using System;
using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    [DisallowMultipleComponent]
    public sealed class MinecraftCaveGallery : MonoBehaviour
    {
        private static readonly MinecraftCaveType[] DisplayTypes =
        {
            MinecraftCaveType.Cheese,
            MinecraftCaveType.Spaghetti,
            MinecraftCaveType.Noodle,
            MinecraftCaveType.Pillar,
            MinecraftCaveType.Combined,
        };

        private static readonly Vector3[] DisplayPositions =
        {
            new Vector3(-14f, 7f, 0f),
            new Vector3(0f, 7f, 0f),
            new Vector3(14f, 7f, 0f),
            new Vector3(-7f, -7f, 0f),
            new Vector3(7f, -7f, 0f),
        };

        private static readonly Color[] DisplayColors =
        {
            new Color(0.90f, 0.62f, 0.20f),
            new Color(0.77f, 0.25f, 0.20f),
            new Color(0.12f, 0.63f, 0.60f),
            new Color(0.52f, 0.61f, 0.43f),
            new Color(0.28f, 0.48f, 0.78f),
        };

        [Header("Generation")]
        [SerializeField] private int seed = 18731;
        [SerializeField, Min(0.05f)] private float voxelSize = 0.38f;
        [SerializeField] private bool cutaway = true;
        [SerializeField] private MinecraftCaveSettings settings = new MinecraftCaveSettings();

        [Header("Rendering")]
        [SerializeField] private bool castShadows = true;
        [SerializeField] private bool generateColliders;

        private readonly List<GameObject> generatedObjects = new List<GameObject>();
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private readonly List<Material> generatedMaterials = new List<Material>();
        private GUIStyle headingStyle;
        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;

        public int Seed => seed;

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                Rebuild();
            }
        }

        public void Rebuild()
        {
            ClearGeneratedObjects();
            var densityField = new MinecraftCaveDensityField(seed, settings);
            int totalTriangles = 0;

            for (int index = 0; index < DisplayTypes.Length; index++)
            {
                MinecraftCaveType type = DisplayTypes[index];
                VoxelVolume volume = BuildVolume(densityField, type);
                VoxelMeshData meshData = MarchingCubesMesher.Build(volume, 0f, voxelSize);
                totalTriangles += meshData.TriangleCount;
                CreateDisplayObject(type, index, meshData);
            }

            Debug.Log(
                $"Minecraft cave gallery generated seed {seed}: "
                + $"{DisplayTypes.Length} fields, {totalTriangles:N0} triangles.",
                this);
        }

        public void SetSeedAndRebuild(int newSeed)
        {
            seed = newSeed;
            Rebuild();
        }

        private VoxelVolume BuildVolume(
            MinecraftCaveDensityField densityField,
            MinecraftCaveType type)
        {
            var volume = new VoxelVolume();
            MinecraftCaveVolumeGenerator.FillDisplayVolume(
                volume,
                densityField,
                type,
                cutaway);
            return volume;
        }

        private void CreateDisplayObject(
            MinecraftCaveType type,
            int index,
            VoxelMeshData meshData)
        {
            var display = new GameObject(type.ToString());
            display.transform.SetParent(transform, false);
            display.transform.localPosition = DisplayPositions[index];
            generatedObjects.Add(display);

            var surface = new GameObject("Surface");
            surface.transform.SetParent(display.transform, false);
            float sideLength = (VoxelVolume.Size - 1) * voxelSize;
            surface.transform.localPosition = Vector3.one * (-sideLength * 0.5f);

            Mesh mesh = meshData.CreateMesh($"Minecraft Cave - {type}");
            mesh.hideFlags = HideFlags.DontSave;
            generatedMeshes.Add(mesh);

            Material material = CreateMaterial(type.ToString(), DisplayColors[index]);
            generatedMaterials.Add(material);

            MeshFilter filter = surface.AddComponent<MeshFilter>();
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            if (generateColliders)
            {
                MeshCollider collider = surface.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }
        }

        private static Material CreateMaterial(string displayName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                throw new InvalidOperationException("No compatible lit shader is available.");
            }

            var material = new Material(shader)
            {
                name = $"{displayName} Cave Material",
                hideFlags = HideFlags.DontSave,
            };
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
                material.SetFloat("_Smoothness", 0.18f);
            }
            return material;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            EnsureGuiStyles();
            GUI.Label(new Rect(24f, 18f, 420f, 34f), "MINECRAFT DENSITY CAVES", headingStyle);
            GUI.Label(new Rect(26f, 50f, 180f, 24f), $"WORLD SEED  {seed}", labelStyle);

            float buttonWidth = 104f;
            float gap = 8f;
            float right = Screen.width - 24f;
            if (GUI.Button(
                    new Rect(right - buttonWidth * 2f - gap, 22f, buttonWidth, 32f),
                    "PREVIOUS SEED",
                    buttonStyle))
            {
                SetSeedAndRebuild(seed - 1);
            }
            if (GUI.Button(
                    new Rect(right - buttonWidth, 22f, buttonWidth, 32f),
                    "NEXT SEED",
                    buttonStyle))
            {
                SetSeedAndRebuild(seed + 1);
            }

            Camera activeCamera = Camera.main;
            for (int index = 0; index < DisplayTypes.Length; index++)
            {
                if (activeCamera == null)
                {
                    break;
                }

                Vector3 labelWorldPosition = transform.TransformPoint(
                    DisplayPositions[index] + Vector3.down * 6.7f);
                Vector3 labelScreenPosition = activeCamera.WorldToScreenPoint(labelWorldPosition);
                if (labelScreenPosition.z <= 0f)
                {
                    continue;
                }

                GUIStyle style = new GUIStyle(labelStyle)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = DisplayColors[index] },
                };
                GUI.Label(
                    new Rect(
                        labelScreenPosition.x - 70f,
                        Screen.height - labelScreenPosition.y - 12f,
                        140f,
                        28f),
                    DisplayTypes[index].ToString().ToUpperInvariant(),
                    style);
            }
        }

        private void EnsureGuiStyles()
        {
            if (headingStyle != null)
            {
                return;
            }

            headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.94f, 0.94f, 0.91f) },
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.76f, 0.78f, 0.79f) },
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fixedHeight = 32f,
            };
        }

        private void OnDisable()
        {
            ClearGeneratedObjects();
        }

        private void ClearGeneratedObjects()
        {
            foreach (GameObject generatedObject in generatedObjects)
            {
                DestroyGeneratedObject(generatedObject);
            }
            generatedObjects.Clear();

            foreach (Mesh mesh in generatedMeshes)
            {
                DestroyGeneratedObject(mesh);
            }
            generatedMeshes.Clear();

            foreach (Material material in generatedMaterials)
            {
                DestroyGeneratedObject(material);
            }
            generatedMaterials.Clear();
        }

        private static void DestroyGeneratedObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
