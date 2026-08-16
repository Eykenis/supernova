using System.Text;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Creatures;
using Supernova.MinecraftCaves.Creatures.Navigation;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.MinecraftCaves.Editor
{
    /// <summary>
    /// Reports why a live creature is or is not navigating. Reads runtime state
    /// only; it never modifies the scene. Diagnostic aid for locomotion issues,
    /// where the cause is usually invisible from the inspector alone.
    /// </summary>
    public static class CreatureNavigationDiagnostics
    {
        [MenuItem("Tools/Minecraft Caves/Diagnose Creature Navigation")]
        public static void Diagnose()
        {
            var report = new StringBuilder();
            var world = Object.FindObjectOfType<MinecraftCaveInfiniteWorld>();
            report.AppendLine(
                "world = " + (world == null ? "NULL" : world.name));
            if (world != null)
            {
                report.AppendLine(
                    "  voxelSize=" + world.VoxelSize
                    + " isoLevel=" + world.IsoLevel
                    + " generatedChunks=" + world.GeneratedChunkCount);
            }

            CreatureBehaviorAgent[] agents =
                Object.FindObjectsOfType<CreatureBehaviorAgent>();
            report.AppendLine("live agents = " + agents.Length);

            for (int i = 0; i < agents.Length && i < 5; i++)
            {
                AppendAgent(report, agents[i], world);
            }

            Debug.Log(report.ToString());
        }

        private static void AppendAgent(
            StringBuilder report,
            CreatureBehaviorAgent agent,
            MinecraftCaveInfiniteWorld world)
        {
            report.AppendLine("--- " + agent.name);
            report.AppendLine(
                "  state=" + agent.CurrentState
                + " alive=" + agent.IsAlive
                + " visitedNodes=" + agent.LastVisitedNodeCount);

            VoxelPath path = agent.CurrentPath;
            report.AppendLine(path == null
                ? "  path = NULL (no route planned)"
                : "  path nodes=" + path.NodeCount
                    + " index=" + path.CurrentIndex
                    + " reachesTarget=" + path.ReachesTarget
                    + " finished=" + path.IsFinished);

            var motor = agent.GetComponent<CreaturePhysicsMotor>();
            if (motor != null)
            {
                report.AppendLine(
                    "  motor grounded=" + motor.IsGrounded
                    + " horizontalSpeed=" + motor.HorizontalSpeed.ToString("F3")
                    + " commandedSpeed=" + motor.CommandedSpeed.ToString("F3")
                    + " fraction=" + motor.CommandedSpeedFraction.ToString("F3"));
            }

            var body = agent.GetComponent<Rigidbody>();
            if (body != null)
            {
                report.AppendLine(
                    "  rigidbody velocity=" + body.velocity
                    + " kinematic=" + body.isKinematic
                    + " constraints=" + body.constraints);
            }

            IVoxelTerrain terrain = agent.VoxelTerrain;
            if (terrain == null || terrain.World == null)
            {
                report.AppendLine("  terrain NOT bound -> cannot plan");
                return;
            }

            AppendGraphProbe(report, agent, terrain);
        }

        private static void AppendGraphProbe(
            StringBuilder report,
            CreatureBehaviorAgent agent,
            IVoxelTerrain terrain)
        {
            var query = new VoxelTerrainSolidityQuery(terrain);
            var maker = new VoxelPathNodeMaker(query);
            CreatureNavigationProfile profile = agent.NavigationProfile
                ?? new CreatureNavigationProfile();

            CreatureBodyBox box = ResolveBox(agent, terrain);
            report.AppendLine("  bodyBox = " + box);
            maker.BeginSearch(box, profile);

            Vector3Int foot = FootNode(agent, terrain);
            report.AppendLine("  sampled foot node = " + foot);

            // Why does the start node fail, if it does?
            bool startOk = maker.TryClassify(foot, out PathNodeType startType);
            report.AppendLine(
                "  start classify = "
                + (startOk ? startType.ToString() : "REJECTED"));
            AppendColumn(report, query, foot, box);

            if (agent.PlayerFoot == null)
            {
                report.AppendLine("  playerFoot NOT bound");
                return;
            }

            Vector3Int target = FootNode2(agent.PlayerFoot.position, terrain);
            report.AppendLine("  player foot node = " + target);
            bool targetOk = maker.TryClassify(target, out PathNodeType targetType);
            report.AppendLine(
                "  target classify = "
                + (targetOk ? targetType.ToString() : "REJECTED"));

            var finder = new VoxelPathfinder(maker);
            VoxelPath probe = finder.Search(foot, target, box, profile);
            report.AppendLine(
                "  probe search = "
                + (probe == null
                    ? "NULL (start unusable)"
                    : probe.NodeCount + " nodes, reaches=" + probe.ReachesTarget
                        + ", visited=" + finder.LastVisitedNodeCount));
        }

        private static void AppendColumn(
            StringBuilder report,
            VoxelTerrainSolidityQuery query,
            Vector3Int foot,
            CreatureBodyBox box)
        {
            var column = new StringBuilder();
            for (int offset = -2; offset <= box.HeightInVoxels + 1; offset++)
            {
                bool known = query.TryGetSolid(
                    foot.x,
                    foot.y + offset,
                    foot.z,
                    out bool solid);
                column.Append(" y")
                    .Append(offset >= 0 ? "+" : "")
                    .Append(offset)
                    .Append('=')
                    .Append(!known ? "?" : solid ? "#" : ".");
            }

            report.AppendLine("  column (#solid .air ?ungenerated):" + column);
        }

        private static CreatureBodyBox ResolveBox(
            CreatureBehaviorAgent agent,
            IVoxelTerrain terrain)
        {
            float width = 0f;
            float height = 0f;
            var motor = agent.GetComponent<CreaturePhysicsMotor>();
            Collider[] colliders = agent.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                var capsule = colliders[i] as CapsuleCollider;
                if (capsule == null
                    || capsule.isTrigger
                    || (motor != null && capsule == motor.CrowdCollider))
                {
                    continue;
                }

                width = Mathf.Max(width, capsule.radius * 2f);
                height = Mathf.Max(height, capsule.height);
            }

            return width > 0f && height > 0f
                ? CreatureBodyBox.FromMetricSize(width, height, terrain.VoxelSize)
                : new CreatureBodyBox(1, 2);
        }

        private static Vector3Int FootNode(
            CreatureBehaviorAgent agent,
            IVoxelTerrain terrain)
        {
            return FootNode2(agent.transform.position, terrain);
        }

        private static Vector3Int FootNode2(
            Vector3 worldPosition,
            IVoxelTerrain terrain)
        {
            Vector3 local = terrain.TerrainTransform
                .InverseTransformPoint(worldPosition) / terrain.VoxelSize;
            return new Vector3Int(
                Mathf.RoundToInt(local.x),
                Mathf.FloorToInt(local.y + 0.5f),
                Mathf.RoundToInt(local.z));
        }
    }
}
