using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Managers;
using VrBattlegrounds.GameModes;
using Mirror;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Коллайдер-зона спавна конкретной команды.
    /// Отслеживает, кто из игроков вошёл, проверяет принадлежность к нужной командой, 
    /// может ответить на вопрос "Все ли живые игроки команды находятся в этой зоне?".
    /// Требует BoxCollider (isTrigger = true) на том же GameObject.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TeamSpawnZone : MonoBehaviour
    {
        [Tooltip("Команда, которой принадлежит эта зона")]
        [SerializeField] private TeamData _team;

        [Tooltip("Где появляется аватар. Пусто — центр зоны. Нужна, когда центр занят (зона лобби на всю " +
                 "арену: в центре — тумба арсенала).")]
        [SerializeField] private Transform _spawnPoint;

        /// <summary>Точка спавна в этой зоне (<c>AvatarSpawnPointResolver</c>): своя или центр зоны.</summary>
        public Transform SpawnPoint => _spawnPoint != null ? _spawnPoint : transform;

        [Header("Visibility Settings")]
        [Tooltip("Материал для эффекта X-ray (видимость сквозь стены)")]
        [SerializeField] private Material _xrayMaterial;

        [Header("Debug View (ReadOnly)")]
        [SerializeField] private int _playersInZoneCount;
        [SerializeField] private RoundState _currentRoundState;

        /// <summary>Срабатывает когда игрок входит в зону. Передаётся сам контроллер игрока.</summary>
        public event Action<TeamSpawnZone, PlayerController> PlayerEntered;

        /// <summary>Срабатывает когда игрок покидает зону (перестал быть полностью внутри или вышел совсем).</summary>
        public event Action<TeamSpawnZone, PlayerController> PlayerExited;

        private BoxCollider _boxCollider;
        private MeshRenderer _meshRenderer;
        private Material _originalMaterial;
        private PlayerController _localPlayer;

        // Игроки, которые физически касаются триггера (кандидаты на проверку полного входа)
        private readonly HashSet<PlayerController> _playersTouching = new HashSet<PlayerController>();

        // Игроки, чья голова ПОЛНОСТЬЮ внутри зоны. 
        // Именно этот список видят внешние скрипты через GetPlayersInZone() и события.
        private readonly HashSet<PlayerController> _playersInZone = new HashSet<PlayerController>();

        // Проверка полного нахождения считается на лету, в локальных координатах коллайдера
        // (см. IsHeadCenterInZone). Раньше здесь лежал кэш мирового AABB, посчитанный в Awake, —
        // он и был находкой VR-05: зона, которую подвинули после Awake, проверялась по старому
        // месту, а у повёрнутой зоны AABB заметно больше самой зоны.

        /// <summary>Половина размера коллайдера головы игрока (Camera BoxCollider). </summary>
        private static readonly Vector3 HeadHalfExtents = new Vector3(0.125f, 0.11f, 0.09f);

        // Кеш: PlayerController → Transform камеры, чтобы не делать GetComponentInChildren каждый Stay
        private readonly Dictionary<PlayerController, Transform> _cameraTransformCache =
            new Dictionary<PlayerController, Transform>();

        /// <summary>
        /// Чья зона сейчас. Сериализованная <see cref="HomeTeam"/> — хозяин первой половины карты;
        /// после смены сторон (<see cref="SpawnSides"/>) зона принадлежит другой команде. Все
        /// потребители — спавн, возрождение, «в своей зоне», подсветка — читают это свойство.
        /// </summary>
        public TeamData Team => SpawnSides.Resolve(_team);

        /// <summary>Хозяин зоны в первой половине карты — как настроено на сцене.</summary>
        public TeamData HomeTeam => _team;

        /// <summary>Высота пола под зоной (мир) — для шейдера границы. Нет пола — ниже всего.</summary>
        private float _floorY = NoFloor;
        private const float NoFloor = -100000f;

        /// <summary>
        /// Пол ищется в <c>Start</c>, а не в <c>Awake</c>: к нему коллайдеры карты уже включены.
        /// Коробка зоны на картах уходит под землю, и пол границы рисуется на найденной высоте.
        /// </summary>
        private void Start()
        {
            _floorY = FindFloorHeight();
            UpdateColor();
        }

        /// <summary>
        /// Пол под зоной: лучи сверху вниз из центра и у углов, по каждому — первая поверхность,
        /// смотрящая вверх (не игрок, не триггер); из них — самая низкая. Мебель и стены арсенала
        /// внутри зоны выше пола, поэтому минимум по лучам — пол.
        /// </summary>
        private float FindFloorHeight()
        {
            float best = float.PositiveInfinity;
            float height = Mathf.Abs(transform.lossyScale.y * _boxCollider.size.y);

            foreach (Vector2 xz in FloorProbes)
            {
                Vector3 top = transform.TransformPoint(_boxCollider.center + Vector3.Scale(new Vector3(xz.x, 0.5f, xz.y), _boxCollider.size));
                RaycastHit[] hits = Physics.RaycastAll(top, Vector3.down, height, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (h1, h2) => h1.distance.CompareTo(h2.distance));

                foreach (RaycastHit hit in hits)
                {
                    if (hit.normal.y < 0.7f || hit.collider.GetComponentInParent<PlayerController>() != null) continue;
                    best = Mathf.Min(best, hit.point.y);
                    break;
                }
            }

            if (float.IsPositiveInfinity(best))
            {
                GameLog.Match.Info($"[TeamSpawnZone] {name}: пол под зоной не найден — граница рисуется по нижней грани.", this);
                return NoFloor;
            }
            return best;
        }

        private static readonly Vector2[] FloorProbes =
        {
            new Vector2(0f, 0f), new Vector2(0.35f, 0.35f), new Vector2(-0.35f, 0.35f),
            new Vector2(0.35f, -0.35f), new Vector2(-0.35f, -0.35f)
        };

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();
            _boxCollider.isTrigger = true;
            _meshRenderer = GetComponent<MeshRenderer>();

            if (_meshRenderer != null)
            {
                _originalMaterial = _meshRenderer.sharedMaterial;
            }

            // Ensure there's a kinematic rigidbody so trigger events fire regardless of the player's rigidbody setup
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            if (_team == null)
            {
                GameLog.Match.Warning(
                    $"[TeamSpawnZone] У SpawnZone на объекте {gameObject.name} не назначена команда (_team).");
            }
            else
            {
                UpdateColor();
            }
        }

        /// <summary>
        /// Голова игрока целиком внутри зоны? На вход — мировая позиция центра камеры.
        ///
        /// Считается в локальных координатах коллайдера, а не через мировой AABB. Причин две.
        /// Во-первых, AABB повёрнутой зоны заметно больше самой зоны, и точка за её гранью
        /// считалась внутренней — обещанной в комментарии точности «±5–12 см» для повёрнутых
        /// зон не было вовсе. Во-вторых, кэшировать теперь нечего: зона, которую подвинули,
        /// повернули или отмасштабировали после <c>Awake</c>, проверяется по своему текущему
        /// положению, а не по тому, где она была на старте (находка VR-05).
        ///
        /// Приближение осталось одно: голова считается коробкой, выровненной по осям зоны, —
        /// её собственный поворот не учитывается. Точность ~±<see cref="HeadHalfExtents"/>
        /// (5–12 см), стоимость — одно умножение точки на матрицу и три сравнения.
        /// </summary>
        public bool IsHeadCenterInZone(Vector3 worldHeadCenter)
        {
            Vector3 local = transform.InverseTransformPoint(worldHeadCenter) - _boxCollider.center;

            // Размер головы задан в метрах мира — переводим в локальные единицы коллайдера.
            Vector3 scale = transform.lossyScale;
            Vector3 headHalf = new Vector3(
                ToLocalExtent(HeadHalfExtents.x, scale.x),
                ToLocalExtent(HeadHalfExtents.y, scale.y),
                ToLocalExtent(HeadHalfExtents.z, scale.z));

            // Насколько центр головы может отойти от центра зоны, оставаясь целиком внутри.
            // Отрицательное значение по любой оси означает, что зона уже головы и целиком
            // в неё не поместиться — сравнение по модулю само вернёт false.
            Vector3 allowed = _boxCollider.size * 0.5f - headHalf;

            return Mathf.Abs(local.x) <= allowed.x
                && Mathf.Abs(local.y) <= allowed.y
                && Mathf.Abs(local.z) <= allowed.z;
        }

        /// <summary>Мировой размер в локальные единицы. Нулевой масштаб схлопывает зону в ничто.</summary>
        private static float ToLocalExtent(float worldExtent, float scale)
        {
            float absScale = Mathf.Abs(scale);
            return absScale > Mathf.Epsilon ? worldExtent / absScale : float.PositiveInfinity;
        }

        /// <summary>
        /// Стороны поменялись: зона сменила хозяина. Перекрашивается, а стоящим внутри сервер
        /// заново сообщает, чья она теперь, — иначе «в своей зоне» осталось бы от прошлой половины.
        /// </summary>
        private void OnSidesChanged()
        {
            UpdateColor();
            UpdateVisibility();

            foreach (PlayerController player in _playersInZone)
            {
                ReportZoneState(player, true);
            }
        }

        private void OnEnable()
        {
            SpawnSides.Changed += OnSidesChanged;
            EliminationMode.OnRoundStateChangedLocal += OnRoundStateChanged;
            PlayerSession.LocalAvatarChanged += OnLocalAvatarChanged;

            // Аватар мог заспавниться раньше, чем включилась зона: зоны живут в сцене карты,
            // а сессия переживает её загрузку.
            OnLocalAvatarChanged(PlayerSession.LocalSession != null
                ? PlayerSession.LocalSession.ActiveAvatar
                : null);

            UpdateVisibility();
        }

        private void OnDisable()
        {
            SpawnSides.Changed -= OnSidesChanged;
            EliminationMode.OnRoundStateChangedLocal -= OnRoundStateChanged;
            PlayerSession.LocalAvatarChanged -= OnLocalAvatarChanged;

            if (_localPlayer != null)
            {
                _localPlayer.PlayerDied -= OnLocalPlayerDied;
            }
            _localPlayer = null;
        }

        /// <summary>
        /// Локальный аватар появился, сменился или исчез.
        ///
        /// Раньше зона искала его в <c>Update</c> каждый кадр — и искала не там:
        /// в <c>NetworkClient.localPlayer</c> лежит <see cref="PlayerSession"/>, а не аватар,
        /// поэтому поиск не находил ничего никогда (находка NET-04). Теперь связь
        /// реплицируется, и зона просто подписана на её изменение.
        /// </summary>
        private void OnLocalAvatarChanged(PlayerController avatar)
        {
            // ReferenceEquals, а не ==: уничтоженный аватар по-Unity равен null,
            // и переход «был → уничтожен» иначе не обработался бы.
            if (ReferenceEquals(_localPlayer, avatar)) return;

            if (_localPlayer != null)
            {
                _localPlayer.PlayerDied -= OnLocalPlayerDied;
            }

            _localPlayer = avatar;

            if (_localPlayer != null)
            {
                _localPlayer.PlayerDied += OnLocalPlayerDied;
            }

            UpdateVisibility();
        }

        private void OnRoundStateChanged(RoundState newState)
        {
            GameLog.Match.Verbose($"[TeamSpawnZone] {name}: фаза раунда {_currentRoundState} → {newState}.", this);
            _currentRoundState = newState;
            UpdateVisibility();
        }

        private void OnLocalPlayerDied(PlayerController player)
        {
            UpdateVisibility();
        }

        private bool _lastLocalAlive = true;
        private GameMode _lastMode;

        /// <summary>
        /// Жив ли свой игрок и какой режим — сверяется каждый кадр: выбывание в конце боя, вход нового
        /// аватара выбывшим и возрождение идут мимо <c>PlayerDied</c>, а смена режима на той же карте
        /// («Начать матч» из разминки) — мимо фазы раунда. Граница зависит от обоих напрямую.
        /// </summary>
        private void Update()
        {
            bool alive = _localPlayer == null || _localPlayer.IsAlive;
            GameMode mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode : null;
            if (alive == _lastLocalAlive && ReferenceEquals(mode, _lastMode)) return;

            _lastLocalAlive = alive;
            _lastMode = mode;
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (_meshRenderer == null) return;

            bool hasLocal = _localPlayer != null;
            bool alive = hasLocal && _localPlayer.IsAlive;
            // Session проверяется на null: на клиенте сессия может ещё не разрешиться.
            bool ownTeam = hasLocal && _localPlayer.Session != null && _localPlayer.Session.Team == Team;

            GameMode mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode : null;
            bool modeShowsZones = mode != null && mode.ShowsSpawnZones;

            SpawnZoneVisibility.Decide(modeShowsZones, hasLocal, alive, ownTeam, _currentRoundState, out bool isVisible, out bool xray);

            if (_meshRenderer.enabled != isVisible)
            {
                string who = hasLocal
                    ? $"локальный {_localPlayer.name}: жив={alive}, своя зона={ownTeam}"
                    : "локального аватара нет";
                GameLog.Match.Info(
                    $"[TeamSpawnZone] {name} ({(Team != null ? Team.Name : "без команды")}): {(isVisible ? "показана" : "скрыта")} — " +
                    $"режим {(mode != null ? mode.GetType().Name : "нет")} (границы {(modeShowsZones ? "рисует" : "не рисует")}), фаза {_currentRoundState}, {who}.", this);
            }

            _meshRenderer.enabled = isVisible;
            if (!isVisible) return;

            _meshRenderer.sharedMaterial = xray && _xrayMaterial != null ? _xrayMaterial : _originalMaterial;
            UpdateColor();
        }

        private void OnValidate()
        {
            UpdateColor();
        }

        private void UpdateColor()
        {
            if (Team != null)
            {
                MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    // Use MaterialPropertyBlock instead of .material to avoid leaking materials
                    // or throwing "Not allowed to access Renderer.material on prefab object" errors
                    // when editing prefabs in Play Mode.
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    meshRenderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", Team.color);
                    block.SetColor("_Color", Team.color); // support both URP and standard shaders
                    block.SetFloat("_FloorY", _floorY);
                    meshRenderer.SetPropertyBlock(block);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();

            // Если коллайдер не принадлежит игроку или игрок УЖЕ касается — игнорируем
            if (player != null && _playersTouching.Add(player))
            {
                // Кешируем Transform камеры один раз при первом контакте игрока с зоной
                if (!_cameraTransformCache.ContainsKey(player))
                {
                    Camera cam = player.GetComponentInChildren<Camera>(true);
                    if (cam != null)
                        _cameraTransformCache[player] = cam.transform;
                }
            }
        }

        private void OnTriggerStay(Collider other)
        {
            // Быстрая проверка полного нахождения: O(1) на игрока
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || !_playersTouching.Contains(player)) return;

            if (!_cameraTransformCache.TryGetValue(player, out Transform camT) || camT == null) return;

            bool isCurrentlyFullyInside = IsHeadCenterInZone(camT.position);
            bool wasAlreadyFullyInside = _playersInZone.Contains(player);

            if (isCurrentlyFullyInside && !wasAlreadyFullyInside)
            {
                // СОБЫТИЕ: Игрок зашёл ЦЕЛИКОМ
                _playersInZone.Add(player);
                _playersInZoneCount = _playersInZone.Count;

                ReportZoneState(player, true);

                PlayerEntered?.Invoke(this, player);

                GameLog.Match.Verbose(
                    $"[TeamSpawnZone] Игрок {player.name} вошёл в зону '{name}' (полное нахождение)");
            }
            else if (!isCurrentlyFullyInside && wasAlreadyFullyInside)
            {
                // СОБЫТИЕ: Игрок больше не внутри целиком (но всё ещё касается колайдером)
                _playersInZone.Remove(player);
                _playersInZoneCount = _playersInZone.Count;

                ReportZoneState(player, false);

                PlayerExited?.Invoke(this, player);

                GameLog.Match.Verbose(
                    $"[TeamSpawnZone] Игрок {player.name} покинул зону '{name}' (вышел из режима полного нахождения)");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();

            // Если игрок совсем перестал касаться триггера
            if (player != null && _playersTouching.Remove(player))
            {
                // Если он до этого момента считался "внутри целиком", вызываем событие выхода
                if (_playersInZone.Remove(player))
                {
                    _playersInZoneCount = _playersInZone.Count;

                    ReportZoneState(player, false);

                    PlayerExited?.Invoke(this, player);

                    GameLog.Match.Verbose(
                        $"[TeamSpawnZone] Игрок {player.name} покинул зону '{name}' (вышел совсем)");
                }

                _cameraTransformCache.Remove(player);
            }
        }

        /// <summary>
        /// Сообщает сессии игрока, что он вошёл в <b>эту</b> зону или вышел из неё.
        ///
        /// <para>
        /// Зона сообщает только факт и <b>свою команду</b>. Что этот факт значит для
        /// конкретного игрока — «я у себя на спавне» или «я в базе противника», —
        /// решает <see cref="PlayerSession" />: там лежит команда игрока.
        /// </para>
        ///
        /// <para>
        /// Здесь была находка <b>RDY-04</b>: обе точки писали
        /// <c>ServerSetInSpawnZone(true/false)</c> любому вошедшему, не спрашивая
        /// команду, — хотя соседние методы того же класса
        /// (<see cref="GetTeamPlayersInZone" />, <see cref="AreAllTeamPlayersFullyInZone" />)
        /// команду проверяли. Игрок, забредший в базу противника, считался стоящим
        /// «в своей зоне» и сохранял право на готовность к раунду.
        /// </para>
        /// </summary>
        private void ReportZoneState(PlayerController player, bool inside)
        {
            // Учёт ведёт сервер: IsInSpawnZone — условие готовности, а её судит он.
            if (!NetworkServer.active) return;

            // Session — null в окне между спавном аватара и спавном его сессии.
            if (player == null || player.Session == null) return;

            // Зона без команды не знает, чья она, и сказать о себе ей нечего.
            // О самом факте уже предупредил Awake.
            if (Team == null) return;

            if (inside)
                player.Session.ServerEnterSpawnZone(Team.teamIndex);
            else
                player.Session.ServerExitSpawnZone(Team.teamIndex);
        }

        /// <summary>Возвращает копию списка всех игроков, физически находящихся в зоне.</summary>
        public List<PlayerController> GetPlayersInZone()
        {
            return new List<PlayerController>(_playersInZone);
        }

        /// <summary>Возвращает игроков, которые находятся в зоне и чья команда совпадает с Team.</summary>
        public List<PlayerController> GetTeamPlayersInZone()
        {
            List<PlayerController> result = new List<PlayerController>();
            if (Team == null) return result;

            foreach (var p in _playersInZone)
            {
                // Session — null в окне между спавном аватара и спавном его сессии
                // (порядок доставки спавнов Mirror не гарантирует). Раньше здесь
                // вылетал NullReferenceException и уносил с собой весь вызывающий код.
                if (p != null && p.Session != null && p.Session.Team == Team)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>Возвращает всех ЖИВЫХ игроков из PlayersManager требуемой команды, которых физически НЕТ в этой зоне.</summary>
        public List<PlayerController> GetTeamPlayersNotInZone()
        {
            List<PlayerController> result = new List<PlayerController>();
            if (Team == null || PlayersManager.Instance == null) return result;

            IEnumerable<PlayerSession> allAliveInTeam = PlayersManager.Instance.GetAlivePlayers(Team);
            foreach (var session in allAliveInTeam)
            {
                var alivePlayer = session.ActiveAvatar;
                if (alivePlayer != null && !_playersInZone.Contains(alivePlayer))
                {
                    result.Add(alivePlayer);
                }
            }

            return result;
        }

        /// <summary>Проверяет, все ли ЖИВЫЕ члены команды находятся в этом триггере (касание).</summary>
        public bool AreAllTeamPlayersInZone()
        {
            if (Team == null || PlayersManager.Instance == null) return false;

            var notInZone = GetTeamPlayersNotInZone();
            return notInZone.Count == 0;
        }

        // ─── Полное нахождение (голова целиком внутри) ────────────────────────────

        /// <summary>
        /// Быстрая проверка: голова игрока (Camera BoxCollider) целиком внутри зоны.
        /// Геометрия — в <see cref="IsHeadCenterInZone"/>, здесь только поиск камеры игрока.
        /// </summary>
        public bool IsPlayerFullyInZone(PlayerController player)
        {
            if (player == null || !_playersTouching.Contains(player)) return false;
            if (!_cameraTransformCache.TryGetValue(player, out Transform camT) || camT == null) return false;
            return IsHeadCenterInZone(camT.position);
        }

        /// <summary>
        /// Возвращает true, если все ЖИВЫЕ игроки команды полностью (головой) внутри зоны.
        /// Оптимизировано: проверяем текущий состав _playersInZone.
        /// </summary>
        public bool AreAllTeamPlayersFullyInZone()
        {
            if (Team == null || PlayersManager.Instance == null) return false;

            foreach (PlayerSession session in PlayersManager.Instance.GetAlivePlayers(Team))
            {
                var alive = session.ActiveAvatar;
                if (alive != null && !_playersInZone.Contains(alive))
                    return false;
            }
            return true;
        }

        /// <summary>Возвращает копию множества игроков, голова которых ПОЛНОСТЬЮ внутри зоны.</summary>
        public List<PlayerController> GetPlayersFullyInZone()
        {
            return new List<PlayerController>(_playersInZone);
        }
    }

    /// <summary>
    /// Кому видна граница зоны. Рисовать ли границы вообще, решает режим (<c>GameMode.ShowsSpawnZones</c>):
    /// разминка — свободная арена без границ. Дальше общее правило: игрок видит только свою зону — чужие не видны никогда,
    /// без своего аватара своей зоны нет. Выбывший видит свою ярко и сквозь стены: по цвету её пола он
    /// находит базу. Живой (он бывает только в закупке, на отсчёте и в бою) видит свою до боя — где
    /// стоять; в бою граница пропадает.
    /// </summary>
    public static class SpawnZoneVisibility
    {
        public static void Decide(bool modeShowsZones, bool hasLocalAvatar, bool alive, bool ownTeam, RoundState state,
                                  out bool visible, out bool xray)
        {
            xray = false;
            visible = false;
            if (!modeShowsZones || !hasLocalAvatar || !ownTeam) return;

            if (!alive)
            {
                visible = true;
                xray = true;
                return;
            }

            visible = state != RoundState.Combat;
        }
    }
}
