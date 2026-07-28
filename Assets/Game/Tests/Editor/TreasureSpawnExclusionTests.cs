using System.Reflection;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class TreasureSpawnExclusionTests
    {
        private GameObject worldObject;

        [TearDown]
        public void TearDown()
        {
            if (worldObject != null) Object.DestroyImmediate(worldObject);
        }

        [Test]
        public void SpawnExclusion_RejectsCandidatesNearCellOnly()
        {
            worldObject = new GameObject("World");
            MinecraftCaveInfiniteWorld world =
                worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
            LevelConfiguration level =
                AssetDatabase.LoadAssetAtPath<LevelConfiguration>(
                    ProjectAssetPaths.Config.FirstLevel);
            Assert.That(level, Is.Not.Null);
            Assert.That(world.ApplyLevelConfiguration(level), Is.True);
            SetField(world, "targetSpawnWorldPosition", new Vector3(10f, 4f, 10f));

            Assert.That(IsExcluded(world, new Vector3(15f, 100f, 10f)), Is.True);
            Assert.That(IsExcluded(world, new Vector3(30f, 4f, 10f)), Is.False);
        }

        private static bool IsExcluded(
            MinecraftCaveInfiniteWorld world,
            Vector3 position)
        {
            MethodInfo method = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "IsInsideTreasureSpawnExclusion",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(world, new object[] { position });
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
