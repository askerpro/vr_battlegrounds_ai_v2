namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>Общие геометрические пороги; штрафы задаёт режим.</summary>
    public static class WallPassRules
    {
        public const float HeadDepth = 0.05f;
        public const float ReleaseDepth = 0.03f;
        public const double ContactSeconds = 0.3;
        public const double ReleaseSeconds = 0.15;
        public const float MaxLean = 0.5f;
        public const float HintClearance = 0.15f;
        public const float HintReleaseClearance = 0.2f;
        public const float HintLean = 0.45f;
        public const float HintReleaseLean = 0.4f;
        public const float SupportRadius = 0.025f;
        public const int RingSamples = 16;
    }
}
