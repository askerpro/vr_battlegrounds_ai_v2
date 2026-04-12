using UnityEngine;

using VrBattlegrounds;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Данные об игровом режиме — название, иконка, идентификатор, команды, префаб логики.
    /// Создать: ПКМ в Project → Create → VrBattlegrounds → Game Mode Data
    ///
    /// <see cref="modePrefab"/> — префаб с компонентом <see cref="GameMode"/>.
    /// MatchManager инстанцирует его при StartMatch() и уничтожает при StopMatch().
    /// Команды из <see cref="teams"/> передаются в режим через <see cref="GameMode.Initialize"/>.
    /// По сети передаётся только строка <see cref="modeId"/> через SyncVar в SessionManager.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GameModeData_",
        menuName  = "VR Battlegrounds/Game Mode Data")]
    public class GameModeData : ScriptableObject
    {
        [Tooltip("Уникальный строковый идентификатор режима.\nПример: 'respawn', 'elimination'")]
        public string modeId = "";

        [Tooltip("Отображаемое название режима в UI.")]
        public string displayName = "";

        [Tooltip("Минимальное количество игроков (суммарно) для начала матча.")]
        [Min(1)]
        public int minPlayersToStart = 2;

        [Tooltip("Иконка режима для меню администратора.")]
        public Sprite icon;

        [Tooltip("Краткое описание режима (опционально).")]
        [TextArea(2, 4)]
        public string description = "";

        [Tooltip("Две команды, участвующие в режиме. Назначить TeamData assets из Assets/Data/Teams/.")]
        public TeamData[] teams = new TeamData[0];

        [Tooltip("Префаб с компонентом GameMode. Инстанцируется MatchManager-ом при StartMatch, уничтожается при StopMatch.\nПрефаб должен содержать компонент-наследник GameMode (RespawnMode, EliminationMode).")]
        public GameObject modePrefab;

        [Tooltip("Префаб интерфейса игрока (VR HUD). Спавнится компонентом PlayerHUDManager локального игрока внутрь его UI-контейнера при старте матча/подключении.")]
        public GameObject hudPrefab;

        public override string ToString() => displayName;
    }
}
