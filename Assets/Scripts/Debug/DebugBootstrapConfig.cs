using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Параметры отладочного сценария инициализации.
    /// Создать: правая кнопка в Project -> Create -> VrBattlegrounds -> Debug Bootstrap Config.
    /// Назначить в Inspector компонента DebugOrchestrator на сцене.
    ///
    /// Чтобы отключить быструю инициализацию — деактивировать GameObject с DebugOrchestrator
    /// или снять флаг enabled в этом конфиге.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DebugBootstrapConfig",
        menuName  = "VR Battlegrounds/Debug Bootstrap Config")]
    public class DebugBootstrapConfig : ScriptableObject
    {
        [Header("Общие настройки")]
        [Tooltip("Включить быструю инициализацию. Выключить = обычный старт игры.")]
        public bool enabled = true;

        [Tooltip("Если true, хост (игрок, запустивший сервер в редакторе) автоматически получает гибридную роль PlayerAdmin.")]
        public bool hostIsAdmin = true;

        // Поле teamsForAutoAssign удалено: команды раздаёт активный режим
        // (GameMode.ServerAssignTeams) — разминка даёт «Разминку» игроку без команды.

        [Header("Матч")]
        [Tooltip("Автоматически запустить матч как только подключится достаточно игроков.")]
        public bool autoStartGameplay = true;

        [Tooltip("Переопределить минимальное количество игроков для старта (0 = использовать значение из GameModeData). Удобно использовать 1 для соло отладки.")]
        [Min(0)]
        public int minPlayersOverride = 1;

        [Tooltip("Сколько ботов-противников сервер добавит сам при старте (0 — ни одного). " +
                 "Бот — настоящий игрок без шлема: его можно ранить и убить, он считается в раундах. " +
                 "См. DevTools.Bots.BotDirector.")]
        [Min(0)]
        public int botCount = 0;

        [Header("Карта")]
        [Tooltip("Автоматически загрузить эту сцену при старте сервера. Оставить пустым — не грузить.")]
        public string autoLoadMapScene = "";

        [Tooltip("Идентификатор режима (modeId из GameModeData), который запустится после загрузки карты. " +
                 "Оставить пустым — не устанавливать режим автоматически. Пример: \"elimination\".")]
        public string autoGameModeId = "elimination";

        [Header("Сеть (Редактор)")]
        [Tooltip("Роль по умолчанию при запуске без тегов Multiplayer Play Mode.")]
        public VrBattlegrounds.Network.GameNetworkDiscovery.AppRole fallbackEditorRole = VrBattlegrounds.Network.GameNetworkDiscovery.AppRole.Host;

        [Tooltip("Автоматически использовать fallback роль (пропустить UI выбор)")]
        public bool autoStartFallbackRole = false;
    }
}
