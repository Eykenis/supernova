using System.Collections.Generic;
using NUnit.Framework;
using Supernova.MinecraftCaves.Creatures.Navigation;
using UnityEngine;

namespace Supernova.Tests
{
    /// <summary>
    /// Drives the navigation graph and A* search directly. The core is plain C#,
    /// so these run without a scene, a terrain component or generated chunks.
    /// </summary>
    public sealed class VoxelPathfindingTests
    {
        private const int GroundY = 8;

        private FakeSolidityQuery world;
        private VoxelPathNodeMaker nodeMaker;
        private VoxelPathfinder pathfinder;
        private CreatureNavigationProfile profile;
        private CreatureBodyBox body;

        [SetUp]
        public void SetUp()
        {
            world = new FakeSolidityQuery();
            nodeMaker = new VoxelPathNodeMaker(world);
            pathfinder = new VoxelPathfinder(nodeMaker);
            profile = new CreatureNavigationProfile();
            // One voxel wide and two tall keeps the geometry in these fixtures
            // readable; body sizing itself is covered separately.
            body = new CreatureBodyBox(1, 2);
        }

        [Test]
        public void BodyBox_NarrowCreatureOccupiesASingleColumn()
        {
            // The skeleton is 0.68 m wide at a 0.42 voxel size (1.62 cells).
            // Minecraft's floor(size + 1) would demand a three-cell corridor; here
            // that rejects nearly every cave node, so a slim body plans through one
            // column and lets physics handle the walls.
            CreatureBodyBox skeleton = CreatureBodyBox.FromMetricSize(
                0.68f,
                1.8f,
                0.42f);

            Assert.That(skeleton.WidthInVoxels, Is.EqualTo(1));
            Assert.That(skeleton.HorizontalRadius, Is.Zero);
            Assert.That(
                skeleton.HeightInVoxels,
                Is.EqualTo(4),
                "Height is still checked so a creature is never routed under a "
                    + "ceiling it cannot fit below.");
        }

        [Test]
        public void BodyBox_ModerateWidthStillPlansSingleColumnSoItCanClimbSlopes()
        {
            // The cactus is 0.90 m wide (2.14 cells). It must stay single-column
            // like the skeleton: a three-cell box cannot stand on slope-adjacent
            // nodes because the uphill terrain intrudes on its footprint, which
            // leaves the creature unable to climb where a slim body walks up.
            CreatureBodyBox cactus = CreatureBodyBox.FromMetricSize(
                0.90f,
                1.29f,
                0.42f);

            Assert.That(
                cactus.WidthInVoxels,
                Is.EqualTo(1),
                "A body just over two cells wide must not widen, or it cannot "
                    + "approach slopes.");
        }

        [Test]
        public void BodyBox_BroadCreatureStillChecksItsNeighbours()
        {
            CreatureBodyBox giant = CreatureBodyBox.FromMetricSize(
                1.8f,
                3.6f,
                0.42f);

            Assert.That(
                giant.WidthInVoxels,
                Is.GreaterThan(1),
                "A body several cells wide must still verify side clearance.");
            Assert.That(giant.HeightInVoxels, Is.EqualTo(9));
        }

        [Test]
        public void BodyBox_ForcesOddWidthSoTheBoxStaysCentred()
        {
            Assert.That(new CreatureBodyBox(2, 3).WidthInVoxels, Is.EqualTo(3));
            Assert.That(new CreatureBodyBox(4, 3).WidthInVoxels, Is.EqualTo(5));
        }

        [Test]
        public void FlatPlane_ProducesAPathThatReachesTheTarget()
        {
            world.FillGround(-12, 12, -12, 12, GroundY);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(6, GroundY + 1, 0);

            VoxelPath path = pathfinder.Search(start, target, body, profile);

            Assert.That(path, Is.Not.Null);
            Assert.That(path.ReachesTarget, Is.True);
            Assert.That(path.FinalNode, Is.EqualTo(target));
            Assert.That(path.Nodes[0], Is.EqualTo(start));
            AssertEveryStepIsAdjacent(path);
        }

        [Test]
        public void DiagonalTravel_CostsMoreThanAStraightStep()
        {
            Assert.That(
                VoxelPathNodeMaker.StepCost(Vector3Int.zero, new Vector3Int(1, 0, 0)),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                VoxelPathNodeMaker.StepCost(Vector3Int.zero, new Vector3Int(1, 0, 1)),
                Is.EqualTo(Mathf.Sqrt(2f)).Within(0.0001f));
        }

        [Test]
        public void SingleStep_IsClimbedWithinTheJumpHeight()
        {
            world.FillGround(-12, 12, -12, 12, GroundY);
            // Raise the far half by one layer to form a single step.
            world.FillGround(3, 12, -12, 12, GroundY + 1);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(6, GroundY + 2, 0);

            VoxelPath path = pathfinder.Search(start, target, body, profile);

            Assert.That(path, Is.Not.Null);
            Assert.That(path.ReachesTarget, Is.True);
            AssertEveryStepIsAdjacent(path);
        }

        [Test]
        public void TwoBlockWall_ForcesADetourAroundIt()
        {
            world.FillGround(-12, 12, -12, 12, GroundY);
            // A wall two voxels tall spanning z in [-2, 2] at x == 3, leaving gaps
            // beyond it so a detour exists.
            for (int z = -2; z <= 2; z++)
            {
                world.SetSolid(3, GroundY + 1, z);
                world.SetSolid(3, GroundY + 2, z);
            }

            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(6, GroundY + 1, 0);

            VoxelPath path = pathfinder.Search(start, target, body, profile);

            Assert.That(path, Is.Not.Null);
            Assert.That(path.ReachesTarget, Is.True);
            AssertEveryStepIsAdjacent(path);
            Assert.That(
                path.Nodes,
                Has.None.Matches<Vector3Int>(node =>
                    node.x == 3 && node.z >= -2 && node.z <= 2),
                "The route must not pass through the wall.");
            Assert.That(
                path.Nodes,
                Has.Some.Matches<Vector3Int>(node => node.z < -2 || node.z > 2),
                "The route must leave the straight line to get around the wall.");
        }

        [Test]
        public void Cliff_WithinTheFallLimit_IsConnected()
        {
            world.FillGround(-12, 2, -12, 12, GroundY);
            // Ground on the far side sits two layers lower, inside the default
            // safe fall distance of three.
            world.FillGround(3, 12, -12, 12, GroundY - 2);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(6, GroundY - 1, 0);

            VoxelPath path = pathfinder.Search(start, target, body, profile);

            Assert.That(path, Is.Not.Null);
            Assert.That(path.ReachesTarget, Is.True);
            AssertEveryStepIsAdjacent(path);
        }

        [Test]
        public void Cliff_BeyondTheFallLimit_IsNotConnected()
        {
            world.FillGround(-12, 2, -12, 12, GroundY);
            // Drop of five layers exceeds the safe fall distance, so the far ledge
            // must stay unreachable and the search settles for a partial path.
            world.FillGround(3, 12, -12, 12, GroundY - 5);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(6, GroundY - 4, 0);

            VoxelPath path = pathfinder.Search(start, target, body, profile);

            Assert.That(path, Is.Not.Null);
            Assert.That(path.ReachesTarget, Is.False);
            Assert.That(
                path.Nodes,
                Has.None.Matches<Vector3Int>(node => node.y < GroundY),
                "The creature must not descend past the safe fall limit.");
        }

        [Test]
        public void DiagonalThroughABlockedCorner_IsRejected()
        {
            world.FillGround(-4, 4, -4, 4, GroundY);
            // Seal both orthogonal neighbours of the diagonal. The diagonal cell
            // itself stays open, so only the corner rule can reject it.
            world.SetSolid(1, GroundY + 1, 0);
            world.SetSolid(1, GroundY + 2, 0);
            world.SetSolid(0, GroundY + 1, 1);
            world.SetSolid(0, GroundY + 2, 1);

            var origin = new Vector3Int(0, GroundY + 1, 0);
            var diagonal = new Vector3Int(1, GroundY + 1, 1);
            nodeMaker.BeginSearch(body, profile);

            // The destination is standable in isolation.
            Assert.That(
                nodeMaker.TryClassify(diagonal, out PathNodeType diagonalType),
                Is.True);
            Assert.That(diagonalType, Is.EqualTo(PathNodeType.Walkable));

            var successors = new Vector3Int[VoxelPathNodeMaker.MaximumSuccessors];
            var costs = new float[VoxelPathNodeMaker.MaximumSuccessors];
            int successorCount = nodeMaker.GetSuccessors(origin, successors, costs);

            // Yet it must not be offered as a successor, because reaching it would
            // slice through the sealed corner.
            for (int i = 0; i < successorCount; i++)
            {
                Assert.That(
                    successors[i],
                    Is.Not.EqualTo(diagonal),
                    "A diagonal past two blocked orthogonals must be rejected.");
            }
        }

        [Test]
        public void DiagonalPastASingleBlockedOrthogonal_IsAlsoRejected()
        {
            world.FillGround(-4, 4, -4, 4, GroundY);
            // Only one orthogonal is sealed. Both sides must be passable before a
            // diagonal successor is offered, so this is still rejected.
            world.SetSolid(1, GroundY + 1, 0);
            world.SetSolid(1, GroundY + 2, 0);

            var origin = new Vector3Int(0, GroundY + 1, 0);
            var diagonal = new Vector3Int(1, GroundY + 1, 1);
            nodeMaker.BeginSearch(body, profile);

            var successors = new Vector3Int[VoxelPathNodeMaker.MaximumSuccessors];
            var costs = new float[VoxelPathNodeMaker.MaximumSuccessors];
            int successorCount = nodeMaker.GetSuccessors(origin, successors, costs);

            for (int i = 0; i < successorCount; i++)
            {
                Assert.That(
                    successors[i],
                    Is.Not.EqualTo(diagonal),
                    "A diagonal needs both orthogonals passable.");
            }
        }

        [Test]
        public void DiagonalInTheOpen_IsOffered()
        {
            world.FillGround(-4, 4, -4, 4, GroundY);

            var origin = new Vector3Int(0, GroundY + 1, 0);
            var diagonal = new Vector3Int(1, GroundY + 1, 1);
            nodeMaker.BeginSearch(body, profile);

            var successors = new Vector3Int[VoxelPathNodeMaker.MaximumSuccessors];
            var costs = new float[VoxelPathNodeMaker.MaximumSuccessors];
            int successorCount = nodeMaker.GetSuccessors(origin, successors, costs);

            Assert.That(
                successorCount,
                Is.EqualTo(8),
                "Open flat ground must offer all eight neighbours.");
            bool found = false;
            for (int i = 0; i < successorCount; i++)
            {
                found |= successors[i] == diagonal;
            }

            Assert.That(found, Is.True);
        }

        [Test]
        public void UngeneratedVoxels_AreImpassableRatherThanAir()
        {
            world.FillGround(-4, 4, -4, 4, GroundY);
            // Nothing is generated beyond x == 2, so stepping there is unknown.
            world.LimitGeneratedRange(-4, 2, -4, 4);

            nodeMaker.BeginSearch(body, profile);

            Assert.That(
                nodeMaker.TryClassify(new Vector3Int(4, GroundY + 1, 0), out _),
                Is.False,
                "An ungenerated position must not classify as walkable air.");
        }

        [Test]
        public void WideBody_IsRejectedByANarrowCorridor()
        {
            world.FillGround(-8, 8, -8, 8, GroundY);
            // Walls at z == -1 and z == 1 leave a corridor exactly one voxel wide.
            for (int x = -8; x <= 8; x++)
            {
                for (int y = 1; y <= 4; y++)
                {
                    world.SetSolid(x, GroundY + y, -1);
                    world.SetSolid(x, GroundY + y, 1);
                }
            }

            var corridorNode = new Vector3Int(0, GroundY + 1, 0);
            var slimBody = new CreatureBodyBox(1, 2);
            var wideBody = new CreatureBodyBox(3, 2);

            nodeMaker.BeginSearch(slimBody, profile);
            bool slimFits = nodeMaker.TryClassify(corridorNode, out PathNodeType slimType);

            nodeMaker.BeginSearch(wideBody, profile);
            bool wideFits = nodeMaker.TryClassify(corridorNode, out _);

            Assert.That(slimFits, Is.True);
            Assert.That(slimType, Is.EqualTo(PathNodeType.Walkable));
            Assert.That(
                wideFits,
                Is.False,
                "A three voxel wide body cannot occupy a one voxel corridor.");
        }

        [Test]
        public void Slope_ReadsAsAdjacentWithoutAnAbsoluteHeightField()
        {
            // A staircase rising one layer per column: the graph must connect it as
            // ordinary adjacency rather than requiring a shared ground height.
            for (int x = -2; x <= 8; x++)
            {
                int columnTop = GroundY + Mathf.Clamp(x, 0, 8);
                for (int z = -3; z <= 3; z++)
                {
                    for (int y = 0; y <= columnTop; y++)
                    {
                        world.SetSolid(x, y, z);
                    }
                }
            }

            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(5, GroundY + 6, 0);

            VoxelPath path = pathfinder.Search(start, target, body, profile);

            Assert.That(path, Is.Not.Null);
            Assert.That(path.ReachesTarget, Is.True);
            AssertEveryStepIsAdjacent(path);
        }

        [Test]
        public void VisitLimit_ReturnsAPartialPathTowardTheNearestNode()
        {
            world.FillGround(-40, 40, -40, 40, GroundY);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(35, GroundY + 1, 0);
            SetVisitLimit(profile, 24);

            VoxelPath path = pathfinder.Search(start, target, body, profile);

            Assert.That(path, Is.Not.Null);
            Assert.That(path.ReachesTarget, Is.False);
            Assert.That(pathfinder.LastVisitedNodeCount, Is.LessThanOrEqualTo(24));
            float startDistance = VoxelPathfinder.Heuristic(start, target);
            float endDistance = VoxelPathfinder.Heuristic(path.FinalNode, target);
            Assert.That(
                endDistance,
                Is.LessThan(startDistance),
                "The fallback route must make progress toward the target.");
        }

        [Test]
        public void UnreachableTarget_WalksToTheClosestReachableNode()
        {
            world.FillGround(-16, 16, -16, 16, GroundY);
            // Seal the target inside a chamber four voxels tall, far above the
            // jump limit, so no route exists at all.
            for (int x = 8; x <= 12; x++)
            {
                for (int z = -2; z <= 2; z++)
                {
                    for (int y = 1; y <= 4; y++)
                    {
                        bool isWall = x == 8 || x == 12 || z == -2 || z == 2;
                        if (isWall)
                        {
                            world.SetSolid(x, GroundY + y, z);
                        }
                    }

                    world.SetSolid(x, GroundY + 5, z);
                }
            }

            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(10, GroundY + 1, 0);

            VoxelPath path = pathfinder.Search(start, target, body, profile);

            Assert.That(path, Is.Not.Null);
            Assert.That(
                path.ReachesTarget,
                Is.False,
                "A sealed chamber must not yield a complete path.");
            float startDistance = VoxelPathfinder.Heuristic(start, target);
            float endDistance = VoxelPathfinder.Heuristic(path.FinalNode, target);
            Assert.That(
                endDistance,
                Is.LessThan(startDistance),
                "The creature must still close on the target it cannot reach.");
            Assert.That(
                path.Nodes,
                Has.None.Matches<Vector3Int>(node =>
                    node.x > 8 && node.x < 12 && node.z > -2 && node.z < 2),
                "The route must not enter the sealed chamber.");
        }

        [Test]
        public void FlatPath_NeverReportsAClimb()
        {
            world.FillGround(-12, 12, -12, 12, GroundY);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(6, GroundY + 1, 0);
            CreatureNavigator navigator = CreateNavigator();
            Assert.That(navigator.MoveTo(start, target, body, 0f), Is.Not.Null);

            // Sample foot heights that straddle the rounding boundary, as the
            // interpolated terrain surface does in play: it sits inside the
            // supporting voxel rather than on its top face.
            float[] footOffsets = { -0.49f, -0.2f, 0f, 0.2f, 0.49f };
            for (int i = 0; i < footOffsets.Length; i++)
            {
                var foot = new Vector3(
                    start.x,
                    start.y + footOffsets[i],
                    start.z);

                Assert.That(
                    navigator.TryGetSteering(foot, 0.9f, out _, out int rise),
                    Is.True);
                Assert.That(
                    rise,
                    Is.Zero,
                    "Flat ground must not request a jump at foot offset "
                        + footOffsets[i] + ".");
            }
        }

        [Test]
        public void ClimbEdge_ReportsTheGraphLayerDifference()
        {
            world.FillGround(-12, 12, -12, 12, GroundY);
            world.FillGround(1, 12, -12, 12, GroundY + 1);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(4, GroundY + 2, 0);
            CreatureNavigator navigator = CreateNavigator();
            VoxelPath path = navigator.MoveTo(start, target, body, 0f);

            Assert.That(path, Is.Not.Null);
            Assert.That(path.ReachesTarget, Is.True);

            var foot = new Vector3(start.x, start.y, start.z);
            Assert.That(
                navigator.TryGetSteering(
                    foot,
                    0.9f,
                    out Vector3Int next,
                    out int rise),
                Is.True);
            Assert.That(
                rise,
                Is.EqualTo(next.y - start.y),
                "The rise must equal the traversed edge's layer difference.");
            Assert.That(rise, Is.GreaterThan(0));
        }

        [Test]
        public void Steering_AdvancesOnHorizontalProximityAlone()
        {
            world.FillGround(-12, 2, -12, 12, GroundY);
            world.FillGround(3, 12, -12, 12, GroundY - 2);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(6, GroundY - 1, 0);
            CreatureNavigator navigator = CreateNavigator();
            VoxelPath path = navigator.MoveTo(start, target, body, 0f);
            Assert.That(path, Is.Not.Null);

            Vector3Int firstNode = path.CurrentNode;
            // Directly above the node but two layers high, as a creature is while
            // falling over a ledge.
            var airborneFoot = new Vector3(
                firstNode.x,
                firstNode.y + 2f,
                firstNode.z);

            navigator.TryGetSteering(airborneFoot, 0.9f, out Vector3Int next, out _);

            Assert.That(
                next,
                Is.Not.EqualTo(firstNode),
                "A node directly below must count as reached so the route advances.");
        }

        [Test]
        public void ExhaustedPath_IsReplannedWithoutWaitingOutTheCooldown()
        {
            world.FillGround(-16, 16, -16, 16, GroundY);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(4, GroundY + 1, 0);
            CreatureNavigator navigator = CreateNavigator();
            VoxelPath path = navigator.MoveTo(start, target, body, 0f);
            Assert.That(path, Is.Not.Null);
            Assert.That(navigator.HasActivePath, Is.True);

            // Walking the route to its end must not leave the creature parked until
            // an unrelated cooldown expires.
            path.Invalidate();
            Assert.That(navigator.HasActivePath, Is.False);

            Assert.That(
                navigator.ShouldReplan(target, 0.01f),
                Is.True,
                "An exhausted route must be rebuilt immediately.");
        }

        [Test]
        public void FailedPlan_BacksOffInsteadOfSearchingEveryFrame()
        {
            world.FillGround(-4, 4, -4, 4, GroundY);
            var start = new Vector3Int(0, GroundY + 1, 0);
            CreatureNavigator navigator = CreateNavigator();

            // Target buried in rock, so no route exists and no path is adopted.
            var unreachable = new Vector3Int(0, 1, 0);
            Assert.That(
                navigator.MoveTo(start, unreachable, body, 0f),
                Is.Null);

            Assert.That(
                navigator.ShouldReplan(unreachable, 0.01f),
                Is.False,
                "A failed search must back off rather than retry every frame.");
            Assert.That(
                navigator.ShouldReplan(unreachable, 10f),
                Is.True,
                "The backoff must expire so the creature tries again later.");
        }

        [Test]
        public void WanderTarget_IsFarEnoughToBeWorthWalkingTo()
        {
            world.FillGround(-20, 20, -20, 20, GroundY);
            var origin = new Vector3Int(0, GroundY + 1, 0);
            CreatureNavigator navigator = CreateNavigator();

            for (int i = 0; i < 40; i++)
            {
                Assert.That(
                    navigator.TrySampleWanderTarget(
                        origin,
                        body,
                        i,
                        out Vector3Int target),
                    Is.True);

                int offsetX = target.x - origin.x;
                int offsetZ = target.z - origin.z;
                float horizontal = Mathf.Sqrt(
                    offsetX * offsetX + offsetZ * offsetZ);
                // A neighbouring cell is already inside the arrival tolerance, so it
                // would complete instantly and leave the creature stuttering.
                Assert.That(
                    horizontal,
                    Is.GreaterThan(1.5f),
                    "Wander target must be more than one cell away.");
            }
        }

        private CreatureNavigator CreateNavigator()
        {
            return new CreatureNavigator(
                nodeMaker,
                pathfinder,
                profile,
                new System.Random(1));
        }

        [Test]
        public void UnusableStart_ReportsNoPath()
        {
            world.FillGround(-4, 4, -4, 4, GroundY);
            // Bury the start position inside solid rock.
            world.SetSolid(0, GroundY + 1, 0);
            world.SetSolid(0, GroundY + 2, 0);

            VoxelPath path = pathfinder.Search(
                new Vector3Int(0, GroundY + 1, 0),
                new Vector3Int(3, GroundY + 1, 0),
                body,
                profile);

            Assert.That(path, Is.Null);
        }

        [Test]
        public void ReusedWorkspace_ProducesTheSamePathTwice()
        {
            world.FillGround(-12, 12, -12, 12, GroundY);
            var start = new Vector3Int(0, GroundY + 1, 0);
            var target = new Vector3Int(6, GroundY + 1, 3);

            VoxelPath first = pathfinder.Search(start, target, body, profile);
            var firstNodes = new List<Vector3Int>(first.Nodes);
            VoxelPath second = pathfinder.Search(start, target, body, profile);

            Assert.That(second.Nodes, Is.EqualTo(firstNodes));
            Assert.That(second.ReachesTarget, Is.EqualTo(first.ReachesTarget));
        }

        private static void AssertEveryStepIsAdjacent(VoxelPath path)
        {
            for (int i = 1; i < path.NodeCount; i++)
            {
                Vector3Int step = path.Nodes[i] - path.Nodes[i - 1];
                Assert.That(
                    Mathf.Abs(step.x) <= 1 && Mathf.Abs(step.z) <= 1,
                    Is.True,
                    $"Step {i} moves more than one column: {step}.");
                Assert.That(
                    step.x != 0 || step.z != 0,
                    Is.True,
                    $"Step {i} does not move horizontally: {step}.");
            }
        }

        private static void SetVisitLimit(
            CreatureNavigationProfile target,
            int limit)
        {
            typeof(CreatureNavigationProfile)
                .GetField(
                    "visitLimit",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic)
                .SetValue(target, limit);
        }

        /// <summary>
        /// Voxel source backed by an explicit solid set plus a generated window.
        /// Anything outside the window reports unknown, exercising the rule that
        /// ungenerated terrain is impassable rather than empty.
        /// </summary>
        private sealed class FakeSolidityQuery : IVoxelSolidityQuery
        {
            private readonly HashSet<Vector3Int> solids = new HashSet<Vector3Int>();
            private bool hasLimit;
            private int minimumX;
            private int maximumX;
            private int minimumZ;
            private int maximumZ;

            public void SetSolid(int x, int y, int z)
            {
                solids.Add(new Vector3Int(x, y, z));
            }

            public void FillGround(
                int fromX,
                int toX,
                int fromZ,
                int toZ,
                int surfaceY)
            {
                for (int x = fromX; x <= toX; x++)
                {
                    for (int z = fromZ; z <= toZ; z++)
                    {
                        for (int y = 0; y <= surfaceY; y++)
                        {
                            SetSolid(x, y, z);
                        }
                    }
                }
            }

            public void LimitGeneratedRange(
                int fromX,
                int toX,
                int fromZ,
                int toZ)
            {
                hasLimit = true;
                minimumX = fromX;
                maximumX = toX;
                minimumZ = fromZ;
                maximumZ = toZ;
            }

            public bool TryGetSolid(int voxelX, int voxelY, int voxelZ, out bool isSolid)
            {
                isSolid = false;
                if (voxelY < 0 || voxelY > 255)
                {
                    return false;
                }

                if (hasLimit
                    && (voxelX < minimumX
                        || voxelX > maximumX
                        || voxelZ < minimumZ
                        || voxelZ > maximumZ))
                {
                    return false;
                }

                isSolid = solids.Contains(new Vector3Int(voxelX, voxelY, voxelZ));
                return true;
            }
        }
    }
}
