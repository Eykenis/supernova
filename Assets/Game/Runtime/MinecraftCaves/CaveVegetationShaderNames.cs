namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Shader names shared by runtime code, the editor asset builder and tests.
    /// Runtime code cannot reach <c>ProjectAssetPaths</c>, which lives in the
    /// editor assembly, so the canonical strings live here instead of being
    /// retyped at each call site.
    /// </summary>
    public static class CaveVegetationShaderNames
    {
        public const string CaveGrassBlade = "Supernova/Vegetation/Cave Grass Blade";
    }
}
