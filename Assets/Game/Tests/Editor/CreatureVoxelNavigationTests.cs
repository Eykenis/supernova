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
        public void Transition_OneVoxelStepProducesRisingDestination()
        {
            var stepQuery = new OneVoxelStepQuery();

            Assert.That(
                CreatureVoxelNavigation.IsStandable(
                    stepQuery,
                    shape,
                    new Vector3Int(1, 0, 0)),
                Is.True,
                "General surface tolerance remains available for observed positions.");
            Assert.That(
                CreatureVoxelNavigation.TryResolveTransition(
                    stepQuery,
                    shape,
                    settings,
                    Vector3Int.zero,
                    Vector3Int.right,
                    out Vector3Int destination,
                    out int movementCost),
                Is.True);

            Assert.That(destination, Is.EqualTo(new Vector3Int(1, 1, 0)));
            Assert.That(movementCost, Is.EqualTo(3));
        }

        [Test]
        public void Transition_OneVoxelStepRejectsBlockedHeadroom()
        {
            shape.SetBakedData(
                1f,
                new[]
                {
                    Vector3Int.zero,
                    Vector3Int.up,
                });
            var stepQuery = new OneVoxelStepQuery(true);

            Assert.That(
                CreatureVoxelNavigation.TryResolveTransition(
                    stepQuery,
                    shape,
                    settings,
                    Vector3Int.zero,
                    Vector3Int.right,
                    out _,
                    out _),
                Is.False);
        }

        [Test]
        public void PathSearch_ClampsExpansionLimitToConfiguredMaximum()
        {
            settings.maximumExpandedNodes = 8192;
            var path = new List<Vector3Int>();

            bool found = CreatureVoxelNavigation.TryFindPath(
                query,
                shape,
                settings,
                Vector3Int.zero,
                new Vector3Int(8192, 0, 0),
                path,
                out int expandedNodes);

            Assert.That(found, Is.False);
            Assert.That(
                expandedNodes,
                Is.EqualTo(CreatureVoxelNavigation.MaximumExpandedNodeLimit));
            Assert.That(path, Is.Empty);
        }

        [Test]
        public void PursuitSearch_BudgetExhaustionReturnsPathTowardTarget()
        {
            settings.maximumExpandedNodes = 32;
            var path = new List<Vector3Int>();
            var target = new Vector3Int(64, 0, 0);

            bool found = CreatureVoxelNavigation.TryFindPursuitPath(
                query,
                shape,
                settings,
                Vector3Int.zero,
                target,
                path,
                out int expandedNodes,
                out bool reachedTarget);

            Assert.That(found, Is.True);
            Assert.That(reachedTarget, Is.False);
            Assert.That(expandedNodes, Is.EqualTo(32));
            Assert.That(path.Count, Is.GreaterThan(1));
            Assert.That(
                path.Count,
                Is.EqualTo(
                    CreatureVoxelNavigation.MaximumPursuitPathNodeCount));
            Assert.That(path[0], Is.EqualTo(Vector3Int.zero));
            Assert.That(path[path.Count - 1].x, Is.GreaterThan(0));
            Assert.That(path[path.Count - 1].x, Is.LessThan(target.x));
            Assert.That(path[path.Count - 1].z, Is.Zero);
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

        [Test]
        public void CreatureMotors_DoNotPhysicallyBlockEachOther()
        {
            var firstObject = new GameObject("First Creature");
            var secondObject = new GameObject("Second Creature");
            var terrainObject = new GameObject("Terrain");
            firstObject.SetActive(false);
            secondObject.SetActive(false);
            CapsuleCollider firstCollider =
                firstObject.AddComponent<CapsuleCollider>();
            CapsuleCollider secondCollider =
                secondObject.AddComponent<CapsuleCollider>();
            BoxCollider terrainCollider =
                terrainObject.AddComponent<BoxCollider>();
            firstObject.AddComponent<CreaturePhysicsMotor>();
            secondObject.AddComponent<CreaturePhysicsMotor>();

            try
            {
                firstObject.SetActive(true);
                secondObject.SetActive(true);

                Assert.That(
                    Physics.GetIgnoreCollision(firstCollider, secondCollider),
                    Is.True,
                    "Active creature motors should pass through each other so "
                    + "crowds cannot deadlock on a shared voxel path.");
                Assert.That(
                    Physics.GetIgnoreCollision(firstCollider, terrainCollider),
                    Is.False,
                    "Creature collision with terrain and other non-creatures "
                    + "must remain enabled.");

                secondObject.SetActive(false);
                Assert.That(
                    Physics.GetIgnoreCollision(firstCollider, secondCollider),
                    Is.False,
                    "Disabling a creature should restore its collision pair.");

                secondObject.SetActive(true);
                Assert.That(
                    Physics.GetIgnoreCollision(firstCollider, secondCollider),
                    Is.True,
                    "Re-enabled creatures should rejoin crowd collision handling.");
            }
            finally
            {
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(secondObject);
                Object.DestroyImmediate(firstObject);
            }
        }

        private sealed class BlockedCornerQuery : ICreatureVoxelQuery
        {
            public bool TryGetSolid(
                Vector3Int voxel,
                out bool isSolid)
            {
                bool wall = voxel.x == 1
                    && voxel.z == 0
                    && (voxel.y == 1 || voxel.y == 2);
                isSolid = voxel.y <= 0 || wall;
                return true;
            }
        }

        private sealed class FlatGroundQuery : ICreatureVoxelQuery
        {
            public bool TryGetSolid(Vector3Int voxel, out bool isSolid)
            {
                isSolid = voxel.y <= 0;
                return true;
            }
        }

        private sealed class OneVoxelStepQuery : ICreatureVoxelQuery
        {
            private readonly bool blocksHeadroom;

            public OneVoxelStepQuery(bool blocksHeadroom = false)
            {
                this.blocksHeadroom = blocksHeadroom;
            }

            public bool TryGetSolid(Vector3Int voxel, out bool isSolid)
            {
                bool raisedStep = voxel == new Vector3Int(1, 1, 0);
                bool ceiling = blocksHeadroom
                    && voxel == new Vector3Int(1, 3, 0);
                isSolid = voxel.y <= 0 || raisedStep || ceiling;
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


        [Test]
        public void PathSimplification_OpenFloorUsesOneStraightSegment()
        {
            var path = new List<Vector3Int>();
            Vector3Int target = new Vector3Int(6, 0, 4);
            Assert.That(
                CreatureVoxelNavigation.TryFindPath(
                    query,
                    shape,
                    settings,
                    Vector3Int.zero,
                    target,
                    path,
                    out _),
                Is.True);

            CreatureVoxelNavigation.SimplifyPath(
                query,
                shape,
                settings,
                path);

            Assert.That(path, Is.EqualTo(new[]
            {
                Vector3Int.zero,
                target,
            }));
        }

        [Test]
        public void DirectSegment_DoesNotCutThroughBlockedCorner()
        {
            var blockedQuery = new BlockedCornerQuery();

            Assert.That(
                CreatureVoxelNavigation.CanTraverseDirectHorizontalSegment(
                    blockedQuery,
                    shape,
                    settings,
                    Vector3Int.zero,
                    new Vector3Int(1, 0, 1)),
                Is.False);
        }

        [TestCase(CreatureBehaviorState.Pursue, false, CreatureBehaviorState.Idle)]
        [TestCase(CreatureBehaviorState.Wander, false, CreatureBehaviorState.Idle)]
        [TestCase(CreatureBehaviorState.Pursue, true, CreatureBehaviorState.Pursue)]
        [TestCase(CreatureBehaviorState.Attack, false, CreatureBehaviorState.Attack)]
        [TestCase(CreatureBehaviorState.Caught, true, CreatureBehaviorState.Idle)]
        public void AnimationPresentation_UsesActualMovementForLocomotion(
            CreatureBehaviorState behaviorState,
            bool isActuallyMoving,
            CreatureBehaviorState expected)
        {
            Assert.That(
                CreatureBehaviorAnimator.ResolvePresentationState(
                    behaviorState,
                    isActuallyMoving),
                Is.EqualTo(expected));
        }

        [Test]
        public void StuckRecovery_ClearsPathAndStopsMotor()
        {
            var creatureObject = new GameObject("Creature");
            try
            {
                CreatureBehaviorAgent agent =
                    creatureObject.AddComponent<CreatureBehaviorAgent>();
                FieldInfo pathField = typeof(CreatureBehaviorAgent).GetField(
                    "path",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo pathIndexField =
                    typeof(CreatureBehaviorAgent).GetField(
                        "pathIndex",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo recoveryUntilField =
                    typeof(CreatureBehaviorAgent).GetField(
                        "recoverySteeringUntil",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo recover = typeof(CreatureBehaviorAgent).GetMethod(
                    "BeginStuckRecovery",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(pathField, Is.Not.Null);
                Assert.That(recover, Is.Not.Null);

                var activePath =
                    (List<Vector3Int>)pathField.GetValue(agent);
                activePath.Add(Vector3Int.zero);
                activePath.Add(Vector3Int.right);
                pathIndexField.SetValue(agent, 1);
                CreaturePhysicsMotor motor =
                    creatureObject.GetComponent<CreaturePhysicsMotor>();
                SetPrivateField(agent, "motor", motor);
                motor.Submit(new CreatureMovementCommand(
                    1,
                    Vector3.right,
                    Vector3.up,
                    0));

                recover.Invoke(agent, null);

                Assert.That(activePath, Is.Empty);
                Assert.That(pathIndexField.GetValue(agent), Is.Zero);
                Assert.That(motor.HasCommand, Is.False);
                Assert.That(
                    (float)recoveryUntilField.GetValue(agent),
                    Is.GreaterThan(Time.time));
            }
            finally
            {
                Object.DestroyImmediate(creatureObject);
            }
        }

        [Test]
        public void Pursuit_RepathsEverySecondWithoutImmediateRetry()
        {
            var terrainObject = new GameObject("Terrain");
            var creatureObject = new GameObject("Creature");
            try
            {
                var terrain = new TestVoxelTerrain(terrainObject.transform);
                creatureObject.transform.position = Vector3.up;
                CreatureBehaviorAgent agent =
                    creatureObject.AddComponent<CreatureBehaviorAgent>();
                agent.BindWorldContext(terrain, null);
                SetPrivateField(
                    agent,
                    "query",
                    new MinecraftCaveVoxelQuery(terrain));
                SetPrivateField(agent, "shape", shape);
                SetPrivateField(agent, "navigation", settings);
                MethodInfo updatePursue =
                    typeof(CreatureBehaviorAgent).GetMethod(
                        "UpdatePursue",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(updatePursue, Is.Not.Null);
                var target = new Vector3Int(4, 0, 0);

                updatePursue.Invoke(agent, new object[] { target });
                Assert.That(agent.CurrentPath, Is.Empty);
                Assert.That(agent.LastExpandedNodeCount, Is.GreaterThan(0));
                FieldInfo nextRefreshField =
                    typeof(CreatureBehaviorAgent).GetField(
                        "nextPursuitPathRefreshTime",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(nextRefreshField, Is.Not.Null);
                Assert.That(
                    (float)nextRefreshField.GetValue(agent) - Time.time,
                    Is.EqualTo(
                        CreatureBehaviorAgent.PursuitPathRefreshInterval)
                        .Within(0.001f));

                SetPrivateField(agent, "lastExpandedNodeCount", -1);
                updatePursue.Invoke(agent, new object[] { target });
                Assert.That(agent.LastExpandedNodeCount, Is.EqualTo(-1));

                nextRefreshField.SetValue(agent, Time.time);
                updatePursue.Invoke(agent, new object[] { target });
                Assert.That(agent.LastExpandedNodeCount, Is.GreaterThan(0));
                Assert.That(agent.CurrentPath, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(creatureObject);
                Object.DestroyImmediate(terrainObject);
            }
        }

        [Test]
        public void Pursuit_FailedRefreshKeepsCurrentPathAndTarget()
        {
            var terrainObject = new GameObject("Terrain");
            var creatureObject = new GameObject("Creature");
            try
            {
                var terrain = new TestVoxelTerrain(terrainObject.transform);
                creatureObject.transform.position = Vector3.up;
                CreatureBehaviorAgent agent =
                    creatureObject.AddComponent<CreatureBehaviorAgent>();
                agent.BindWorldContext(terrain, null);
                SetPrivateField(
                    agent,
                    "query",
                    new MinecraftCaveVoxelQuery(terrain));
                SetPrivateField(agent, "shape", shape);
                SetPrivateField(agent, "navigation", settings);
                FieldInfo pathField = typeof(CreatureBehaviorAgent).GetField(
                    "path",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(pathField, Is.Not.Null);
                var activePath =
                    (List<Vector3Int>)pathField.GetValue(agent);
                activePath.Add(Vector3Int.zero);
                activePath.Add(Vector3Int.right);
                SetPrivateField(agent, "pathIndex", 1);
                SetPrivateField(agent, "currentTarget", Vector3Int.right);
                MethodInfo updatePursue =
                    typeof(CreatureBehaviorAgent).GetMethod(
                        "UpdatePursue",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(updatePursue, Is.Not.Null);

                updatePursue.Invoke(
                    agent,
                    new object[] { new Vector3Int(4, 0, 0) });

                Assert.That(activePath, Is.EqualTo(new[]
                {
                    Vector3Int.zero,
                    Vector3Int.right,
                }));
                Assert.That(agent.CurrentPathIndex, Is.EqualTo(1));
                Assert.That(agent.CurrentTarget, Is.EqualTo(Vector3Int.right));
                Assert.That(agent.LastExpandedNodeCount, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(creatureObject);
                Object.DestroyImmediate(terrainObject);
            }
        }

        [Test]
        public void Pursuit_UsesAcquireAndRetentionDistances()
        {
            var creatureObject = new GameObject("Creature");
            try
            {
                CreatureBehaviorAgent agent =
                    creatureObject.AddComponent<CreatureBehaviorAgent>();
                SetPrivateField(agent, "pursuitDistance", 64f);
                SetPrivateField(agent, "pursuitRetentionDistance", 96f);
                SetPrivateField(agent, "simulationDistance", 160f);
                SetPrivateField(agent, "attackDistance", 2f);
                MethodInfo selectState =
                    typeof(CreatureBehaviorAgent).GetMethod(
                        "SelectState",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(selectState, Is.Not.Null);

                Assert.That(
                    selectState.Invoke(agent, new object[] { 60f }),
                    Is.EqualTo(CreatureBehaviorState.Pursue));
                Assert.That(
                    selectState.Invoke(agent, new object[] { 80f }),
                    Is.EqualTo(CreatureBehaviorState.Pursue));
                Assert.That(
                    selectState.Invoke(agent, new object[] { 100f }),
                    Is.EqualTo(CreatureBehaviorState.Wander));
                Assert.That(
                    selectState.Invoke(agent, new object[] { 80f }),
                    Is.EqualTo(CreatureBehaviorState.Wander));
                Assert.That(
                    selectState.Invoke(agent, new object[] { 1f }),
                    Is.EqualTo(CreatureBehaviorState.Attack));
            }
            finally
            {
                Object.DestroyImmediate(creatureObject);
            }
        }

        [Test]
        public void Pursuit_EntryStaggersInitialPathSearchAcrossAgents()
        {
            var firstObject = new GameObject("First Creature");
            var secondObject = new GameObject("Second Creature");
            try
            {
                CreatureBehaviorAgent first =
                    firstObject.AddComponent<CreatureBehaviorAgent>();
                CreatureBehaviorAgent second =
                    secondObject.AddComponent<CreatureBehaviorAgent>();
                SetPrivateField(first, "pursuitRefreshPhase", 0.1f);
                SetPrivateField(second, "pursuitRefreshPhase", 0.9f);
                MethodInfo enterState =
                    typeof(CreatureBehaviorAgent).GetMethod(
                        "EnterState",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo nextRefresh =
                    typeof(CreatureBehaviorAgent).GetField(
                        "nextPursuitPathRefreshTime",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(enterState, Is.Not.Null);
                Assert.That(nextRefresh, Is.Not.Null);
                float startTime = Time.time;

                enterState.Invoke(
                    first,
                    new object[] { CreatureBehaviorState.Pursue });
                enterState.Invoke(
                    second,
                    new object[] { CreatureBehaviorState.Pursue });

                float firstDelay =
                    (float)nextRefresh.GetValue(first) - startTime;
                float secondDelay =
                    (float)nextRefresh.GetValue(second) - startTime;
                Assert.That(firstDelay, Is.InRange(0f, 1f));
                Assert.That(secondDelay, Is.InRange(0f, 1f));
                Assert.That(secondDelay - firstDelay, Is.GreaterThan(0.6f));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void ClearingNavigation_ResetsWanderAnchorToCurrentPosition()
        {
            var terrainObject = new GameObject("Terrain");
            var creatureObject = new GameObject("Creature");
            try
            {
                var terrain = new TestVoxelTerrain(terrainObject.transform);
                creatureObject.transform.position = new Vector3(7f, 2f, 3f);
                CreatureBehaviorAgent agent =
                    creatureObject.AddComponent<CreatureBehaviorAgent>();
                agent.BindWorldContext(terrain, null);
                SetPrivateField(agent, "currentSupport", Vector3Int.zero);
                SetPrivateField(agent, "currentTarget", new Vector3Int(20, 0, 0));
                MethodInfo clearNavigation =
                    typeof(CreatureBehaviorAgent).GetMethod(
                        "ClearNavigation",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(clearNavigation, Is.Not.Null);

                clearNavigation.Invoke(agent, null);

                var expectedSupport = new Vector3Int(7, 1, 3);
                Assert.That(agent.ObservedSupport, Is.EqualTo(expectedSupport));
                Assert.That(agent.CurrentSupport, Is.EqualTo(expectedSupport));
                Assert.That(agent.CurrentTarget, Is.EqualTo(expectedSupport));
            }
            finally
            {
                Object.DestroyImmediate(creatureObject);
                Object.DestroyImmediate(terrainObject);
            }
        }

        [Test]
        public void WanderRetry_UsesIndependentTimingJitter()
        {
            var firstObject = new GameObject("First Creature");
            var secondObject = new GameObject("Second Creature");
            try
            {
                CreatureBehaviorAgent first =
                    firstObject.AddComponent<CreatureBehaviorAgent>();
                CreatureBehaviorAgent second =
                    secondObject.AddComponent<CreatureBehaviorAgent>();
                SetPrivateField(first, "random", new System.Random(1));
                SetPrivateField(second, "random", new System.Random(2));
                MethodInfo getInterval =
                    typeof(CreatureBehaviorAgent).GetMethod(
                        "GetRandomizedWanderRetryInterval",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(getInterval, Is.Not.Null);

                float firstInterval = (float)getInterval.Invoke(
                    first,
                    new object[] { 8f });
                float secondInterval = (float)getInterval.Invoke(
                    second,
                    new object[] { 8f });

                Assert.That(firstInterval, Is.InRange(5.2f, 10.8f));
                Assert.That(secondInterval, Is.InRange(5.2f, 10.8f));
                Assert.That(firstInterval, Is.Not.EqualTo(secondInterval));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void WanderPath_UsesShortRandomLegFromCurrentPosition()
        {
            var terrainObject = new GameObject("Terrain");
            var creatureObject = new GameObject("Creature");
            try
            {
                var terrain = new TestVoxelTerrain(
                    terrainObject.transform,
                    true);
                creatureObject.transform.position = new Vector3(16f, 1f, 16f);
                CreatureBehaviorAgent agent =
                    creatureObject.AddComponent<CreatureBehaviorAgent>();
                agent.BindWorldContext(terrain, null);
                SetPrivateField(
                    agent,
                    "query",
                    new MinecraftCaveVoxelQuery(terrain));
                SetPrivateField(agent, "shape", shape);
                SetPrivateField(agent, "navigation", settings);
                SetPrivateField(agent, "random", new System.Random(3));
                SetPrivateField(agent, "wanderLegRadius", 8f);
                SetPrivateField(agent, "nextWanderAttemptTime", 0f);
                MethodInfo updateWander =
                    typeof(CreatureBehaviorAgent).GetMethod(
                        "UpdateWander",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(updateWander, Is.Not.Null);
                Vector3Int start = agent.CurrentSupport;

                updateWander.Invoke(agent, null);

                Assert.That(agent.CurrentPath, Is.Not.Empty);
                Assert.That(agent.CurrentTarget, Is.Not.EqualTo(start));
                Assert.That(
                    Vector3.Distance(start, agent.CurrentTarget),
                    Is.LessThanOrEqualTo(8f));
            }
            finally
            {
                Object.DestroyImmediate(creatureObject);
                Object.DestroyImmediate(terrainObject);
            }
        }

        private sealed class TestVoxelTerrain : IVoxelTerrain
        {
            private readonly InfiniteVoxelWorld world;

            public TestVoxelTerrain(
                Transform terrainTransform,
                bool flatGround = false)
            {
                TerrainTransform = terrainTransform;
                world = new InfiniteVoxelWorld();
                world.EnsureChunk(Vector2Int.zero).Data.Fill(-1f);
                if (flatGround)
                {
                    for (int z = 0; z < VoxelColumnChunkData.Width; z++)
                    {
                        for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                        {
                            world.SetDensity(x, 0, z, 1f);
                        }
                    }
                }
                else
                {
                    world.SetDensity(0, 0, 0, 1f);
                    world.SetDensity(4, 0, 0, 1f);
                }
            }

            public Transform TerrainTransform { get; }
            public InfiniteVoxelWorld World => world;
            public VoxelTypeCatalog VoxelTypeCatalog => null;
            public float VoxelSize => 1f;
            public float IsoLevel => 0f;

            public Vector3Int WorldPositionToVoxel(Vector3 worldPosition)
            {
                return Vector3Int.RoundToInt(worldPosition);
            }

            public bool TryMineVoxel(
                Vector3Int coordinate,
                out VoxelMiningResult result)
            {
                result = default;
                return false;
            }

            public bool TryMineBrush(
                Vector3Int primaryCoordinate,
                Vector3 worldDirection,
                VoxelMiningBrushSettings settings,
                out VoxelMiningBrushResult result)
            {
                result = default;
                return false;
            }

            public bool TryMineExplosion(
                Vector3 worldCenter,
                VoxelExplosionSettings settings,
                out VoxelExplosionResult result)
            {
                result = default;
                return false;
            }

            public bool TrySetVoxelAndRebuild(
                int worldX,
                int worldY,
                int worldZ,
                float density,
                VoxelTypeId type)
            {
                return false;
            }
        }
    }
}
