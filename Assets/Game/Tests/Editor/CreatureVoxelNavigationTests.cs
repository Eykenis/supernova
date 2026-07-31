using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class CreatureVoxelNavigationTests
    {
        private CreatureVoxelShape shape;
        private CreatureNavigationSettings settings;
        private FlatGroundQuery query;

        [SetUp]
        public void SetUp()
        {
            shape = ScriptableObject.CreateInstance<CreatureVoxelShape>();
            shape.SetBakedData(1f, new[] { Vector3Int.zero });
            settings = new CreatureNavigationSettings
            {
                safeFallHeight = 3,
                maximumJumpHeight = 1,
                maximumSingleMoveCost = 100,
                maximumExpandedNodes =
                    CreatureVoxelNavigation.MaximumExpandedNodeLimit,
            };
            query = new FlatGroundQuery();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(shape);
        }

        [Test]
        public void Transition_DiagonalCostsTwiceCardinalMovement()
        {
            Assert.That(
                CreatureVoxelNavigation.TryResolveTransition(
                    query,
                    shape,
                    settings,
                    Vector3Int.zero,
                    Vector3Int.right,
                    out _,
                    out int cardinalCost),
                Is.True);
            Assert.That(
                CreatureVoxelNavigation.TryResolveTransition(
                    query,
                    shape,
                    settings,
                    Vector3Int.zero,
                    new Vector3Int(1, 0, 1),
                    out _,
                    out int diagonalCost),
                Is.True);

            Assert.That(cardinalCost, Is.EqualTo(1));
            Assert.That(diagonalCost, Is.EqualTo(2));
        }

        [Test]
        public void PathSearch_ClampsExpansionLimitToFiveHundredTwelve()
        {
            settings.maximumExpandedNodes = 8192;
            var path = new List<Vector3Int>();

            bool found = CreatureVoxelNavigation.TryFindPath(
                query,
                shape,
                settings,
                Vector3Int.zero,
                new Vector3Int(2048, 0, 0),
                path,
                out int expandedNodes);

            Assert.That(found, Is.False);
            Assert.That(
                expandedNodes,
                Is.EqualTo(CreatureVoxelNavigation.MaximumExpandedNodeLimit));
            Assert.That(path, Is.Empty);
        }

        [Test]
        public void PathSearch_WorkspaceCanBeReusedAfterCappedSearch()
        {
            var path = new List<Vector3Int>();
            CreatureVoxelNavigation.TryFindPath(
                query,
                shape,
                settings,
                Vector3Int.zero,
                new Vector3Int(2048, 0, 0),
                path,
                out _);

            bool found = CreatureVoxelNavigation.TryFindPath(
                query,
                shape,
                settings,
                Vector3Int.zero,
                new Vector3Int(4, 0, 0),
                path,
                out _);

            Assert.That(found, Is.True);
            Assert.That(path[0], Is.EqualTo(Vector3Int.zero));
            Assert.That(path[path.Count - 1], Is.EqualTo(new Vector3Int(4, 0, 0)));
        }

        [Test]
        public void PathSearch_UsesTraversalLinkAcrossUnsupportedGap()
        {
            Vector3Int destination = new Vector3Int(3, 2, 0);
            var linkedQuery = new LinkedGapQuery(
                Vector3Int.zero,
                destination);
            var path = new List<Vector3Int>();

            bool found = CreatureVoxelNavigation.TryFindPath(
                linkedQuery,
                shape,
                settings,
                Vector3Int.zero,
                destination,
                path,
                out _);

            Assert.That(found, Is.True);
            Assert.That(path, Is.EqualTo(new[]
            {
                Vector3Int.zero,
                destination,
            }));
        }

        [Test]
        public void PathSearch_RejectsTraversalLinkAboveConfiguredJumpHeight()
        {
            Vector3Int destination = new Vector3Int(3, 4, 0);
            var linkedQuery = new LinkedGapQuery(
                Vector3Int.zero,
                destination);
            var path = new List<Vector3Int>();
            settings.maximumTraversalJumpHeight = 3;

            bool found = CreatureVoxelNavigation.TryFindPath(
                linkedQuery,
                shape,
                settings,
                Vector3Int.zero,
                destination,
                path,
                out _);

            Assert.That(found, Is.False);
            Assert.That(path, Is.Empty);
        }

        [Test]
        public void DynamicPlatform_AddsOnlyTheMissingUphillLink()
        {
            var worldObject = new GameObject("Cave World");
            var owner = new GameObject("Platform Owner");
            MinecraftCaveInfiniteWorld caveWorld =
                worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
            var voxelWorld = new InfiniteVoxelWorld();
            voxelWorld.EnsureChunk(Vector2Int.zero).Data.Fill(-1f);
            voxelWorld.SetDensity(1, 0, 0, 1f);
            SetPrivateField(caveWorld, "world", voxelWorld);
            SetPrivateField(caveWorld, "voxelSize", 1f);
            SetPrivateField(caveWorld, "isoLevel", 0f);
            try
            {
                DynamicCreatureNavigation.RegisterPlatform(
                    caveWorld,
                    owner,
                    new Vector3(0f, 3f, 0f),
                    0.6f,
                    0f);
                var platformSupport = new Vector3Int(0, 2, 0);
                var terrainSupport = new Vector3Int(1, 0, 0);

                Assert.That(
                    DynamicCreatureNavigation.ContainsSupport(
                        caveWorld,
                        platformSupport),
                    Is.True);
                Assert.That(
                    DynamicCreatureNavigation.TryGetTraversalLink(
                        caveWorld,
                        terrainSupport,
                        platformSupport,
                        out _),
                    Is.True);
                Assert.That(
                    DynamicCreatureNavigation.TryGetTraversalLink(
                        caveWorld,
                        platformSupport,
                        terrainSupport,
                        out _),
                    Is.False);
            }
            finally
            {
                DynamicCreatureNavigation.UnregisterPlatform(
                    caveWorld,
                    owner);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(worldObject);
            }
        }

        [TestCase(32f, 1f)]
        [TestCase(32.01f, 2f)]
        [TestCase(64f, 2f)]
        [TestCase(64.01f, 5f)]
        [TestCase(96f, 5f)]
        [TestCase(96.01f, 10f)]
        [TestCase(128f, 10f)]
        [TestCase(128.01f, 0f)]
        public void NavigationDistance_UsesExpectedIntervalMultiplier(
            float distanceInVoxels,
            float expectedMultiplier)
        {
            MethodInfo method = typeof(CreatureBehaviorAgent).GetMethod(
                "GetNavigationIntervalMultiplier",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            Assert.That(
                method.Invoke(null, new object[] { distanceInVoxels }),
                Is.EqualTo(expectedMultiplier));
        }

        private sealed class FlatGroundQuery : ICreatureVoxelQuery
        {
            public bool TryGetSolid(Vector3Int voxel, out bool isSolid)
            {
                isSolid = voxel.y <= 0;
                return true;
            }
        }

        private sealed class LinkedGapQuery :
            ICreatureVoxelQuery,
            ICreatureTraversalLinkQuery
        {
            private readonly Vector3Int start;
            private readonly Vector3Int destination;
            private readonly CreatureTraversalLink link;

            public LinkedGapQuery(
                Vector3Int start,
                Vector3Int destination)
            {
                this.start = start;
                this.destination = destination;
                Vector3Int delta = destination - start;
                link = new CreatureTraversalLink(
                    destination,
                    Mathf.Abs(delta.x) + Mathf.Abs(delta.z)
                        + Mathf.Abs(delta.y),
                    new Vector2(delta.x, delta.z).magnitude,
                    delta.y);
            }

            public int NavigationRevision => 1;

            public bool TryGetSolid(Vector3Int voxel, out bool isSolid)
            {
                isSolid = voxel == start || voxel == destination;
                return true;
            }

            public void GetTraversalLinks(
                Vector3Int fromSupport,
                List<CreatureTraversalLink> results)
            {
                results.Clear();
                if (fromSupport == start)
                {
                    results.Add(link);
                }
            }

            public bool TryGetTraversalLink(
                Vector3Int fromSupport,
                Vector3Int toSupport,
                out CreatureTraversalLink result)
            {
                if (fromSupport == start && toSupport == destination)
                {
                    result = link;
                    return true;
                }

                result = default;
                return false;
            }
        }

        private static void SetPrivateField(
            object target,
            string name,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
