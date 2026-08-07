namespace NutBoltSort
{
    /// <summary>
    /// Single source of truth for all Unity scene names.
    /// Add entries here whenever a new scene is created.
    /// Never hardcode scene name strings in multiple scripts.
    /// </summary>
    public static class SceneNames
    {
        /// <summary>The splash / boot scene (index 0 in Build Settings).</summary>
        public const string Boot = "00_Boot";

        /// <summary>The main menu scene (index 1 in Build Settings).</summary>
        public const string MainMenu = "MainMenu";

        /// <summary>The gameplay scene (index 2 in Build Settings).</summary>
        public const string Gameplay = "BoltNutSortGM";
    }
}
