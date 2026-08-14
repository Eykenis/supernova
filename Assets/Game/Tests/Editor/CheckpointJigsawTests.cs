using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using Supernova.WorldGeneration;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class CheckpointJigsawTests
    {
        private const int Seed = 771244;

        private static DenseJigsawWorldConfiguration LoadConfiguration()
        {
            DenseJigsawWorldConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<DenseJigsawWorldConfiguration>(
                    ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
            Assert.That(configuration, Is.Not.Null);
            return configuration;
        }

        private static JigsawStructureFeatureSettings LoadCheckpointHallSettings()
        {
            JigsawStructureFeatureDefinition definition =
                LoadCheckpointHallDefinition();
            Assert.That(
                definition.TryCreateSettings(
                    out JigsawStructureFeatureSettings settings,
                    out string error),
                Is.True,
                error);
            return settings;
        }

        private static JigsawStructureFeatureDefinition
            LoadCheckpointHallDefinition()
        {
            JigsawStructureFeatureDefinition definition =
                AssetDatabase.LoadAssetAtPath<JigsawStructureFeatureDefinition>(
                    ProjectAssetPaths.Config.SpawnCheckpointHallJigsaw);
            Assert.That(definition, Is.Not.Null);
            return definition;
        }

        private static DenseJigsawFeature LoadDenseMixedWithCheckpointHall()
        {
            DenseJigsawWorldConfiguration configuration = LoadConfiguration();
            Assert.That(
                DenseJigsawFeatureMixer.TryBuild(
                    configuration,
                    LoadCheckpointHallDefinition(),
                    out DenseJigsawFeature feature,
                    out string error),
                Is.True,
                error);
            return feature;
        }

        private static JigsawStructureFeatureSettings
            LoadFixedDenseCheckpointSettings()
        {
            DenseJigsawFeature mixedFeature =
                LoadDenseMixedWithCheckpointHall();
            Assert.That(
                DenseJigsawFeatureMixer.TryBuildFixedOriginFeature(
                    mixedFeature,
                    LoadCheckpointHallSettings(),
                    out JigsawStructureFeatureSettings fixedFeature,
                    out string error),
                Is.True,
                error);
            return fixedFeature;
        }

        [Test]
        public void CollectCheckpointRequests_PlacesDiscAtFixedHallGroundCenter()
        {
            JigsawStructureFeatureSettings settings =
                LoadFixedDenseCheckpointSettings();
            Assert.That(
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    Seed,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement),
                Is.True);
            JigsawStructureGenerator.Piece hall =
                JigsawStructureGenerator.BuildLayout(
                    settings,
                    Seed,
                    placement)[0];
            Vector3Int expected = new Vector3Int(
                (hall.Bounds.MinX + hall.Bounds.MaxX) / 2,
                hall.StartFloorY + 1,
                (hall.Bounds.MinZ + hall.Bounds.MaxZ) / 2);
            Vector3Int column = InfiniteVoxelWorld.WorldToChunk(
                expected.x,
                expected.y,
                expected.z);

            var requests = new List<CheckpointSpawnRequest>();
            JigsawStructureGenerator.CollectCheckpointRequests(
                column,
                Seed,
                new[] { settings },
                requests,
                1f);

            Assert.That(requests.Select(item => item.VoxelPosition), Is.EqualTo(
                new[] { expected }));
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].Prefab, Is.Not.Null);
            Assert.That(requests[0].IsSpawnCheckpoint, Is.True);
            Assert.That(
                AssetDatabase.GetAssetPath(requests[0].Prefab),
                Is.EqualTo(ProjectAssetPaths.Models.CheckpointDisk));
        }

        [Test]
        public void CheckpointDisk_HasSolidKinematicCollisionBody()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Models.CheckpointDisk);
            Assert.That(prefab, Is.Not.Null);

            Rigidbody body = prefab.GetComponent<Rigidbody>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.useGravity, Is.False);

            BoxCollider collider = prefab.GetComponent<BoxCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.enabled, Is.True);
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(collider.size.x, Is.GreaterThan(0f));
            Assert.That(collider.size.y, Is.GreaterThan(0f));
            Assert.That(collider.size.z, Is.GreaterThan(0f));
        }

        [Test]
        public void TryResolvePlayerSpawn_UsesAuthoredPositionInsideFixedHall()
        {
            JigsawStructureFeatureSettings settings =
                LoadFixedDenseCheckpointSettings();
            Assert.That(
                JigsawStructureGenerator.TryResolvePlayerSpawn(
                    Seed,
                    new[] { settings },
                    out PlayerSpawnRequest request),
                Is.True);

            Assert.That(
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    Seed,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement),
                Is.True);
            JigsawStructureGenerator.Piece hall =
                JigsawStructureGenerator.BuildLayout(
                    settings,
                    Seed,
                    placement)[0];

            Assert.That(request.VoxelPosition.x,
                Is.InRange(hall.Bounds.MinX, hall.Bounds.MaxX));
            Assert.That(request.VoxelPosition.y,
                Is.InRange(hall.Bounds.MinY, hall.Bounds.MaxY));
            Assert.That(request.VoxelPosition.z,
                Is.InRange(hall.Bounds.MinZ, hall.Bounds.MaxZ));
            Assert.That(request.VoxelPosition,
                Is.Not.EqualTo(new Vector3Int(0, hall.StartFloorY + 1, 0)));
        }

        [Test]
        public void NonIntersectingSelection_AlwaysKeepsFixedSpawnHall()
        {
            DenseJigsawFeature mixedFeature =
                LoadDenseMixedWithCheckpointHall();
            JigsawStructureFeatureSettings denseSettings =
                mixedFeature.Settings;
            JigsawStructureFeatureSettings hallSettings =
                LoadFixedDenseCheckpointSettings();
            JigsawPlacementSelection selection =
                JigsawPlacementSelection.CreateNonIntersecting(
                    new[] { denseSettings, hallSettings },
                    Seed,
                    -96,
                    -96,
                    95,
                    95);

            Assert.That(
                JigsawStructureGenerator.TryGetPlacement(
                    hallSettings,
                    Seed,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement),
                Is.True);
            Assert.That(selection.Allows(hallSettings, placement), Is.True);
        }

        [Test]
        public void FixedOriginFeature_UsesResolvedDenseWorldFloor()
        {
            DenseJigsawFeature mixedFeature =
                LoadDenseMixedWithCheckpointHall();
            JigsawStructureFeatureSettings fixedFeature =
                LoadFixedDenseCheckpointSettings();

            Assert.That(
                fixedFeature.MinFloorHeight,
                Is.EqualTo(mixedFeature.Settings.MinFloorHeight));
            Assert.That(
                fixedFeature.MaxFloorHeight,
                Is.EqualTo(mixedFeature.Settings.MaxFloorHeight));
            Assert.That(
                fixedFeature.WorldHeight,
                Is.EqualTo(LoadConfiguration().WorldHeight));
        }

        [Test]
        public void FixedOriginFeature_StartsWithOpenCheckpointHall()
        {
            JigsawStructureFeatureSettings settings =
                LoadFixedDenseCheckpointSettings();
            JigsawPieceSettings start = settings.GetPiece(
                settings.StartPieceIndex);

            Assert.That(start.IsStartPiece, Is.True);
            Assert.That(
                start.ConnectorPattern,
                Is.EqualTo(JigsawPieceDefinition.ConnectorPattern.FourWay));
            Assert.That(
                start.SpawnMarkers.Any(marker => marker.Kind
                    == StructureSpawnMarkerDefinition.Kind.Checkpoint),
                Is.True);

            Assert.That(
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    Seed,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement),
                Is.True);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(
                    settings,
                    Seed,
                    placement);
            Assert.That(layout[0].Openings.Count, Is.EqualTo(4));
            for (int i = 0; i < layout[0].Openings.Count; i++)
            {
                Assert.That(layout[0].Openings[i].Width, Is.EqualTo(5));
                Assert.That(layout[0].Openings[i].Height, Is.EqualTo(5));
            }
            Assert.That(layout.Count, Is.GreaterThan(1));
        }

        [Test]
        public void DenseRandomPool_GeneratesAdditionalCheckpointHall()
        {
            DenseJigsawFeature feature = LoadDenseMixedWithCheckpointHall();
            Assert.That(
                feature.TryGetFamilyStartPieceIndex(
                    LoadCheckpointHallSettings().StableId,
                    out int hallModuleIndex),
                Is.True);
            JigsawPieceSettings hall = feature.Settings.GetPiece(hallModuleIndex);
            Assert.That(hall.IsStartPiece, Is.False);
            Assert.That(hall.Weight, Is.GreaterThan(0));
            Assert.That(hall.IsEligible(hall.PoolId, 1), Is.True);

            for (int seed = 0; seed < 128; seed++)
            {
                if (!JigsawStructureGenerator.TryGetPlacement(
                    feature.Settings,
                    seed,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement))
                {
                    continue;
                }

                IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                    JigsawStructureGenerator.BuildLayout(
                        feature.Settings,
                        seed,
                        placement);
                JigsawStructureGenerator.Piece generatedHall =
                    layout.FirstOrDefault(piece =>
                        piece.ModuleIndex == hallModuleIndex);
                if (generatedHall.ModuleId == null)
                {
                    continue;
                }

                Vector3Int column = InfiniteVoxelWorld.WorldToChunk(
                    generatedHall.Origin.x,
                    generatedHall.Origin.y,
                    generatedHall.Origin.z);
                var requests = new List<CheckpointSpawnRequest>();
                JigsawStructureGenerator.CollectCheckpointRequests(
                    column,
                    seed,
                    new[] { feature.Settings },
                    requests,
                    1f);
                Assert.That(requests, Is.Not.Empty);
                Assert.That(
                    requests,
                    Has.All.Matches<CheckpointSpawnRequest>(
                        request => !request.IsSpawnCheckpoint));
                return;
            }

            Assert.Fail("No random Dense layout generated the checkpoint hall.");
        }

        [Test]
        public void CollectCheckpointRequests_ZeroChanceDisablesFixedHall()
        {
            JigsawStructureFeatureSettings settings =
                LoadCheckpointHallSettings();

            var requests = new List<CheckpointSpawnRequest>();
            JigsawStructureGenerator.CollectCheckpointRequests(
                Vector3Int.zero,
                Seed,
                new[] { settings },
                requests,
                0f);
            Assert.That(requests, Is.Empty);
        }

        [Test]
        public void CollectCheckpointRequests_OnlyOwningColumnReportsFixedHallCenter()
        {
            JigsawStructureFeatureSettings settings =
                LoadFixedDenseCheckpointSettings();

            var distant = new List<CheckpointSpawnRequest>();
            JigsawStructureGenerator.CollectCheckpointRequests(
                new Vector3Int(40, 0, 40),
                Seed,
                new[] { settings },
                distant,
                1f);
            Assert.That(distant, Is.Empty);
        }
    }
}
