using UnityEngine;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Параметры отладочного сценария инициализации.
    /// Создать: правая кнопка в Project ? Create ? VrBattlegrounds ? Debug Bootstrap Config.
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

        [Header("Игрок")]
        [Tooltip("Команда, которая будет назначена локальному игроку автоматически.")]
        public Team autoTeam = Team.Terrorists;

        [Header("Матч")]
        [Tooltip("Автоматически запустить матч как только подключится достаточно игроков.")]
        public bool autoStartMatch = true;

        [Tooltip("Минимальное количество игроков для автостарта матча.")]
        [Min(1)]
        public int minPlayersToAutoStart = 1;

        [Header("Карта")]
        [Tooltip("Автоматически загрузить эту сцену при старте сервера. Оставить пустым — не грузить.")]
        public string autoLoadMapScene = "";
    }
}
