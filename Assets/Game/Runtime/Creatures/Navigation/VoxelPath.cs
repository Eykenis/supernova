using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// One planned route: a sequence of foot nodes to visit in order. A path that
    /// stopped at the node closest to the target reports
    /// <see cref="ReachesTarget"/> as false, matching how Minecraft hands back a
    /// partial path when the goal could not be reached.
    /// </summary>
    public sealed class VoxelPath
    {
        private readonly List<Vector3Int> nodes = new List<Vector3Int>();

        public IReadOnlyList<Vector3Int> Nodes => nodes;
        public bool ReachesTarget { get; private set; }
        public int CurrentIndex { get; private set; }
        public bool IsFinished => CurrentIndex >= nodes.Count;
        public int NodeCount => nodes.Count;

        public Vector3Int CurrentNode =>
            IsFinished ? default : nodes[CurrentIndex];

        public Vector3Int FinalNode =>
            nodes.Count == 0 ? default : nodes[nodes.Count - 1];

        internal void Reset(bool reachesTarget)
        {
            nodes.Clear();
            CurrentIndex = 0;
            ReachesTarget = reachesTarget;
        }

        internal void Append(Vector3Int node)
        {
            nodes.Add(node);
        }

        /// <summary>Reverses the nodes gathered while walking parents backwards.</summary>
        internal void FinishReversedAppend()
        {
            nodes.Reverse();
        }

        /// <summary>
        /// Skips the leading node, which is always where the creature already
        /// stands. A path that consists of only that node reads as finished.
        /// </summary>
        internal void SkipStartNode()
        {
            CurrentIndex = 1;
        }

        public void Advance()
        {
            if (!IsFinished)
            {
                CurrentIndex++;
            }
        }

        public void Invalidate()
        {
            CurrentIndex = nodes.Count;
        }

        public bool TryGetNextNode(out Vector3Int node)
        {
            if (IsFinished)
            {
                node = default;
                return false;
            }

            node = nodes[CurrentIndex];
            return true;
        }
    }
}
