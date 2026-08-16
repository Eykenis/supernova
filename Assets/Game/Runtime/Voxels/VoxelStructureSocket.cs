using System;
using Supernova.MinecraftCaves;
using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>
    /// A connection marker authored inside a voxel structure template. Storing
    /// sockets in the template means a jigsaw piece that uses the template does
    /// not have to restate them, so the marker cannot drift out of sync with the
    /// geometry it belongs to.
    /// </summary>
    [Serializable]
    public sealed class VoxelStructureSocket
    {
        [SerializeField] private string stableId = "socket";
        [Tooltip("Template sample the opening is centred on, in local coordinates.")]
        [SerializeField] private Vector3Int localPosition;
        [Tooltip("Surface the socket faces, relative to the template's forward axis.")]
        [SerializeField]
        private JigsawConnectorDefinition.Face face =
            JigsawConnectorDefinition.Face.Forward;
        [SerializeField]
        private JigsawConnectorDefinition.Role role =
            JigsawConnectorDefinition.Role.Bidirectional;

        [Header("Matching")]
        [SerializeField] private string socketName = "*";
        [SerializeField] private string targetName = "*";
        [SerializeField] private string targetPoolId = "main";
        [SerializeField] private string fallbackPoolId;

        [Header("Opening")]
        [SerializeField, Min(1)] private int openingWidth = 3;
        [Tooltip("Vertical size on a wall socket; forward/back size on an Up/Down socket.")]
        [SerializeField, Min(1)] private int openingHeight = 3;
        [SerializeField, Range(0f, 1f)] private float activationChance = 1f;

        public string StableId => string.IsNullOrWhiteSpace(stableId)
            ? "socket"
            : stableId.Trim();
        public Vector3Int LocalPosition => localPosition;
        public JigsawConnectorDefinition.Face Face => face;
        public JigsawConnectorDefinition.Role Role => role;
        public string SocketName => Normalize(socketName);
        public string TargetName => Normalize(targetName);
        public string TargetPoolId => string.IsNullOrWhiteSpace(targetPoolId)
            ? "main"
            : targetPoolId.Trim();
        public string FallbackPoolId => string.IsNullOrWhiteSpace(fallbackPoolId)
            ? string.Empty
            : fallbackPoolId.Trim();
        public int OpeningWidth => MakeOdd(Mathf.Max(1, openingWidth));
        public int OpeningHeight => Mathf.Max(1, openingHeight);
        public float ActivationChance => Mathf.Clamp01(activationChance);

        public void Configure(
            string socketId,
            Vector3Int templatePosition,
            JigsawConnectorDefinition.Face socketFace,
            JigsawConnectorDefinition.Role socketRole,
            string name,
            string target,
            string targetPool,
            int apertureWidth = 3,
            int apertureHeight = 3,
            float chance = 1f,
            string fallbackPool = "")
        {
            stableId = socketId;
            localPosition = templatePosition;
            face = socketFace;
            role = socketRole;
            socketName = name;
            targetName = target;
            targetPoolId = targetPool;
            openingWidth = apertureWidth;
            openingHeight = apertureHeight;
            activationChance = chance;
            fallbackPoolId = fallbackPool;
        }

        public void ClampToSize(Vector3Int size)
        {
            localPosition = new Vector3Int(
                Mathf.Clamp(localPosition.x, 0, Mathf.Max(0, size.x - 1)),
                Mathf.Clamp(localPosition.y, 0, Mathf.Max(0, size.y - 1)),
                Mathf.Clamp(localPosition.z, 0, Mathf.Max(0, size.z - 1)));
            openingWidth = MakeOdd(Mathf.Max(1, openingWidth));
            openingHeight = Mathf.Max(1, openingHeight);
            activationChance = Mathf.Clamp01(activationChance);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
        }

        private static int MakeOdd(int value)
        {
            return (value & 1) == 0 ? value + 1 : value;
        }
    }
}
