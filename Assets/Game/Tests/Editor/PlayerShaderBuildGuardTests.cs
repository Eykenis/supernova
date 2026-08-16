using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests.Editor
{
    public sealed class PlayerShaderBuildGuardTests
    {
        [Test]
        public void StandaloneBuild_DisablesLilToonBuildOptimization()
        {
            string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                BuildTargetGroup.Standalone);

            Assert.That(
                symbols.Split(';').Any(symbol => string.Equals(
                    symbol,
                    "LILTOON_DISABLE_OPTIMIZATION",
                    StringComparison.Ordinal)),
                Is.True);
        }

        [TestCase("lilToon")]
        [TestCase("StencilGeometry")]
        public void RequiredPlayerShader_IsSupportedAndHasNoCompileErrors(
            string shaderName)
        {
            Shader shader = Shader.Find(shaderName);

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                string.Join(
                    Environment.NewLine,
                    ShaderUtil.GetShaderMessages(shader)
                        .Select(message => message.message)));
        }
    }
}
