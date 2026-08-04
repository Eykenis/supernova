using System;

namespace Supernova.MinecraftCaves
{
    /// <summary>Worker-thread-safe snapshot of an authored jigsaw socket.</summary>
    public readonly struct JigsawConnectorSettings
    {
        public JigsawConnectorSettings(
            string stableId,
            JigsawConnectorDefinition.Role role,
            JigsawConnectorDefinition.Face face,
            JigsawConnectorDefinition.Joint joint,
            string socketName,
            string targetName,
            string targetPoolId,
            string fallbackPoolId,
            int alongOffset,
            int lateralOffset,
            int verticalOffset,
            float activationChance,
            int openingWidth,
            int openingHeight)
        {
            StableId = string.IsNullOrWhiteSpace(stableId)
                ? "socket"
                : stableId.Trim();
            Role = role;
            Face = face;
            Joint = joint;
            SocketName = NormalizeMatch(socketName);
            TargetName = NormalizeMatch(targetName);
            TargetPoolId = string.IsNullOrWhiteSpace(targetPoolId)
                ? "main"
                : targetPoolId.Trim();
            FallbackPoolId = string.IsNullOrWhiteSpace(fallbackPoolId)
                ? string.Empty
                : fallbackPoolId.Trim();
            AlongOffset = Math.Max(-1, alongOffset);
            LateralOffset = lateralOffset;
            VerticalOffset = Math.Max(0, verticalOffset);
            ActivationChance = Clamp01(activationChance);
            OpeningWidth = MakeOdd(Math.Max(1, openingWidth));
            OpeningHeight = Math.Max(1, openingHeight);
        }

        public string StableId { get; }
        public JigsawConnectorDefinition.Role Role { get; }
        public JigsawConnectorDefinition.Face Face { get; }
        public JigsawConnectorDefinition.Joint Joint { get; }
        public string SocketName { get; }
        public string TargetName { get; }
        public string TargetPoolId { get; }
        public string FallbackPoolId { get; }
        public int AlongOffset { get; }
        public int LateralOffset { get; }
        public int VerticalOffset { get; }
        public float ActivationChance { get; }
        public int OpeningWidth { get; }
        public int OpeningHeight { get; }

        public bool CanAcceptInput => Role != JigsawConnectorDefinition.Role.Output;
        public bool CanEmitOutput => Role != JigsawConnectorDefinition.Role.Input;

        public bool Matches(JigsawConnectorSettings input)
        {
            return CanEmitOutput
                && input.CanAcceptInput
                && MatchesName(TargetName, input.SocketName)
                && MatchesName(input.TargetName, SocketName);
        }

        private static bool MatchesName(string expected, string actual)
        {
            return expected == "*" || actual == "*"
                || string.Equals(expected, actual, StringComparison.Ordinal);
        }

        private static string NormalizeMatch(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
        }

        private static int MakeOdd(int value)
        {
            return (value & 1) == 0 ? value + 1 : value;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
