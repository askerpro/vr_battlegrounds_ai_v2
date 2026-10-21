using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Сцена стенда ботов (Editor/MPPM): ссылки на раскладку и оружие стенда для сценариев. Замеры прежнего пути тела
    /// бота (кейсы T01–T10, пробы выстрела и сети) удалены вместе с ним (bot-input, решение пользователя 2026-10-21);
    /// бот на вводе устройства проверяется демо <see cref="BotStandInputSmoke"/>. Замеры на вводе — отдельной задачей.
    /// </summary>
    public sealed class BotCombatStand : MonoBehaviour
    {
        public MapData Map;
        public GameObject DetourWall, HardCover, SoftCover, VisualCover;
        public WeaponInfo[] Weapons;

        public static BotCombatStand Instance { get; private set; }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
