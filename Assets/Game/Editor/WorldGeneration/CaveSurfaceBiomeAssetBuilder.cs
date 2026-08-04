using System;
using System.Collections.Generic;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Supernova.MinecraftCaves.Editor
{
    public static class CaveSurfaceBiomeAssetBuilder
    {
        [MenuItem("Tools/Minecraft Caves/Rebuild Surface Biome Assets")]
        public static void RebuildSurfaceBiomeAssets()
        {
            EnsureFolder(ProjectAssetPaths.Folders.Biomes);
            EnsureFolder(ProjectAssetPaths.Folders.SurfaceBrushes);
            EnsureFolder(ProjectAssetPaths.Folders.SurfaceContentPrefabs);
            EnsureFolder(ProjectAssetPaths.Folders.SurfaceContentMaterials);
            EnsureFolder(ProjectAssetPaths.Folders.SurfaceContentModels);
            EnsureFolder(ProjectAssetPaths.Folders.VegetationMaterials);
            EnsureFolder(ProjectAssetPaths.Folders.VegetationModels);

            Material grassMaterial = EnsureMaterial(
                ProjectAssetPaths.Materials.GrassSurfacePlaceholder,
                new Color(0.16f, 0.55f, 0.12f));
            Material vineMaterial = EnsureMaterial(
                ProjectAssetPaths.Materials.VineSurfacePlaceholder,
                new Color(0.08f, 0.32f, 0.07f));
            Mesh grassMesh = EnsurePlaceholderMesh(
                ProjectAssetPaths.Models.GrassSurfacePlaceholder,
                CaveSurfacePlaceholderKind.Grass);
            RebuildGrassPrefab(grassMesh, grassMaterial);
            GameObject vinePrefab = RebuildVinePrefab(vineMaterial);

            // Stylised blade meshes and their dedicated shader replace the flat
            // placeholder quads for the instanced grass brush. The placeholder
            // mesh and material above are still produced because the prefab-mode
            // path continues to reference them.
            Material bladeMaterial = EnsureShaderMaterial(
                ProjectAssetPaths.Materials.CaveGrassBlade,
                CaveVegetationShaderNames.CaveGrassBlade);
            Mesh bladeLod0 = EnsureGeneratedBladeMesh(
                ProjectAssetPaths.Models.CaveGrassBladeLod0,
                CaveGrassBladeMeshSettings.Lod0);
            Mesh bladeLod1 = EnsureGeneratedBladeMesh(
                ProjectAssetPaths.Models.CaveGrassBladeLod1,
                CaveGrassBladeMeshSettings.Lod1);
            Mesh bladeLod2 = EnsureGeneratedBladeMesh(
                ProjectAssetPaths.Models.CaveGrassBladeLod2,
                CaveGrassBladeMeshSettings.Lod2);

            VoxelTypeDefinition stone = LoadRequired<VoxelTypeDefinition>(
                ProjectAssetPaths.Config.StoneVoxel);
            CaveSurfaceBrushDefinition grassBrush = EnsureAsset<
                CaveSurfaceBrushDefinition>(
                ProjectAssetPaths.Config.GrassSurfaceBrush,
                "Grass");
            grassBrush.ConfigureInstanced(
                bladeLod0,
                bladeMaterial,
                new[] { stone },
                CaveSurfaceOrientation.Upward,
                1009,
                6f,
                0.6f,
                0.4f,
                0.015f,
                new Vector2(0.75f, 1.3f),
                new Vector2(0.7f, 1.2f),
                ShadowCastingMode.Off,
                true,
                45f);
            grassBrush.ConfigureVegetation(
                new[]
                {
                    new CaveSurfaceLodTier(bladeLod0, 12f),
                    new CaveSurfaceLodTier(bladeLod1, 25f),
                    new CaveSurfaceLodTier(bladeLod2, 0f),
                },
                12f,
                0.65f,
                2.5f,
                3f,
                new Vector2(0.72f, 1.35f),
                new Vector2(0.85f, 1.2f),
                35f,
                0.16f,
                0.35f,
                0.45f,
                new Vector2(1f, 0.35f),
                2f);
            EditorUtility.SetDirty(grassBrush);

            CaveSurfaceBrushDefinition vineBrush = EnsureAsset<
                CaveSurfaceBrushDefinition>(
                ProjectAssetPaths.Config.VineSurfaceBrush,
                "Vine");
            vineBrush.Configure(
                vinePrefab,
                new[] { stone },
                CaveSurfaceOrientation.Downward,
                2029,
                0.02f,
                0.6f,
                0.4f,
                0.01f,
                new Vector2(0.8f, 1.2f),
                new Vector2(0.6f, 1.6f));
            EditorUtility.SetDirty(vineBrush);

            CaveBiomeDefinition grassy = EnsureAsset<CaveBiomeDefinition>(
                ProjectAssetPaths.Config.GrassyCaveBiome,
                "Grassy");
            grassy.Configure(
                "grassy",
                "Grassy",
                new[] { grassBrush, vineBrush });
            grassy.ConfigureVegetationTint(
                new Color(0.055f, 0.184f, 0.078f),
                new Color(0.34f, 0.61f, 0.208f),
                new Color(0.53f, 0.79f, 0.35f),
                0.3f,
                1f);
            EditorUtility.SetDirty(grassy);

            CaveBiomeDefinition bald = EnsureAsset<CaveBiomeDefinition>(
                ProjectAssetPaths.Config.BaldCaveBiome,
                "Bald");
            bald.Configure(
                "bald",
                "Bald",
                Array.Empty<CaveSurfaceBrushDefinition>());
            EditorUtility.SetDirty(bald);

            CaveBiomeCatalog catalog = EnsureAsset<CaveBiomeCatalog>(
                ProjectAssetPaths.Config.CaveBiomeCatalog,
                "DefaultCaveBiomes");
            catalog.Configure(
                0.008f,
                15485863,
                bald,
                new[] { new CaveBiomeSelection(grassy, 0f, 1f) });
            EditorUtility.SetDirty(catalog);

            MinecraftWorldGenerationConfiguration world = LoadRequired<
                MinecraftWorldGenerationConfiguration>(
                ProjectAssetPaths.Config.WorldGeneration);
            var serializedWorld = new SerializedObject(world);
            SerializedProperty catalogProperty =
                serializedWorld.FindProperty("caveBiomeCatalog");
            if (catalogProperty == null)
            {
                throw new InvalidOperationException(
                    "World generation configuration has no caveBiomeCatalog field.");
            }
            catalogProperty.objectReferenceValue = catalog;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(world);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Rebuilt grassy/bald cave biomes, the vine brush, and the "
                + "stylised grass brush with three blade LOD tiers, its shader "
                + "material and the biome vegetation tint.");
        }

        private static GameObject RebuildGrassPrefab(
            Mesh mesh,
            Material material)
        {
            var root = new GameObject("GrassPlaceholder");
            try
            {
                root.AddComponent<VoxelSurfaceAttachment>();
                MeshFilter filter = root.AddComponent<MeshFilter>();
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = material;
                return SavePrefab(
                    root,
                    ProjectAssetPaths.Prefabs.GrassSurfacePlaceholder);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject RebuildVinePrefab(Material material)
        {
            var root = new GameObject("VinePlaceholder");
            try
            {
                root.AddComponent<VoxelSurfaceAttachment>();
                GameObject stem = GameObject.CreatePrimitive(
                    PrimitiveType.Cylinder);
                stem.name = "Stem";
                stem.transform.SetParent(root.transform, false);
                stem.transform.localPosition = new Vector3(0f, 0.65f, 0f);
                stem.transform.localScale = new Vector3(0.025f, 0.65f, 0.025f);
                stem.GetComponent<MeshRenderer>().sharedMaterial = material;
                Object.DestroyImmediate(stem.GetComponent<Collider>());

                CreateLeaf(
                    root.transform,
                    "Leaf_A",
                    new Vector3(0.08f, 0.42f, 0f),
                    Quaternion.Euler(0f, 0f, -30f),
                    material);
                CreateLeaf(
                    root.transform,
                    "Leaf_B",
                    new Vector3(-0.08f, 0.85f, 0.02f),
                    Quaternion.Euler(0f, 0f, 28f),
                    material);
                return SavePrefab(
                    root,
                    ProjectAssetPaths.Prefabs.VineSurfacePlaceholder);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateLeaf(
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation,
            Material material)
        {
            GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = name;
            leaf.transform.SetParent(parent, false);
            leaf.transform.localPosition = position;
            leaf.transform.localRotation = rotation;
            leaf.transform.localScale = new Vector3(0.16f, 0.035f, 0.08f);
            leaf.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(leaf.GetComponent<Collider>());
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Failed to save surface placeholder prefab at " + path);
            }
            return prefab;
        }

        /// <summary>
        /// Creates or retargets a material onto a named shader. Unlike
        /// <see cref="EnsureMaterial"/> this throws rather than falling back to URP
        /// Lit: a silent fallback would still render green grass and would hide a
        /// shader compile error behind almost-correct output.
        /// </summary>
        private static Material EnsureShaderMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Required shader is missing or failed to compile: "
                    + shaderName);
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(path),
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Writes a generated blade mesh, reusing the existing asset when present
        /// so its GUID survives and brush references stay intact.
        /// </summary>
        private static Mesh EnsureGeneratedBladeMesh(
            string path,
            in CaveGrassBladeMeshSettings settings)
        {
            Mesh generated = CaveGrassBladeMeshBuilder.Build(
                settings,
                System.IO.Path.GetFileNameWithoutExtension(path));
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            string meshName = generated.name;
            EditorUtility.CopySerialized(generated, mesh);
            mesh.name = meshName;
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Material EnsureMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No supported lit shader is available for placeholders.");
            }
            if (material == null)
            {
                material = new Material(shader);
                material.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }
            material.color = color;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh EnsurePlaceholderMesh(
            string path,
            CaveSurfacePlaceholderKind kind)
        {
            Mesh generated = CaveSurfacePlaceholderVisual
                .CreatePlaceholderMesh(kind);
            generated.name = System.IO.Path.GetFileNameWithoutExtension(path);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            EditorUtility.CopySerialized(generated, mesh);
            mesh.name = generated.name;
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static T EnsureAsset<T>(string path, string assetName)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            asset.name = assetName;
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static T LoadRequired<T>(string path)
            where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    "Required centralized asset is missing: " + path);
            }
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)
                ?.Replace('\\', '/');
            string child = System.IO.Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent)
                || string.IsNullOrWhiteSpace(child))
            {
                throw new InvalidOperationException(
                    "Cannot create invalid asset folder: " + path);
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
