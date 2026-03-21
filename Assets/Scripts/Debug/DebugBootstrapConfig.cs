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
        menuName  = "VrBattlegrounds/Debug Bootstrap Config")]
    public class DebugBootstrapConfig : ScriptableObject
    {
        [Header("Общие настройки")]
        [Tooltip("Включить быструю инициализацию. Выключить = обычный старт игры.")]
        public bool enabled = true;

        [Tooltip("Если true, хост (игрок, запустивший сервер в редакторе) автоматически получает гибридную роль PlayerAdmin.")]
        public bool hostIsAdmin = true;

        [Header("Игрок")]
        [Tooltip("Команды для автоматического распределения игроков. Каждый новый игрок идёт в команду с наименьшим числом участников. Оставить пустым — команду не назначать.")]
        public List<TeamData> teamsForAutoAssign = new List<TeamData>();

        [Header("Матч")]
        [Tooltip("Автоматически запустить матч как только подключится достаточно игроков.")]
        public bool autoStartGameplay = true;

        [Tooltip("Минимальное количество игроков для автостарта матча.")]
        [Min(1)]
        public int minPlayersToAutoStart = 1;

        [Header("Карта")]
        [Tooltip("Автоматически загрузить эту сцену при старте сервера. Оставить пустым — не грузить.")]
        public string autoLoadMapScene = "";

        [Tooltip("Идентификатор режима (modeId из GameModeData), который запустится после загрузки карты. " +
                 "Оставить пустым — не устанавливать режим автоматически. Пример: \"elimination\".")]
        public string autoGameModeId = "elimination";
    }
}
