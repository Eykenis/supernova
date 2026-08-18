using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Editor
{
    [InitializeOnLoad]
    internal sealed class PlayerShaderBuildGuard : IPreprocessBuildWithReport
    {
        static PlayerShaderBuildGuard()
        {
            EditorApplication.delayCall +=
                EnsureEditorShaderConfiguration;
        }

        private const string LilToonOptimizationDisableDefine =
            "LILTOON_DISABLE_OPTIMIZATION";
        private const string LilToonShaderName = "lilToon";
        private const string SoftFalloffLitShaderName =
            "Supernova/Lighting/Soft Falloff Lit";
        private const string CrystalOreLitShaderName =
            VoxelShaderNames.CrystalOreLit;
        private const string CrystalOreLitCompatibleShaderName =
            VoxelShaderNames.CrystalOreLitCompatible;
        private const string CrystalOreSparkleOverlayShaderName =
            VoxelShaderNames.CrystalOreSparkleOverlay;
        private const string PortalClippedLitShaderName =
            "Supernova/PortalExample/Clipped Lit";
        private const string PortalSurfaceShaderName =
            "Supernova/PortalExample/Surface";
        private const string StencilGeometryShaderName = "StencilGeometry";
        private const string MaterialDependencyOnlyShaderName =
            "Universal Render Pipeline/Lit";
        private const string GrassTurfLayerShaderName =
            "Supernova/Terrain/Grass Turf Layer";
        private static readonly string[] PortalClippedLocalKeywords =
        {
            "_NORMALMAP",
            "_ALPHATEST_ON",
            "_EMISSION",
            "_METALLICSPECGLOSSMAP",
            "_SPECULAR_SETUP",
            "_OCCLUSIONMAP",
        };
        private static readonly string[] CriticalRuntimeShaderNames =
        {
            SoftFalloffLitShaderName,
            CrystalOreLitShaderName,
            CrystalOreLitCompatibleShaderName,
            CrystalOreSparkleOverlayShaderName,
            LilToonShaderName,
            StencilGeometryShaderName,
            PortalClippedLitShaderName,
            PortalSurfaceShaderName,
            "Supernova/Vegetation/Cave Grass Blade",
        };
        private static readonly string[] RuntimeVariantShaderNames =
        {
            CrystalOreLitShaderName,
            CrystalOreLitCompatibleShaderName,
            CrystalOreSparkleOverlayShaderName,
            GrassTurfLayerShaderName,
            "Supernova/Vegetation/Cave Grass Blade",
        };
        private static readonly string[][] RuntimeLightingKeywordSets =
            BuildRuntimeLightingKeywordSets();
        public int callbackOrder => -10000;

        private static void EnsureEditorShaderConfiguration()
        {
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall +=
                    EnsureEditorShaderConfiguration;
                return;
            }

            try
            {
                Material[] materials = FindBuildMaterials().ToArray();
                EnsureCrystalOrePreloadedAssets(materials);
                EnsureShaderVariantsArePreloaded(materials);
                EnsureCriticalShadersAreAlwaysIncluded(materials);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Failed to persist runtime shader build state: "
                    + exception);
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            ValidateLilToonBuildConfiguration(report.summary.platformGroup);
            ValidateShader(LilToonShaderName);
            ValidateShader(StencilGeometryShaderName);
            ValidateShader(SoftFalloffLitShaderName);
            ValidateShader(CrystalOreLitShaderName);
            ValidateShader(CrystalOreLitCompatibleShaderName);
            ValidateShader(CrystalOreSparkleOverlayShaderName);
            ValidateShader(PortalClippedLitShaderName);
            ValidateShader(PortalSurfaceShaderName);

            Material[] materials = FindBuildMaterials().ToArray();
            EnsureCrystalOrePreloadedAssets(materials);
            EnsureShaderVariantsArePreloaded(materials);
            EnsureCriticalShadersAreAlwaysIncluded(materials);
            foreach (Material material in materials)
            {
                ValidateMaterial(material);
            }
        }

        private static void ValidateLilToonBuildConfiguration(
            BuildTargetGroup targetGroup)
        {
            if (targetGroup != BuildTargetGroup.Standalone)
            {
                return;
            }

            string symbols =
                PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
            bool optimizationDisabled = symbols.Split(';').Any(
                symbol => string.Equals(
                    symbol,
                    LilToonOptimizationDisableDefine,
                    StringComparison.Ordinal));
            if (!optimizationDisabled)
            {
                throw new BuildFailedException(
                    "Standalone builds must define "
                    + LilToonOptimizationDisableDefine
                    + " so Player builds use the same lilToon shader source "
                    + "as the Editor.");
            }
        }

        private static IEnumerable<Material> FindBuildMaterials()
        {
            string[] rootPaths = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .Concat(new[] { ProjectAssetPaths.Scenes.Home })
                .Concat(PlayerSettings.GetPreloadedAssets()
                    .Where(asset => asset != null)
                    .Select(asset => AssetDatabase.GetAssetPath(asset)))
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (rootPaths.Length == 0)
            {
                yield break;
            }

            var seen = new HashSet<Material>();
            foreach (string dependencyPath in
                AssetDatabase.GetDependencies(rootPaths, true))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    dependencyPath);
                if (material != null && seen.Add(material))
                {
                    yield return material;
                }
            }

            foreach (string guid in AssetDatabase.FindAssets(
                "t:Material",
                new[] { ProjectAssetPaths.Folders.AssetsRoot }))
            {
                string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    materialPath);
                if (material == null || material.shader == null)
                {
                    continue;
                }

                string shaderName = material.shader.name;
                bool isRequiredFamily = string.Equals(
                    shaderName,
                    SoftFalloffLitShaderName,
                    StringComparison.Ordinal)
                    || string.Equals(
                        shaderName,
                        CrystalOreLitShaderName,
                        StringComparison.Ordinal)
                    || shaderName.IndexOf(
                        "lilToon",
                        StringComparison.OrdinalIgnoreCase) >= 0;
                if (isRequiredFamily && seen.Add(material))
                {
                    yield return material;
                }
            }
        }

        private static void EnsureShaderVariantsArePreloaded(
            IEnumerable<Material> materials)
        {
            ShaderVariantCollection collection = AssetDatabase.LoadAssetAtPath<
                ShaderVariantCollection>(
                ProjectAssetPaths.Config.PlayerShaderVariants);
            if (collection == null)
            {
                collection = new ShaderVariantCollection();
                AssetDatabase.CreateAsset(
                    collection,
                    ProjectAssetPaths.Config.PlayerShaderVariants);
            }

            collection.Clear();
            foreach (Material material in materials)
            {
                if (material == null || material.shader == null)
                {
                    continue;
                }

                // Soft Falloff and lilToon are packed whole through Always
                // Included. Crystal Ore is also always included, but its
                // exact material variants are retained here because Tuanjie
                // can collect shaders before pre-build settings mutations.
                // URP/Lit remains dependency-driven because including it
                // whole expands millions of built-in variants.
                if (material.shader.name == MaterialDependencyOnlyShaderName
                    || material.shader.name == SoftFalloffLitShaderName
                    || material.shader.name.IndexOf(
                        "lilToon",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                AddShaderVariants(
                    collection,
                    material.shader,
                    material.shaderKeywords);
                if (RuntimeVariantShaderNames.Contains(material.shader.name))
                {
                    AddRuntimeLightingVariants(
                        collection,
                        material.shader,
                        material.shaderKeywords);
                }
            }
            AddShaderVariants(
                collection,
                Shader.Find(LilToonShaderName),
                Array.Empty<string>());
            AddShaderVariants(
                collection,
                Shader.Find(StencilGeometryShaderName),
                Array.Empty<string>());
            AddRuntimeLightingVariants(
                collection,
                Shader.Find(CrystalOreLitCompatibleShaderName),
                Array.Empty<string>());
            AddShaderVariants(
                collection,
                Shader.Find(CrystalOreSparkleOverlayShaderName),
                Array.Empty<string>());
            AddPortalClippedLitVariants(collection, materials);
            EditorUtility.SetDirty(collection);

            UnityEngine.Object settings = GraphicsSettings.GetGraphicsSettings();
            if (settings == null)
            {
                throw new BuildFailedException(
                    "Graphics settings could not be loaded for shader setup.");
            }

            var serialized = new SerializedObject(settings);
            SerializedProperty preloaded = serialized.FindProperty(
                "m_PreloadedShaders");
            if (preloaded == null || !preloaded.isArray)
            {
                throw new BuildFailedException(
                    "Preloaded Shaders could not be read from graphics "
                    + "settings.");
            }

            bool found = false;
            for (int index = 0; index < preloaded.arraySize; index++)
            {
                if (preloaded.GetArrayElementAtIndex(index)
                    .objectReferenceValue == collection)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                int index = preloaded.arraySize;
                preloaded.InsertArrayElementAtIndex(index);
                preloaded.GetArrayElementAtIndex(index).objectReferenceValue =
                    collection;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.SaveAssets();
        }

        private static void AddShaderVariants(
            ShaderVariantCollection collection,
            Shader shader,
            string[] keywords)
        {
            if (shader == null)
            {
                return;
            }

            string[][] keywordSets = keywords != null && keywords.Length > 0
                ? new[] { Array.Empty<string>(), keywords }
                : new[] { Array.Empty<string>() };
            foreach (PassType passType in Enum.GetValues(typeof(PassType)))
            {
                for (int index = 0; index < keywordSets.Length; index++)
                {
                    try
                    {
                        collection.Add(
                            new ShaderVariantCollection.ShaderVariant(
                                shader,
                                passType,
                                keywordSets[index]));
                    }
                    catch (ArgumentException)
                    {
                        // The shader does not expose this pass/keyword pairing.
                    }
                }
            }
        }

        private static void AddRuntimeLightingVariants(
            ShaderVariantCollection collection,
            Shader shader,
            string[] materialKeywords)
        {
            string[] localKeywords = materialKeywords ?? Array.Empty<string>();
            foreach (string[] lightingKeywords in RuntimeLightingKeywordSets)
            {
                string[] combinedKeywords = localKeywords
                    .Concat(lightingKeywords)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                AddShaderVariants(collection, shader, combinedKeywords);
            }
        }

        private static void AddPortalClippedLitVariants(
            ShaderVariantCollection collection,
            IEnumerable<Material> materials)
        {
            Shader shader = Shader.Find(PortalClippedLitShaderName);
            var localKeywordSets = new List<string[]> { Array.Empty<string>() };
            foreach (Material material in materials)
            {
                if (material == null)
                {
                    continue;
                }

                string[] localKeywords = material.shaderKeywords
                    .Where(keyword => PortalClippedLocalKeywords.Contains(
                        keyword))
                    .OrderBy(keyword => keyword, StringComparer.Ordinal)
                    .ToArray();
                if (!localKeywordSets.Any(existing =>
                    existing.SequenceEqual(localKeywords)))
                {
                    localKeywordSets.Add(localKeywords);
                }
            }

            foreach (string[] localKeywords in localKeywordSets)
            {
                AddRuntimeLightingVariants(
                    collection,
                    shader,
                    localKeywords);
            }
        }

        private static string[][] BuildRuntimeLightingKeywordSets()
        {
            var results = new List<string[]>
            {
                Array.Empty<string>(),
                new[]
                {
                    "_ADDITIONAL_LIGHTS",
                    "_REFLECTION_PROBE_BLENDING",
                    "_REFLECTION_PROBE_BOX_PROJECTION",
                    "_MAIN_LIGHT_SHADOWS",
                    "_ADDITIONAL_LIGHT_SHADOWS",
                    "_SHADOWS_SOFT",
                },
                new[]
                {
                    "_ADDITIONAL_LIGHTS",
                    "_REFLECTION_PROBE_BLENDING",
                    "_REFLECTION_PROBE_BOX_PROJECTION",
                    "_MAIN_LIGHT_SHADOWS_CASCADE",
                    "_ADDITIONAL_LIGHT_SHADOWS",
                    "_SHADOWS_SOFT",
                },
                new[]
                {
                    "_ADDITIONAL_LIGHTS",
                    "_REFLECTION_PROBE_BLENDING",
                    "_REFLECTION_PROBE_BOX_PROJECTION",
                    "_SCREEN_SPACE_OCCLUSION",
                    "_MAIN_LIGHT_SHADOWS",
                    "_ADDITIONAL_LIGHT_SHADOWS",
                    "_SHADOWS_SOFT",
                },
                new[]
                {
                    "_ADDITIONAL_LIGHTS",
                    "_REFLECTION_PROBE_BLENDING",
                    "_REFLECTION_PROBE_BOX_PROJECTION",
                    "_SCREEN_SPACE_OCCLUSION",
                    "_MAIN_LIGHT_SHADOWS_CASCADE",
                    "_ADDITIONAL_LIGHT_SHADOWS",
                    "_SHADOWS_SOFT",
                },
            };
            string[] commonKeywords =
            {
                "_ADDITIONAL_LIGHTS",
                "_REFLECTION_PROBE_BLENDING",
                "_REFLECTION_PROBE_BOX_PROJECTION",
                "_SCREEN_SPACE_OCCLUSION",
            };
            string[] shEvaluationKeywords =
            {
                string.Empty,
                "EVALUATE_SH_MIXED",
                "EVALUATE_SH_VERTEX",
            };
            string[] fogKeywords =
            {
                string.Empty,
                "FOG_LINEAR",
                "FOG_EXP",
                "FOG_EXP2",
            };

            for (int mainShadows = 0; mainShadows <= 1; mainShadows++)
            {
                for (int additionalShadows = 0;
                    additionalShadows <= 1;
                    additionalShadows++)
                {
                    for (int lightCookies = 0; lightCookies <= 1;
                        lightCookies++)
                    {
                        for (int instancing = 0; instancing <= 1; instancing++)
                        {
                            foreach (string shKeyword in shEvaluationKeywords)
                            {
                                foreach (string fogKeyword in fogKeywords)
                                {
                                    var keywords = new List<string>(
                                        commonKeywords);
                                    if (mainShadows != 0)
                                    {
                                        keywords.Add(
                                            "_MAIN_LIGHT_SHADOWS_CASCADE");
                                    }
                                    if (additionalShadows != 0)
                                    {
                                        keywords.Add(
                                            "_ADDITIONAL_LIGHT_SHADOWS");
                                        keywords.Add("_SHADOWS_SOFT_MEDIUM");
                                    }
                                    if (lightCookies != 0)
                                    {
                                        keywords.Add("_LIGHT_COOKIES");
                                    }
                                    if (instancing != 0)
                                    {
                                        keywords.Add("INSTANCING_ON");
                                    }
                                    if (!string.IsNullOrEmpty(shKeyword))
                                    {
                                        keywords.Add(shKeyword);
                                    }
                                    if (!string.IsNullOrEmpty(fogKeyword))
                                    {
                                        keywords.Add(fogKeyword);
                                    }
                                    results.Add(keywords.ToArray());
                                }
                            }
                        }
                    }
                }
            }

            return results.ToArray();
        }
        private static void EnsureCrystalOrePreloadedAssets(
            IEnumerable<Material> materials)
        {
            Shader crystalShader = Shader.Find(CrystalOreLitShaderName);
            if (crystalShader == null)
            {
                throw new BuildFailedException(
                    $"Critical runtime shader '{CrystalOreLitShaderName}' "
                    + "was not found.");
            }

            var preloaded = new List<UnityEngine.Object>(
                PlayerSettings.GetPreloadedAssets());
            bool changed = false;
            if (!preloaded.Contains(crystalShader))
            {
                preloaded.Add(crystalShader);
                changed = true;
            }

            foreach (Material material in materials)
            {
                if (material == null
                    || material.shader == null
                    || material.shader.name != CrystalOreLitShaderName
                    || preloaded.Contains(material))
                {
                    continue;
                }
                preloaded.Add(material);
                changed = true;
            }

            if (changed)
            {
                PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
                AssetDatabase.SaveAssets();
            }
        }

        private static void EnsureCriticalShadersAreAlwaysIncluded(
            IEnumerable<Material> materials)
        {
            var requiredShaders = new HashSet<Shader>();
            foreach (string shaderName in CriticalRuntimeShaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    throw new BuildFailedException(
                        $"Critical runtime shader '{shaderName}' was not found.");
                }
                requiredShaders.Add(shader);
            }

            foreach (Material material in materials)
            {
                if (material == null || material.shader == null)
                {
                    continue;
                }

                if (material.shader.name.IndexOf(
                    "lilToon",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    requiredShaders.Add(material.shader);
                }
            }

            foreach (string dependencyPath in AssetDatabase.GetDependencies(
                ProjectAssetPaths.Scenes.Home,
                true))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    dependencyPath);
                if (material == null || material.shader == null
                    || material.shader.name == MaterialDependencyOnlyShaderName)
                {
                    continue;
                }

                string shaderPath = AssetDatabase.GetAssetPath(
                    material.shader);
                if (!shaderPath.StartsWith(
                    ProjectAssetPaths.Folders.AssetsRoot + "/",
                    StringComparison.Ordinal))
                {
                    continue;
                }

                requiredShaders.Add(material.shader);
            }

            UnityEngine.Object settings = GraphicsSettings.GetGraphicsSettings();
            if (settings == null)
            {
                throw new BuildFailedException(
                    "Graphics settings could not be loaded for shader setup.");
            }

            var serialized = new SerializedObject(settings);
            SerializedProperty included = serialized.FindProperty(
                "m_AlwaysIncludedShaders");
            if (included == null || !included.isArray)
            {
                throw new BuildFailedException(
                    "Always Included Shaders could not be read from graphics "
                    + "settings.");
            }

            var variantCollectionOnlyShaders = new HashSet<Shader>
            {
                Shader.Find(MaterialDependencyOnlyShaderName),
                Shader.Find(GrassTurfLayerShaderName),
            };
            variantCollectionOnlyShaders.Remove(null);

            bool changed = false;
            for (int index = included.arraySize - 1; index >= 0; index--)
            {
                SerializedProperty element = included.GetArrayElementAtIndex(
                    index);
                Shader shader = element.objectReferenceValue as Shader;
                if (shader != null
                    && !variantCollectionOnlyShaders.Contains(shader))
                {
                    continue;
                }

                element.objectReferenceValue = null;
                included.DeleteArrayElementAtIndex(index);
                changed = true;
            }

            var existing = new HashSet<Shader>();
            for (int index = 0; index < included.arraySize; index++)
            {
                Shader shader = included.GetArrayElementAtIndex(index)
                    .objectReferenceValue as Shader;
                if (shader != null)
                {
                    existing.Add(shader);
                }
            }

            foreach (Shader shader in requiredShaders.OrderBy(
                shader => shader.name,
                StringComparer.Ordinal))
            {
                if (!existing.Add(shader))
                {
                    continue;
                }

                int index = included.arraySize;
                included.InsertArrayElementAtIndex(index);
                included.GetArrayElementAtIndex(index).objectReferenceValue =
                    shader;
                changed = true;
            }

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
        }
        private static void ValidateMaterial(Material material)
        {
            if (material.shader == null)
            {
                throw new BuildFailedException(
                    $"Build material '{AssetDatabase.GetAssetPath(material)}' "
                    + "has no shader.");
            }

            ValidateShader(material.shader);
        }

        private static void ValidateShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new BuildFailedException(
                    $"Required Player shader '{shaderName}' was not found.");
            }

            ValidateShader(shader);
        }

        private static void ValidateShader(Shader shader)
        {
            if (!shader.isSupported || ShaderUtil.ShaderHasError(shader))
            {
                throw new BuildFailedException(
                    $"Player shader '{shader.name}' is unsupported or has "
                    + "compile errors for the active build target.");
            }
        }
    }
}
