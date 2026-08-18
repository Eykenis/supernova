namespace Supernova.MinecraftCaves
{
    public static class MinecraftWorldGenerationDebugPassUtility
    {
        public static bool Includes(
            this MinecraftWorldGenerationDebugPass current,
            MinecraftWorldGenerationDebugPass required)
        {
            if (current == MinecraftWorldGenerationDebugPass.FullPipeline)
            {
                return true;
            }

            return current >= required
                && current <= MinecraftWorldGenerationDebugPass.MarkerObjects;
        }

        public static bool IsSelectableDebugPass(
            this MinecraftWorldGenerationDebugPass value)
        {
            return value >= MinecraftWorldGenerationDebugPass.NaturalTerrain
                && value <= MinecraftWorldGenerationDebugPass.MarkerObjects;
        }
    }
}
