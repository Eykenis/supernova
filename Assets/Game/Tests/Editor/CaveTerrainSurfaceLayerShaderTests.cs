using System;
using System.Collections.Generic;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Tests
{
    public sealed class CaveTerrainSurfaceLayerShaderTests
    {
        [Test]
        public void GrassTurfLayerShader_IsSupportedAndHasNoCompileErrors()
        {
            Shader shader = Shader.Find(CaveTerrainShaderNames.GrassTurfLayer);

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(shader), Is.Empty);
            Assert.That(
                shader.renderQueue,
                Is.GreaterThanOrEqualTo((int)RenderQueue.Transparent));
        }

        [Test]
        public void GrassTurfLayerShader_DeclaresForwardOnlyPass()
        {
            Shader shader = Shader.Find(CaveTerrainShaderNames.GrassTurfLayer);
            Assert.That(shader, Is.Not.Null);

            var lightModes = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var lightModeTag = new ShaderTagId("LightMode");
            for (int subShader = 0;
                subShader < shader.subshaderCount;
                subShader++)
            {
                int passCount = shader.GetPassCountInSubshader(subShader);
                for (int pass = 0; pass < passCount; pass++)
                {
                    lightModes.Add(shader
                        .FindPassTagValue(subShader, pass, lightModeTag)
                        .name);
                }
            }

            Assert.That(lightModes.Contains("UniversalForwardOnly"), Is.True);
            Assert.That(lightModes.Contains("DepthOnly"), Is.False);
        }

        [Test]
        public void GrassTurfLayerShaderFiles_ExistAtRegisteredPaths()
        {
            string shaderPath = ProjectAssetPaths.ToAbsoluteFileSystemPath(
                ProjectAssetPaths.Shaders.CaveGrassTurfLayer);
            string passPath = ProjectAssetPaths.ToAbsoluteFileSystemPath(
                ProjectAssetPaths.Shaders.CaveGrassTurfLayerForwardPass);

            Assert.That(System.IO.File.Exists(shaderPath), Is.True, shaderPath);
            Assert.That(System.IO.File.Exists(passPath), Is.True, passPath);
        }
    }
}
