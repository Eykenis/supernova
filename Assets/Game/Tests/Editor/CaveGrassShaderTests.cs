using NUnit.Framework;
using Supernova.MinecraftCaves;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Tests
{
    /// <summary>
    /// The compile assertion here is the cheapest guard in the grass system: it
    /// catches every HLSL error in the blade shader without opening the editor or
    /// entering play mode.
    /// </summary>
    public sealed class CaveGrassShaderTests
    {
        /// <summary>
        /// URP selects passes by their <c>LightMode</c> tag rather than by pass
        /// name, so these are the tag values the pipeline actually looks up.
        /// </summary>
        private static readonly string[] RequiredLightModes =
        {
            "UniversalForward",
            "ShadowCaster",
            "DepthOnly",
            "DepthNormals",
        };

        [Test]
        public void CaveGrassBladeShader_IsSupportedAndHasNoCompileErrors()
        {
            Shader shader = Shader.Find(
                CaveVegetationShaderNames.CaveGrassBlade);

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(shader), Is.Empty);
        }

        [Test]
        public void CaveGrassBladeShader_DeclaresEveryRequiredLightMode()
        {
            Shader shader = Shader.Find(
                CaveVegetationShaderNames.CaveGrassBlade);
            Assert.That(shader, Is.Not.Null);

            // DepthNormals in particular is easy to omit, and its absence only
            // shows up as grass punching holes in screen-space ambient occlusion.
            // Unity reports some built-in tag values upper-cased, so compare
            // case-insensitively.
            var lightModes = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);
            var lightModeTag = new ShaderTagId("LightMode");
            for (int subShader = 0; subShader < shader.subshaderCount; subShader++)
            {
                int passCount = shader.GetPassCountInSubshader(subShader);
                for (int pass = 0; pass < passCount; pass++)
                {
                    lightModes.Add(shader
                        .FindPassTagValue(subShader, pass, lightModeTag)
                        .name);
                }
            }

            for (int i = 0; i < RequiredLightModes.Length; i++)
            {
                Assert.That(
                    lightModes.Contains(RequiredLightModes[i]),
                    Is.True,
                    "Missing shader pass for LightMode "
                    + RequiredLightModes[i]
                    + ". Declared: " + string.Join(", ", lightModes));
            }
        }

        [Test]
        public void SharedAttenuationInclude_ExistsAtItsRegisteredPath()
        {
            // Grass and cave walls must share one attenuation curve, so the
            // extracted include has to stay where both shaders reference it.
            string path = ProjectAssetPaths.ToAbsoluteFileSystemPath(
                ProjectAssetPaths.Shaders.SoftFalloffAttenuation);
            Assert.That(System.IO.File.Exists(path), Is.True, path);
        }

        [Test]
        public void CaveGrassBladeMaterial_UsesTheBladeShaderWithInstancing()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Materials.CaveGrassBlade);

            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(
                material.shader.name,
                Is.EqualTo(CaveVegetationShaderNames.CaveGrassBlade),
                "A silent fall back to URP Lit would still render green grass "
                + "and would hide a shader compile failure.");
            Assert.That(material.enableInstancing, Is.True);
        }
    }
}
