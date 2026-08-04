using System;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// An authored socket on a procedural jigsaw piece. Coordinates are expressed
    /// relative to the piece's forward direction and remain valid after rotation.
    /// </summary>
    [Serializable]
    public sealed class JigsawConnectorDefinition
    {
        public enum Face
        {
            Forward,
            Right,
            Back,
            Left,
        }

        public enum Role
        {
            Input,
            Output,
            Bidirectional,
        }

        public enum Joint
        {
            Aligned,
            Rollable,
        }

        [SerializeField] private string stableId = "socket";
        [SerializeField] private Role role = Role.Bidirectional;
        [SerializeField] private Face face = Face.Forward;
        [SerializeField] private Joint joint = Joint.Aligned;

        [Header("Matching")]
        [SerializeField] private string socketName = "*";
        [SerializeField] private string targetName = "*";
        [SerializeField] private string targetPoolId = "main";
        [SerializeField] private string fallbackPoolId;

        [Header("Placement")]
        [Tooltip("Offset along a wall. For passage side sockets, -1 uses the midpoint.")]
        [SerializeField] private int alongOffset = -1;
        [Tooltip("Offset across a forward/back face from its centre.")]
        [SerializeField] private int lateralOffset;
        [SerializeField, Min(0)] private int verticalOffset = 1;
        [SerializeField, Range(0f, 1f)] private float activationChance = 1f;
        [SerializeField, Min(1)] private int openingWidth = 3;
        [SerializeField, Min(1)] private int openingHeight = 3;

        public void Configure(
            string connectorId,
            Role connectorRole,
            Face connectorFace,
            string name,
            string target,
            string targetPool,
            int wallOffset = -1,
            int crossOffset = 0,
            int yOffset = 1,
            int apertureWidth = 3,
            int apertureHeight = 3,
            float chance = 1f,
            string fallbackPool = "",
            Joint connectorJoint = Joint.Aligned)
        {
            stableId = connectorId;
            role = connectorRole;
            face = connectorFace;
            socketName = name;
            targetName = target;
            targetPoolId = targetPool;
            alongOffset = wallOffset;
            lateralOffset = crossOffset;
            verticalOffset = yOffset;
            openingWidth = apertureWidth;
            openingHeight = apertureHeight;
            activationChance = chance;
            fallbackPoolId = fallbackPool;
            joint = connectorJoint;
            ClampConfiguration();
        }

        internal JigsawConnectorSettings CreateSettings()
        {
            ClampConfiguration();
            return new JigsawConnectorSettings(
                stableId,
                role,
                face,
                joint,
                socketName,
                targetName,
                targetPoolId,
                fallbackPoolId,
                alongOffset,
                lateralOffset,
                verticalOffset,
                activationChance,
                openingWidth,
                openingHeight);
        }

        internal void ClampConfiguration()
        {
            stableId = string.IsNullOrWhiteSpace(stableId)
                ? "socket"
                : stableId.Trim();
            socketName = NormalizeMatch(socketName);
            targetName = NormalizeMatch(targetName);
            targetPoolId = string.IsNullOrWhiteSpace(targetPoolId)
                ? "main"
                : targetPoolId.Trim();
            fallbackPoolId = string.IsNullOrWhiteSpace(fallbackPoolId)
                ? string.Empty
                : fallbackPoolId.Trim();
            alongOffset = Mathf.Max(-1, alongOffset);
            verticalOffset = Mathf.Max(0, verticalOffset);
            activationChance = Mathf.Clamp01(activationChance);
            openingWidth = MakeOdd(Mathf.Max(1, openingWidth));
            openingHeight = Mathf.Max(1, openingHeight);
        }

        private static string NormalizeMatch(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
        }

        private static int MakeOdd(int value)
        {
            return (value & 1) == 0 ? value + 1 : value;
        }
    }
}
