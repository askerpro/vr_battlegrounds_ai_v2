using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.UI.HUD;
using VrBattlegrounds.UI.Menu.Overview;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Табло в лазерной сетке зоны спавна (T-47): 4 общих — по одному на грань сетки — и персональный кусок
    /// напротив каждой стены арсенала зоны. Крупный обратный отсчёт, готовность раунда, фаза и время; появляются
    /// и исчезают вместе с сеткой.
    ///
    /// <para>
    /// <b>Видимость не своя.</b> Табло видно ровно тогда, когда видна граница зоны
    /// (<see cref="TeamSpawnZone.BorderVisible"/>, правило — <see cref="SpawnZoneVisibility"/>): подписка на
    /// <see cref="TeamSpawnZone.BorderVisibilityChanged"/>, без собственных таймингов и фаз.
    /// </para>
    ///
    /// <para>
    /// <b>Расстановка — генерацией.</b> Компонент стоит на префабе <c>TeamSpawnZone</c> (значит, на всех
    /// картах). Табло строятся при первом показе сетки по геометрии зоны и её стен
    /// (<see cref="LaserGridLayout"/>, стены — <see cref="SpawnZoneMembership"/>), руками ничего не ставится.
    /// Корень табло — сосед зоны: коробка зоны отмасштабирована неравномерно.
    /// </para>
    ///
    /// <para>
    /// <b>Содержимое</b> — <see cref="LaserGridBoardRules"/> из тех же источников, что часы и обзор планшета
    /// (<see cref="RoundClock"/>, <see cref="WatchScore"/>, <see cref="EliminationMode.PendingReadiness"/>).
    /// Только чтение SyncVar — по сети ничего не шлёт. Снимок собирается каждый кадр, пока табло видно, но
    /// строка пересобирается и уходит в TMP только при изменении (раз в секунду в отсчёте).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TeamSpawnZone))]
    public sealed class LaserGridScreens : MonoBehaviour
    {
        private sealed class PersonalBoard
        {
            public LaserGridBoardView View;
            public ArsenalWallController Wall;
            public LaserGridOwnerInfo Shown;
            public bool Composed;
        }

        private TeamSpawnZone _zone;
        private GameObject _root;
        private readonly List<LaserGridBoardView> _common = new List<LaserGridBoardView>();
        private readonly List<PersonalBoard> _personal = new List<PersonalBoard>();
        private readonly List<string> _pendingNames = new List<string>();

        private LaserGridRoundInfo _shownRound;
        private bool _roundComposed;

        /// <summary>Табло построены (были показаны хоть раз или построены вручную).</summary>
        public bool IsBuilt => _root != null;

        /// <summary>Табло сейчас видны.</summary>
        public bool IsShowing => _root != null && _root.activeSelf;

        public GameObject Root => _root;
        public IReadOnlyList<LaserGridBoardView> CommonBoards => _common;
        public int PersonalCount => _personal.Count;

        public IEnumerable<LaserGridBoardView> PersonalBoards
        {
            get { foreach (PersonalBoard board in _personal) yield return board.View; }
        }

        public LaserGridBoardView PersonalBoardOf(ArsenalWallController wall)
        {
            foreach (PersonalBoard board in _personal)
                if (board.Wall == wall) return board.View;
            return null;
        }

        private TeamSpawnZone Zone => _zone != null ? _zone : (_zone = GetComponent<TeamSpawnZone>());

        private void OnEnable()
        {
            Zone.BorderVisibilityChanged += ApplyBorderVisible;
            ApplyBorderVisible(Zone.BorderVisible);
        }

        private void OnDisable()
        {
            if (_zone != null) _zone.BorderVisibilityChanged -= ApplyBorderVisible;
            if (_root != null) _root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_root == null) return;
            if (Application.isPlaying) Destroy(_root);
            else DestroyImmediate(_root);
        }

        /// <summary>
        /// Сетка появилась или исчезла — табло вместе с ней. Строятся при первом показе. Сетка включается ещё в
        /// <c>OnEnable</c> зоны, до поиска пола в её <c>Start</c>, — тогда постройка ждёт пола (кадр), иначе табло
        /// встали бы от нижней грани коробки, уходящей под землю.
        /// </summary>
        public void ApplyBorderVisible(bool visible)
        {
            _wantVisible = visible;
            if (visible && _root == null)
            {
                if (Application.isPlaying && !Zone.BorderFloorReady) return;
                Build();
            }
            if (_root == null) return;

            if (_root.activeSelf != visible) _root.SetActive(visible);
            if (visible) Refresh(force: true);
        }

        private bool _wantVisible;

        private void Update()
        {
            if (_wantVisible && _root == null && Zone.BorderFloorReady) ApplyBorderVisible(true);
            if (IsShowing) Refresh(force: false);
        }

        // ── Расстановка ───────────────────────────────────────

        /// <summary>
        /// Строит табло по текущей геометрии (повторный вызов перестраивает). Стены и зоны — все на загруженных
        /// сценах; редактор и тесты могут передать свои (сцена-превью).
        /// </summary>
        public void Build(IEnumerable<ArsenalWallController> allWalls = null, IEnumerable<TeamSpawnZone> allZones = null)
        {
            Clear();

            TeamSpawnZone zone = Zone;
            IEnumerable<TeamSpawnZone> zones = allZones ?? FindObjectsByType<TeamSpawnZone>(FindObjectsInactive.Exclude);
            IEnumerable<ArsenalWallController> all = allWalls ?? FindObjectsByType<ArsenalWallController>(FindObjectsInactive.Exclude);
            List<ArsenalWallController> walls = WallsOf(zone, all, zones);
            List<LaserGridScreenPose> poses = Plan(zone, walls);

            // Корень — в корне сцены зоны, а не под ней: коробка зоны отмасштабирована неравномерно, а группа
            // зоны может нести свой масштаб. Табло стоят в мировых координатах раскладки.
            _root = new GameObject("LaserGridScreens_" + zone.name);
            if (_root.scene != zone.gameObject.scene && zone.gameObject.scene.IsValid())
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_root, zone.gameObject.scene);

            int commonIndex = 0;
            foreach (LaserGridScreenPose pose in poses)
            {
                if (pose.Kind == LaserGridScreenKind.Common)
                {
                    _common.Add(LaserGridBoardView.Create(_root.transform, pose, $"Common_{pose.Face}_{commonIndex++}"));
                    continue;
                }

                ArsenalWallController wall = walls[pose.WallId];
                _personal.Add(new PersonalBoard
                {
                    View = LaserGridBoardView.Create(_root.transform, pose, "Personal_" + wall.name),
                    Wall = wall
                });
            }

            _roundComposed = false;
            GameLog.Match.Info($"[LaserGridScreens] {zone.name}: табло в сетке построены — общих {_common.Count}, " +
                               $"персональных {_personal.Count} (стен в зоне {walls.Count}).", this);
        }

        private void Clear()
        {
            if (_root != null)
            {
                if (Application.isPlaying) Destroy(_root);
                else DestroyImmediate(_root);
            }
            _root = null;
            _common.Clear();
            _personal.Clear();
        }

        /// <summary>Стены арсенала, стоящие в этой зоне (в порядке имён — раскладка стабильна).</summary>
        public static List<ArsenalWallController> WallsOf(TeamSpawnZone zone, IEnumerable<ArsenalWallController> all,
                                                          IEnumerable<TeamSpawnZone> zones)
        {
            var result = new List<ArsenalWallController>();
            foreach (ArsenalWallController wall in all)
                if (wall != null && SpawnZoneMembership.ZoneOf(wall.transform, zones) == zone) result.Add(wall);
            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        /// <summary>Раскладка табло зоны; <c>WallId</c> персонального — индекс стены в <paramref name="walls"/>.</summary>
        public static List<LaserGridScreenPose> Plan(TeamSpawnZone zone, IReadOnlyList<ArsenalWallController> walls) =>
            Plan(DescribeBox(zone), walls);

        /// <summary>Раскладка по готовой коробке (тест на сцене-превью задаёт пол сам: <c>Start</c> зоны там не идёт).</summary>
        public static List<LaserGridScreenPose> Plan(LaserGridBox box, IReadOnlyList<ArsenalWallController> walls)
        {
            var described = new List<LaserGridWall>(walls.Count);
            for (int i = 0; i < walls.Count; i++) described.Add(DescribeWall(walls[i], i));
            return LaserGridLayout.Build(box, described);
        }

        /// <summary>Коробка сетки зоны в метрах мира: масштаб и поворот учтены, пол — найденный зоной.</summary>
        public static LaserGridBox DescribeBox(TeamSpawnZone zone) => DescribeBox(zone, zone.BorderFloorY);

        /// <summary>Коробка сетки зоны с заданным полом.</summary>
        public static LaserGridBox DescribeBox(TeamSpawnZone zone, float floorY)
        {
            Transform t = zone.transform;
            BoxCollider box = zone.GetComponent<BoxCollider>();
            Vector3 center = t.TransformPoint(box.center);
            Vector3 scale = t.lossyScale;
            return new LaserGridBox(center, t.eulerAngles.y,
                0.5f * scale.x * box.size.x, 0.5f * scale.z * box.size.z,
                floorY, zone.BorderTopY);
        }

        /// <summary>
        /// Стена по её видимой геометрии: границы мешей без текста (табло кошелька) и без предметов на полках
        /// (у них <c>Rigidbody</c>) — их на стене то нет, то есть.
        /// </summary>
        public static LaserGridWall DescribeWall(ArsenalWallController wall, int id)
        {
            bool any = false;
            Bounds bounds = new Bounds(wall.transform.position, Vector3.zero);

            foreach (Renderer renderer in wall.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponent<TMP_Text>() != null) continue;
                if (renderer.GetComponentInParent<Rigidbody>() != null) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!any) return new LaserGridWall(id, wall.transform.position, 0.5f, wall.transform.position.y + 2f);

            // Ширина вдоль стены — проекция границ на её ось X (стены стоят вдоль граней зоны).
            Vector3 right = wall.transform.right;
            float halfWidth = Mathf.Abs(right.x) * bounds.extents.x + Mathf.Abs(right.y) * bounds.extents.y +
                              Mathf.Abs(right.z) * bounds.extents.z;
            return new LaserGridWall(id, bounds.center, halfWidth, bounds.max.y);
        }

        // ── Содержимое ────────────────────────────────────────

        private void Refresh(bool force)
        {
            LaserGridRoundInfo round = ReadRound(out EliminationMode elimination);
            bool roundChanged = force || !_roundComposed || !round.Equals(_shownRound);

            if (roundChanged)
            {
                _shownRound = round;
                _roundComposed = true;
                CollectPendingNames(elimination);
                string common = LaserGridBoardRules.Common(round, _pendingNames).ToRichText();
                foreach (LaserGridBoardView view in _common) view.SetText(common);
            }

            foreach (PersonalBoard board in _personal)
            {
                LaserGridOwnerInfo owner = ReadOwner(board.Wall, elimination);
                if (!roundChanged && board.Composed && owner.Equals(board.Shown)) continue;
                board.Shown = owner;
                board.Composed = true;
                board.View.SetText(LaserGridBoardRules.Personal(round, owner).ToRichText());
            }
        }

        private int ZoneTeamIndex
        {
            get
            {
                TeamData team = Zone.Team;
                return team != null ? team.teamIndex : -1;
            }
        }

        /// <summary>Снимок раунда без выделений памяти — зовётся каждый кадр, пока табло видно.</summary>
        private LaserGridRoundInfo ReadRound(out EliminationMode elimination)
        {
            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            elimination = mode as EliminationMode;
            int teamIndex = ZoneTeamIndex;

            var r = new LaserGridRoundInfo
            {
                Phase = LaserGridBoardRules.Classify(mode != null, elimination != null,
                    elimination != null ? elimination.CurrentState : EliminationState.WaitingForPlayers,
                    elimination != null ? elimination.CurrentRoundPhase : RoundPhase.Setup),
                Seconds = -1
            };

            if (r.Phase == LaserGridBoardPhase.Equipment)
            {
                // В закупке — остаток закупки; без предела ожидания показывать нечего (время раунда не о том).
                float equipment = elimination.EquipmentTimeRemaining;
                r.Seconds = equipment > 0f ? OverviewFormat.ClockSeconds(equipment) : -1;
            }
            else if (RoundClock.TryGetTimeRemaining(mode, out float seconds, out _))
            {
                r.Seconds = OverviewFormat.ClockSeconds(seconds);
            }

            r.HasScore = WatchScore.TryGet(mode, teamIndex, out r.Own, out r.Enemy);

            if (elimination == null) return r;

            r.CountdownHeld = elimination.CountdownHeld;
            r.Round = elimination.CurrentRoundNumber;
            r.TotalRounds = elimination.TotalRounds;
            r.ByTimer = elimination.RoundStartRule == RoundStartRule.Timer;

            PlayersManager players = PlayersManager.Instance;
            if (players == null) return r;

            IReadOnlyList<PlayerSession> sessions = players.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                PlayerSession s = sessions[i];
                if (s == null || s.Role == GameRole.Spectator || s.TeamIndex < 0) continue;

                bool pending = elimination.PendingReadiness.IndexOf(s.netId) >= 0;
                if (s.TeamIndex == teamIndex)
                {
                    r.TeamPlayers++;
                    if (!pending) continue;
                    r.TeamPending++;
                    r.PendingKey = unchecked(r.PendingKey * 31 + (int)s.netId);
                }
                else if (pending)
                {
                    r.EnemyPending++;
                }
            }

            return r;
        }

        /// <summary>Имена ожидаемых своей команды — только когда снимок изменился.</summary>
        private void CollectPendingNames(EliminationMode elimination)
        {
            _pendingNames.Clear();
            PlayersManager players = PlayersManager.Instance;
            if (elimination == null || players == null) return;

            int teamIndex = ZoneTeamIndex;
            foreach (PlayerSession s in players.Sessions)
            {
                if (s != null && s.TeamIndex == teamIndex && s.Role != GameRole.Spectator &&
                    elimination.PendingReadiness.IndexOf(s.netId) >= 0)
                    _pendingNames.Add(s.PlayerName);
            }
        }

        private static LaserGridOwnerInfo ReadOwner(ArsenalWallController wall, EliminationMode elimination)
        {
            if (wall == null || wall.OwnerSessionNetId == 0) return default;

            PlayerSession owner = wall.OwnerSession;
            if (owner == null) return new LaserGridOwnerInfo { HasOwner = true, Name = null };

            PlayerController avatar = owner.ActiveAvatar;
            return new LaserGridOwnerInfo
            {
                HasOwner = true,
                Name = owner.PlayerName,
                IsLocal = owner == PlayerSession.LocalSession,
                Pending = elimination != null && elimination.PendingReadiness.IndexOf(owner.netId) >= 0,
                InZone = owner.IsInSpawnZone,
                Alive = avatar != null && avatar.IsAlive
            };
        }
    }
}
