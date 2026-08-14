using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class DepthProbabilityProfileTests
    {
        [Test]
        public void LowerVoxelY_ProducesHigherProbability()
        {
            var profile = new DepthProbabilityProfile();
            profile.Configure(0.2f, 1f, 1.5f);

            float shallow = profile.EvaluateProbability(0.6f, 224, 256);
            float middle = profile.EvaluateProbability(0.6f, 128, 256);
            float deep = profile.EvaluateProbability(0.6f, 32, 256);

            Assert.That(middle, Is.GreaterThan(shallow));
            Assert.That(deep, Is.GreaterThan(middle));
        }

        [Test]
        public void Configure_EnforcesNonDecreasingDepthMultiplier()
        {
            var profile = new DepthProbabilityProfile();
            profile.Configure(0.8f, 0.2f, 0f);

            Assert.That(profile.ShallowMultiplier, Is.EqualTo(0.8f));
            Assert.That(profile.DeepMultiplier, Is.EqualTo(0.8f));
            Assert.That(profile.CurveExponent, Is.GreaterThan(0f));
        }

        [Test]
        public void FirstLevelWorld_UsesDepthScalingForTreasure()
        {
            LevelConfiguration level =
                AssetDatabase.LoadAssetAtPath<LevelConfiguration>(
                    ProjectAssetPaths.Config.FirstLevel);
            var worldObject = new GameObject("Depth Probability World");
            TreasureDefinition treasure =
                ScriptableObject.CreateInstance<TreasureDefinition>();
            try
            {
                MinecraftCaveInfiniteWorld world =
                    worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
                Assert.That(world.ApplyLevelConfiguration(level), Is.True);
                treasure.Configure(null, 10, 1f, 0.6f, 1);

                Assert.That(
                    EvaluateSpawnProbability(
                        world,
                        "EvaluateTreasureSpawnProbability",
                        treasure,
                        32),
                    Is.GreaterThan(
                        EvaluateSpawnProbability(
                            world,
                            "EvaluateTreasureSpawnProbability",
                            treasure,
                            224)));
            }
            finally
            {
                Object.DestroyImmediate(treasure);
                Object.DestroyImmediate(worldObject);
            }
        }

        private static float EvaluateSpawnProbability(
            MinecraftCaveInfiniteWorld world,
            string methodName,
            ScriptableObject definition,
            int surfaceY)
        {
            MethodInfo method = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (float)method.Invoke(
                world,
                new object[] { definition, surfaceY });
        }
    }
}
