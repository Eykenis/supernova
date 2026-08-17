using System.Linq;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class TreasureContentTests
    {
        [Test]
        public void NewTreasureDefinitions_UseApprovedConfiguration()
        {
            AssertDefinition(
                ProjectAssetPaths.Config.StatueTreasure,
                ProjectAssetPaths.Prefabs.StatueTreasure,
                "雕像",
                900,
                12f,
                0.35f,
                0.25f);
            AssertDefinition(
                ProjectAssetPaths.Config.SphinxTreasure,
                ProjectAssetPaths.Prefabs.SphinxTreasure,
                "人面像",
                1800,
                30f,
                0.25f,
                0.15f);
            TreasureDefinition core = AssertDefinition(
                ProjectAssetPaths.Config.MysticCoreTreasure,
                ProjectAssetPaths.Prefabs.MysticCoreTreasure,
                "神秘核心",
                2500,
                6f,
                0.7f,
                0.1f);
            PlayerToolDefinition bomb =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.BombTool);
            Assert.That(core.DestructionExplosionTool, Is.SameAs(bomb));
        }

        [Test]
        public void NewTreasures_AreRegisteredAndHaveThreeFractureVariants()
        {
            TreasureSpawnTable table =
                AssetDatabase.LoadAssetAtPath<TreasureSpawnTable>(
                    ProjectAssetPaths.Config.TreasureSpawnTable);
            Assert.That(table, Is.Not.Null);

            string[] paths =
            {
                ProjectAssetPaths.Config.StatueTreasure,
                ProjectAssetPaths.Config.SphinxTreasure,
                ProjectAssetPaths.Config.MysticCoreTreasure,
            };
            for (int i = 0; i < paths.Length; i++)
            {
                TreasureDefinition definition =
                    AssetDatabase.LoadAssetAtPath<TreasureDefinition>(
                        paths[i]);
                Assert.That(table.Treasures, Contains.Item(definition));
                Assert.That(definition.FractureVariants.Count, Is.EqualTo(3));
                Assert.That(
                    definition.FractureVariants,
                    Has.All.Not.Null);
            }

            TreasureDefinition core =
                AssetDatabase.LoadAssetAtPath<TreasureDefinition>(
                    ProjectAssetPaths.Config.MysticCoreTreasure);
            Assert.That(
                core.FractureVariants[0]
                    .GetComponentsInChildren<MeshRenderer>(true).Length,
                Is.GreaterThanOrEqualTo(10),
                "Both MysticCore meshes must contribute fragments.");
        }

        [Test]
        public void SphinxPrototype_IsOnePointFiveMetresTallAndCollidable()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.SphinxTreasure);
            Assert.That(prefab, Is.Not.Null);
            Renderer[] renderers =
                prefab.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Assert.That(bounds.size.y, Is.EqualTo(1.5f).Within(0.01f));
            Assert.That(prefab.GetComponentInChildren<Collider>(), Is.Not.Null);
        }

        [Test]
        public void TreasureSpawnTable_UsesSpawnChanceAsSelectionWeight()
        {
            TreasureSpawnTable table =
                AssetDatabase.LoadAssetAtPath<TreasureSpawnTable>(
                    ProjectAssetPaths.Config.TreasureSpawnTable);
            float totalWeight = table.Treasures
                .Where(item => item != null && item.Prefab != null)
                .Sum(item => item.SpawnChance);
            float cumulative = 0f;
            foreach (TreasureDefinition definition in table.Treasures)
            {
                if (definition == null
                    || definition.Prefab == null
                    || definition.SpawnChance <= 0f)
                {
                    continue;
                }

                float midpoint =
                    (cumulative + definition.SpawnChance * 0.5f)
                    / totalWeight;
                Assert.That(
                    table.SelectWeighted(midpoint),
                    Is.SameAs(definition));
                cumulative += definition.SpawnChance;
            }
        }

        [Test]
        public void AuthoredJigsawTreasureMarkers_UseWeightedWorldTable()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:JigsawStructureFeatureDefinition",
                new[] { ProjectAssetPaths.Folders.JigsawStructureFeatures });
            Assert.That(guids, Is.Not.Empty);
            int treasureMarkerCount = 0;
            for (int assetIndex = 0;
                assetIndex < guids.Length;
                assetIndex++)
            {
                JigsawStructureFeatureDefinition feature =
                    AssetDatabase.LoadAssetAtPath<
                        JigsawStructureFeatureDefinition>(
                        AssetDatabase.GUIDToAssetPath(guids[assetIndex]));
                foreach (JigsawPieceDefinition piece in feature.Pieces)
                {
                    foreach (StructureSpawnMarkerDefinition marker
                        in piece.SpawnMarkers)
                    {
                        if (marker.MarkerKind
                            != StructureSpawnMarkerDefinition.Kind.Treasure)
                        {
                            continue;
                        }
                        treasureMarkerCount++;
                        Assert.That(
                            marker.TreasureSelection,
                            Is.EqualTo(
                                StructureSpawnMarkerDefinition
                                    .TreasureSelectionMode
                                    .WeightedWorldTable));
                    }
                }
            }
            Assert.That(treasureMarkerCount, Is.GreaterThan(0));
        }

        private static TreasureDefinition AssertDefinition(
            string definitionPath,
            string prefabPath,
            string displayName,
            int value,
            float weight,
            float fragility,
            float spawnChance)
        {
            TreasureDefinition definition =
                AssetDatabase.LoadAssetAtPath<TreasureDefinition>(
                    definitionPath);
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(definition, Is.Not.Null, definitionPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            Assert.That(definition.Prefab, Is.SameAs(prefab));
            Assert.That(definition.DisplayName, Is.EqualTo(displayName));
            Assert.That(definition.Value, Is.EqualTo(value));
            Assert.That(definition.Weight, Is.EqualTo(weight));
            Assert.That(definition.Fragility, Is.EqualTo(fragility));
            Assert.That(definition.SpawnChance, Is.EqualTo(spawnChance));
            Assert.That(definition.AttemptsPerChunk, Is.EqualTo(1));
            return definition;
        }
    }
}
