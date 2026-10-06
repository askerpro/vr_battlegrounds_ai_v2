using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Хранит выбор администратора для следующей серии матча: режим и список карт.
    /// Переживает смену сцен (объект <c>SessionContext</c>, DontDestroyOnLoad).
    /// Синхронизирует выбор на всех клиентах через SyncVar/SyncList.
    ///
    /// <para>
    /// Здесь же — единственное место поиска данных по идентификатору: режим по
    /// <c>modeId</c> (<see cref="FindModeData"/>, включая разминку) и карта по сцене
    /// (<see cref="FindMap"/>). Раньше режим искался ещё и у «режима сцены» лобби
    /// (<c>GameModeCatalog</c>) — теперь разминка лежит в том же реестре.
    /// </para>
    ///
    /// <para>
    /// Ход серии (какая карта сейчас, общий счёт) — не здесь, а в <see cref="Series"/>:
    /// выбор описывает следующую серию и может меняться, пока текущая идёт.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.SessionManager)]
    public class SessionManager : NetworkBehaviour
    {
        public static SessionManager Instance { get; private set; }

        [Header("Реестры")]
        [Tooltip("Реестр всех карт, включая лобби. Назначить MapRegistry asset.")]
        [SerializeField] private MapRegistry _mapRegistry;

        [Tooltip("Реестр всех игровых режимов, включая разминку. Назначить GameModeRegistry asset.")]
        [SerializeField] private GameModeRegistry _gameModeRegistry;

        // Идентификаторы хранятся как строки — безопасно через смены сцен
        private readonly SyncList<string> _selectedMaps = new SyncList<string>();
        [SyncVar] private string _selectedModeId = "";

        /// <summary>Реестр режимов (включая разминку).</summary>
        public GameModeRegistry ModeRegistry => _gameModeRegistry;

        /// <summary>Реестр карт (включая лобби).</summary>
        public MapRegistry MapRegistry => _mapRegistry;

        /// <summary>Данные первой выбранной карты. Null если карта не выбрана.</summary>
        public MapData SelectedMap => FindMap(SelectedMapScene);

        /// <summary>Данные выбранного режима матча. Null если режим не выбран.</summary>
        public GameModeData SelectedGameModeData
        {
            get
            {
                if (_gameModeRegistry == null)
                {
                    GameLog.Error("[SessionManager] SelectedGameModeData: _gameModeRegistry не назначен в Inspector!");
                    return null;
                }
                if (string.IsNullOrEmpty(_selectedModeId))
                {
                    GameLog.Match.Warning(
                        "[SessionManager] SelectedGameModeData: режим не выбран (_selectedModeId пуст).");
                    return null;
                }
                GameModeData result = _gameModeRegistry.GetById(_selectedModeId);
                if (result == null)
                    GameLog.Error(
                        $"[SessionManager] SelectedGameModeData: режим '{_selectedModeId}' не найден в реестре. " +
                        $"Доступные режимы: {string.Join(", ", System.Array.ConvertAll(_gameModeRegistry.modes, m => m != null ? m.modeId : "null"))}");
                return result;
            }
        }

        /// <summary>
        /// Данные режима по <c>modeId</c> без побочных логов; null — нет в реестре.
        /// Единственный путь от строки к данным режима, и для разминки тоже.
        /// </summary>
        public GameModeData FindModeData(string modeId)
        {
            return _gameModeRegistry != null ? _gameModeRegistry.GetById(modeId) : null;
        }

        /// <summary>Данные карты по имени сцены; null — сцены нет в реестре.</summary>
        public MapData FindMap(string sceneName)
        {
            return _mapRegistry != null ? _mapRegistry.GetBySceneName(sceneName) : null;
        }

        /// <summary>Первая карта выбранной серии.</summary>
        public string SelectedMapScene => _selectedMaps.Count > 0 ? _selectedMaps[0] : "";

        /// <summary>Карты выбранной серии по порядку.</summary>
        public IReadOnlyList<string> SelectedMaps => _selectedMaps;

        /// <summary>Идентификатор выбранного режима.</summary>
        public string SelectedModeId => _selectedModeId;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            transform.SetParent(null); // Ensure it's a root object
            DontDestroyOnLoad(gameObject); // Survive scene transitions
        }

        /// <summary>Серия из одной карты — то, что сейчас выбирает меню.</summary>
        [Server]
        public void SetSession(string mapScene, string modeId)
        {
            SetSeries(modeId, new[] { mapScene });
        }

        /// <summary>
        /// Устанавливает режим и карты следующей серии. Реплицируется клиентам. Только сервер.
        /// </summary>
        [Server]
        public void SetSeries(string modeId, IReadOnlyList<string> mapScenes)
        {
            if (mapScenes == null || mapScenes.Count == 0 || string.IsNullOrEmpty(mapScenes[0]))
            {
                GameLog.Match.Warning("[SessionManager] SetSeries: пустой список карт — игнорируем.");
                return;
            }

            if (string.IsNullOrEmpty(modeId))
            {
                GameLog.Match.Warning("[SessionManager] SetSeries: пустой modeId — игнорируем.");
                return;
            }

            _selectedModeId = modeId;
            _selectedMaps.Clear();
            foreach (string scene in mapScenes)
                if (!string.IsNullOrEmpty(scene)) _selectedMaps.Add(scene);

            GameLog.Match.Info(
                $"[SessionManager] Серия настроена: режим={modeId}, карты={string.Join(" → ", ToArray())}");
        }

        /// <summary>
        /// Начинает выбранную серию (<see cref="Series.ServerBegin"/>): первая карта
        /// грузится и стартует в разминке. Вызывать после <see cref="SetSeries"/>. Только сервер.
        /// </summary>
        [Server]
        public void StartSession()
        {
            if (_selectedMaps.Count == 0)
            {
                GameLog.Error("[SessionManager] StartSession: карта не выбрана.");
                return;
            }

            if (string.IsNullOrEmpty(_selectedModeId))
            {
                GameLog.Error("[SessionManager] StartSession: режим не выбран.");
                return;
            }

            GameLog.Match.Info(
                $"[SessionManager] Запуск серии: режим={_selectedModeId}, карты={string.Join(" → ", ToArray())}");

            if (Series.Instance != null)
            {
                Series.Instance.ServerBegin(ToArray(), _selectedModeId);
                return;
            }

            // Без серии (объект не на SessionContext) — одна карта, как раньше.
            GameLog.Match.Warning("[SessionManager] StartSession: Series нет — грузится только первая карта.");
            MapLoader.Instance?.LoadMap(_selectedMaps[0]);
        }

        private string[] ToArray()
        {
            var result = new string[_selectedMaps.Count];
            for (int i = 0; i < result.Length; i++) result[i] = _selectedMaps[i];
            return result;
        }
    }
}
