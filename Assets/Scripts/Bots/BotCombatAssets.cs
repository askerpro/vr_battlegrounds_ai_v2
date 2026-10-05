using UnityEngine;

namespace VrBattlegrounds.Bots
{
    /// <summary>Контроллеры вооружённой позы; собираются из импортированных humanoid-клипов.</summary>
    public sealed class BotCombatAssets : ScriptableObject
    {
        public const string ResourcePath = "Bots/BotCombatAssets";
        public RuntimeAnimatorController pistol;
        public RuntimeAnimatorController rifle;
    }
}
