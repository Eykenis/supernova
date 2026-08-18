using System.Collections.Generic;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Editor;
using Supernova.WorldGeneration;
using UnityEditor;
using UnityEngine;

public sealed class WorldGenerationPassDebugTests
{
    [TestCase(
        MinecraftWorldGenerationDebugPass.NaturalTerrain,
        MinecraftWorldGenerationDebugPass.NaturalTerrain,
        true)]
    [TestCase(
        MinecraftWorldGenerationDebugPass.NaturalTerrain,
        MinecraftWorldGenerationDebugPass.OreGeneration,
        false)]
    [TestCase(
        MinecraftWorldGenerationDebugPass.OreGeneration,
        MinecraftWorldGenerationDebugPass.OreGeneration,
        true)]
    [TestCase(
        MinecraftWorldGenerationDebugPass.JigsawStructures,
        MinecraftWorldGenerationDebugPass.OreGeneration,
        true)]
    [TestCase(
        MinecraftWorldGenerationDebugPass.JigsawStructures,
        MinecraftWorldGenerationDebugPass.MarkerObjects,
        false)]
    [TestCase(
        MinecraftWorldGenerationDebugPass.MarkerObjects,
        MinecraftWorldGenerationDebugPass.MarkerObjects,
        true)]
    [TestCase(
        MinecraftWorldGenerationDebugPass.FullPipeline,
        MinecraftWorldGenerationDebugPass.MarkerObjects,
        true)]
    public void Includes_UsesCumulativePassCutoff(
        MinecraftWorldGenerationDebugPass current,
        MinecraftWorldGenerationDebugPass required,
        bool expected)
    {
        Assert.That(current.Includes(required), Is.EqualTo(expected));
    }

    [Test]
    public void World_DefaultsToFullPipeline_AndAcceptsDebugCutoff()
    {
        var gameObject = new GameObject("World Generation Debug Test");
        try
        {
            MinecraftCaveInfiniteWorld world =
                gameObject.AddComponent<MinecraftCaveInfiniteWorld>();

            Assert.That(
                world.GenerationDebugPass,
                Is.EqualTo(MinecraftWorldGenerationDebugPass.FullPipeline));
            Assert.That(
                world.SetGenerationDebugPass(
                    MinecraftWorldGenerationDebugPass.JigsawStructures,
                    false),
                Is.True);
            Assert.That(
                world.GenerationDebugPass,
                Is.EqualTo(
                    MinecraftWorldGenerationDebugPass.JigsawStructures));

            Assert.That(world.SetGenerationSeedOverride(24680, false), Is.True);
            Assert.That(world.OverridesWorldSeed, Is.True);
            Assert.That(world.WorldSeed, Is.EqualTo(24680));

            var serializedWorld = new SerializedObject(world);
            serializedWorld.FindProperty("fixedPreviewColumnsPerSide")
                .intValue = 4;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(world.FixedPreviewColumnsPerSide, Is.EqualTo(4));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }


[Test]
    public void DenseDebugPreview_UsesFourByFourOriginOffsets()
    {
        var gameObject = new GameObject("Dense Debug Preview Test");
        try
        {
            DenseJigsawWorldConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<DenseJigsawWorldConfiguration>(
                    ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
            Assert.That(configuration, Is.Not.Null);

            MinecraftCaveInfiniteWorld world =
                gameObject.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(world.ConfigureDenseRegion(configuration), Is.True);

            var serializedWorld = new SerializedObject(world);
            serializedWorld.FindProperty("fixedPreviewArea").boolValue = true;
            serializedWorld.FindProperty("fixedPreviewColumnsPerSide")
                .intValue = 4;
            serializedWorld
                .FindProperty("keepViewerTransformDuringGeneration")
                .boolValue = true;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();

            IReadOnlyList<Vector3Int> offsets =
                world.ConfiguredDenseRegionStreamingOffsets;
            Assert.That(offsets.Count, Is.EqualTo(16));
            Assert.That(world.KeepsViewerTransformDuringGeneration, Is.True);

            var coordinates = new HashSet<Vector3Int>(offsets);
            for (int z = -2; z <= 1; z++)
            {
                for (int x = -2; x <= 1; x++)
                {
                    Assert.That(
                        coordinates.Contains(new Vector3Int(x, 0, z)),
                        Is.True,
                        $"Missing origin-grid column ({x}, {z}).");
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void Controller_SelectsCachedPassWithoutChangingWorldPasses()
    {
        var root = new GameObject("Cached Pass Controller Test");
        try
        {
            var worlds = new MinecraftCaveInfiniteWorld[4];
            for (int i = 0; i < worlds.Length; i++)
            {
                var worldObject = new GameObject($"Pass {i + 1}");
                worldObject.transform.SetParent(root.transform);
                worlds[i] =
                    worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
                worlds[i].SetGenerationDebugPass(
                    (MinecraftWorldGenerationDebugPass)(i + 1),
                    false);
            }

            WorldGenerationPassDebugController controller =
                root.AddComponent<WorldGenerationPassDebugController>();
            var serializedController = new SerializedObject(controller);
            SerializedProperty serializedWorlds =
                serializedController.FindProperty("passWorlds");
            serializedWorlds.arraySize = worlds.Length;
            for (int i = 0; i < worlds.Length; i++)
            {
                serializedWorlds.GetArrayElementAtIndex(i)
                    .objectReferenceValue = worlds[i];
            }
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(
                controller.SelectPass(
                    MinecraftWorldGenerationDebugPass.JigsawStructures),
                Is.True);
            Assert.That(
                controller.CurrentPass,
                Is.EqualTo(
                    MinecraftWorldGenerationDebugPass.JigsawStructures));
            Assert.That(worlds[0].DebugPresentationVisible, Is.False);
            Assert.That(worlds[1].DebugPresentationVisible, Is.False);
            Assert.That(worlds[2].DebugPresentationVisible, Is.True);
            Assert.That(worlds[3].DebugPresentationVisible, Is.False);

            Assert.That(controller.SetSeedForAllPasses(24680), Is.True);
            for (int i = 0; i < worlds.Length; i++)
            {
                Assert.That(worlds[i].WorldSeed, Is.EqualTo(24680));
                Assert.That(
                    worlds[i].GenerationDebugPass,
                    Is.EqualTo(
                        (MinecraftWorldGenerationDebugPass)(i + 1)));
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
