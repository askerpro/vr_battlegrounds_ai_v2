// Ярус C (несколько процессов) — барьер шагов: журнал шагов, рукопожатие e2e-ready и сообщения Mirror только для e2e.
// Отключить из сборки: VRBG_NO_E2E (GameBuilder ставит его прод-сборке всех профилей).
// VRBG_E2E_CORE_ONLY — только для харнесса Tools/TestStand: компилирует чистую логику без Mirror.
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using VrBattlegrounds.Core;
#if !VRBG_E2E_CORE_ONLY
using Mirror;
#endif

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>Исход записи журнала шагов — поле <c>outcome</c> в <c>steps</c> результата процесса.</summary>
    public static class E2EStepOutcome
    {
        /// <summary>Процесс впервые достиг шага (<c>ctx.Step</c> / <c>ctx.ServerStep</c>).</summary>
        public const string Reached = "reached";
        /// <summary>Повтор шага с новым data: первое достижение остаётся, data — последнее.</summary>
        public const string Updated = "updated";
        /// <summary>Шаг другой стороны принят по сети (сервер — от клиента, клиент — от сервера).</summary>
        public const string Received = "received";
        /// <summary>Шаг <c>failed</c>: клиент сообщил о провале (data — причина).</summary>
        public const string Failed = "failed";
        /// <summary>Сервер прервал прогон (abort; data — причина).</summary>
        public const string Aborted = "aborted";
        /// <summary>Барьер пройден (data — кто дошёл / data сервера).</summary>
        public const string BarrierPassed = "barrier-passed";
        /// <summary>Барьер провален: таймаут, провал участника или ошибка протокола (data — диагноз).</summary>
        public const string BarrierFailed = "barrier-failed";
    }

    /// <summary>
    /// Лимиты и роли барьера (условия network 3 и 4). Чистые функции без Mirror — проверяются харнессом.
    /// Роли: <c>server</c> / <c>host</c> (индекс 0) и <c>client-1..client-N</c>, N — <c>-e2eClients</c>;
    /// поздний клиент — один из них, названный в <c>-e2eLate</c>.
    /// </summary>
    public static class E2EStepRules
    {
        /// <summary>Предел длины имени шага и роли, символов.</summary>
        public const int MaxNameLength = 64;
        /// <summary>Предел длины data шага, символов.</summary>
        public const int MaxDataLength = 256;
        /// <summary>Предел различных шагов на одно соединение (и на один процесс).</summary>
        public const int MaxStepsPerConnection = 512;
        /// <summary>Предел сообщений шагов на одно соединение: страховка от цикла, шлющего шаг каждый кадр.</summary>
        public const int MaxMessagesPerConnection = 4096;

        public const string RoleServer = "server";
        public const string RoleHost = "host";
        public const string ClientRolePrefix = "client-";
        /// <summary>Шаг, которым клиент сообщает серверу о своём провале (data — причина).</summary>
        public const string FailedStep = "failed";

        /// <summary>Сторона сервера: выделенный сервер или хост (условие network 7: host — отдельная роль).</summary>
        public static bool IsServerSideRole(string role) => role == RoleServer || role == RoleHost;

        /// <summary>0 — server/host, N — <c>client-N</c> (N ≥ 1, без ведущих нулей), -1 — роль вне схемы.</summary>
        public static int ClientIndexOf(string role)
        {
            if (IsServerSideRole(role)) return 0;
            if (role == null || !role.StartsWith(ClientRolePrefix, StringComparison.Ordinal)) return -1;
            int start = ClientRolePrefix.Length;
            int length = role.Length - start;
            if (length < 1 || length > 4 || role[start] == '0') return -1;
            int index = 0;
            for (int i = start; i < role.Length; i++)
            {
                char ch = role[i];
                if (ch < '0' || ch > '9') return -1;
                index = index * 10 + (ch - '0');
            }
            return index;
        }

        /// <summary>Ошибка роли клиента или null — роль из набора <c>client-1..client-clientCount</c>.</summary>
        public static string ValidateClientRole(string role, int clientCount)
        {
            if (string.IsNullOrEmpty(role)) return "пустая роль клиента";
            if (role.Length > MaxNameLength) return $"роль длиннее {MaxNameLength} символов: '{Clip(role)}'";
            int index = ClientIndexOf(role);
            if (index < 1 || index > clientCount)
                return clientCount < 1
                    ? $"роль '{role}': прогон без клиентов (-e2eClients 0)"
                    : $"роль '{role}' не из ожидаемого набора client-1..client-{clientCount}";
            return null;
        }

        /// <summary>Ошибка шага или null — имя непустое и в пределах, data в пределах.</summary>
        public static string ValidateStep(string name, string data)
        {
            if (string.IsNullOrEmpty(name)) return "пустое имя шага";
            if (name.Length > MaxNameLength) return $"имя шага длиннее {MaxNameLength} символов: '{Clip(name)}'";
            if (data != null && data.Length > MaxDataLength)
                return $"data шага '{name}' длиннее {MaxDataLength} символов ({data.Length})";
            return null;
        }

        /// <summary>Роли, которых ждёт барьер сервера: client-1..N, поздний — только при <paramref name="includeLate"/>.</summary>
        public static List<string> ExpectedClientRoles(int clientCount, string lateRole, bool includeLate)
        {
            var roles = new List<string>(Math.Max(0, clientCount));
            for (int i = 1; i <= clientCount; i++)
            {
                string role = ClientRolePrefix + i;
                if (!includeLate && role == lateRole) continue;
                roles.Add(role);
            }
            return roles;
        }

        /// <summary>Обрезка для диагностики и data: не больше <paramref name="max"/> символов.</summary>
        public static string Clip(string text, int max = 80)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
            return text.Substring(0, Math.Max(0, max - 1)) + "…";
        }
    }

    /// <summary>Состояние одного шага: первое достижение и последнее data.</summary>
    public sealed class E2EStepState
    {
        public string Name = string.Empty;
        public string Data = string.Empty;
        public DateTime FirstUtc;
        public DateTime LastUtc;
    }

    /// <summary>Итог приёма шага клиента сервером.</summary>
    public enum E2EStepAccept
    {
        /// <summary>Новый шаг или новое data — записан.</summary>
        Accepted,
        /// <summary>Повтор с тем же data — сервер дедуплицирует.</summary>
        Duplicate,
        /// <summary>Отброшен: лимит, неверная роль или ошибка протокола (в журнал Unity — именованная ошибка).</summary>
        Rejected,
        /// <summary>Роль занята другим живым соединением; шаг не принят, клиент перешлёт его после повторного e2e-ready.</summary>
        Conflict
    }

    /// <summary>
    /// Чистая логика барьера без Mirror: журнал своих шагов, очередь отправки до рукопожатия, таблица шагов по ролям
    /// на сервере, проверки лимитов и ролей. Сетевая обвязка — <c>E2EBarrier</c>. Экземпляр <see cref="Shared"/> —
    /// статика каркаса: переживает смену сцены и рестарт сети. Все вызовы — с главного потока.
    /// <para>Обработчики сообщений пишут только сюда (условие network 5): ни игровых команд, ни SyncVar, ни событий UXR.</para>
    /// </summary>
    public sealed class E2EBarrierCore
    {
        /// <summary>Состояние процесса; одно на процесс, как и <c>E2ERunner</c>.</summary>
        public static E2EBarrierCore Shared { get; } = new E2EBarrierCore();

        /// <summary>
        /// Сколько роль может «висеть» на двух соединениях: прежнее соединение при переподключении отваливается по
        /// таймауту транспорта. Дольше — именованная ошибка запуска.
        /// </summary>
        public const float RoleConflictGraceSeconds = 15f;

        private const int MaxProtocolErrors = 32;

        private sealed class Connection
        {
            public string Role;
            public int Messages;
            public readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal);
            public float ConflictSince = -1f;
            public string ConflictRole;
            public bool ConflictReported;
            public bool LimitReported;
        }

        // Свои шаги (клиент — для отправки, сервер — только журнал).
        private readonly Dictionary<string, E2EStepState> _own = new Dictionary<string, E2EStepState>(StringComparer.Ordinal);
        private readonly List<E2EStepState> _ownOrder = new List<E2EStepState>();
        private readonly List<E2EStepState> _pending = new List<E2EStepState>();
        private bool _ownLimitReported;
        private string _ownRoleError;
        private bool _ownRoleErrorReported;

        // Клиент: шаги сервера текущего запуска.
        private readonly Dictionary<string, E2EStepState> _fromServer = new Dictionary<string, E2EStepState>(StringComparer.Ordinal);
        private string _lastServerRun;
        private bool _fromServerLimitReported;
        private bool _serverLimitReported;

        // Сервер: шаги по ролям, свои шаги для рассылки, соединения.
        private readonly Dictionary<string, Dictionary<string, E2EStepState>> _byRole =
            new Dictionary<string, Dictionary<string, E2EStepState>>(StringComparer.Ordinal);
        private readonly Dictionary<string, E2EStepState> _serverSteps = new Dictionary<string, E2EStepState>(StringComparer.Ordinal);
        private readonly List<E2EStepState> _serverOrder = new List<E2EStepState>();
        private readonly List<E2EStepState> _broadcast = new List<E2EStepState>();
        private readonly Dictionary<int, Connection> _connections = new Dictionary<int, Connection>();
        private readonly Dictionary<string, int> _roleOwner = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> _errors = new List<string>();

        /// <summary>Каркас активирован текущим сценарием (<see cref="Configure"/>).</summary>
        public bool Configured { get; private set; }
        public E2EContext Context { get; private set; }
        public E2EResult Journal { get; private set; }
        public string Role => Context != null ? Context.Role : null;
        public bool IsServerSide => Context != null && E2EStepRules.IsServerSideRole(Context.Role);

        /// <summary>Сервер: id текущего запуска сценария; уходит клиентам в e2e-ready.</summary>
        public string ServerRun { get; private set; }

        /// <summary>Клиент: id запуска сервера из последнего e2e-ready этого соединения; null — рукопожатия нет.</summary>
        public string HandshakeRun { get; private set; }
        public bool HasHandshake => HandshakeRun != null;

        /// <summary>Прогон прерван сервером: у сервера — своя причина, у клиента — принятая.</summary>
        public string AbortReason { get; private set; }

        /// <summary>Ошибки запуска на сервере: роль вне набора, дубль роли, смена роли соединением.</summary>
        public IReadOnlyList<string> ProtocolErrors => _errors;

        /// <summary>Ошибка своей роли клиента (вне схемы client-N) или null.</summary>
        public string OwnRoleError => _ownRoleError;

        /// <summary>Свои шаги в порядке первого достижения.</summary>
        public IReadOnlyList<E2EStepState> OwnSteps => _ownOrder;

        /// <summary>Сервер: свои шаги для рассылки в порядке первого достижения.</summary>
        public IReadOnlyList<E2EStepState> ServerSteps => _serverOrder;

        /// <summary>Сетевая обвязка: отправить сразу (иначе — на следующем опросе). Только главный поток.</summary>
        internal Action FlushHook { get; set; }

        // ── Жизненный цикл ─────────────────────────────────────────────────

        /// <summary>
        /// Активирует барьер для сценария. Свои шаги обнуляются. Клиент сохраняет рукопожатие и шаги сервера, принятые
        /// до старта своего сценария (в редакторе сервер стартует сценарий раньше клиентов). Сервер начинает новый запуск:
        /// новый <see cref="ServerRun"/>, пустые таблицы.
        /// </summary>
        public void Configure(E2EContext context, E2EResult journal)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Journal = journal;
            Configured = true;
            _own.Clear();
            _ownOrder.Clear();
            _pending.Clear();
            _ownLimitReported = false;
            _ownRoleErrorReported = false;
            _ownRoleError = IsServerSide || E2EStepRules.ClientIndexOf(context.Role) >= 1
                ? null
                : $"своя роль '{context.Role}' вне схемы client-N: шаги не уходят серверу";

            if (IsServerSide)
            {
                _byRole.Clear();
                _serverSteps.Clear();
                _serverOrder.Clear();
                _serverLimitReported = false;
                _broadcast.Clear();
                _connections.Clear();
                _roleOwner.Clear();
                _errors.Clear();
                AbortReason = null;
                ServerRun = Guid.NewGuid().ToString("N").Substring(0, 12);
            }
        }

        /// <summary>Сценарий завершён и исполнитель уничтожен: шаги больше не принимаются от его контекста.</summary>
        public void Deconfigure()
        {
            Configured = false;
            Context = null;
            Journal = null;
        }

        /// <summary>Полный сброс (новый прогон стенда в редакторе).</summary>
        public void ResetAll()
        {
            Deconfigure();
            _own.Clear();
            _ownOrder.Clear();
            _pending.Clear();
            _fromServer.Clear();
            _lastServerRun = null;
            HandshakeRun = null;
            AbortReason = null;
            ServerRun = null;
            _byRole.Clear();
            _serverSteps.Clear();
            _serverOrder.Clear();
            _broadcast.Clear();
            _connections.Clear();
            _roleOwner.Clear();
            _errors.Clear();
        }

        // ── Свои шаги ──────────────────────────────────────────────────────

        /// <summary>
        /// Шаг процесса (<c>ctx.Step</c>). Клиент: журнал и очередь отправки (уйдёт после e2e-ready). Сервер/хост: только
        /// журнал. Повтор с тем же data — без эффекта; с другим data — обновление. false — шаг отброшен (именованная
        /// ошибка в журнал Unity).
        /// </summary>
        public bool Reach(E2EContext context, string name, string data, DateTime utc)
        {
            RequireContext(context);
            data = data ?? string.Empty;
            string error = E2EStepRules.ValidateStep(name, data);
            if (error != null)
            {
                GameLog.Debug.Error($"[E2E] Барьер: шаг отброшен ({Role}): {error}");
                return false;
            }

            if (!Record(_own, _ownOrder, name, data, utc, ref _ownLimitReported, "своих шагов", out bool isNew, out bool changed))
                return false;
            if (!changed) return true;

            JournalAdd(name, Role, data, utc, name == E2EStepRules.FailedStep ? E2EStepOutcome.Failed
                : isNew ? E2EStepOutcome.Reached : E2EStepOutcome.Updated);

            if (IsServerSide) return true;
            if (_ownRoleError != null)
            {
                if (!_ownRoleErrorReported)
                {
                    _ownRoleErrorReported = true;
                    GameLog.Debug.Error($"[E2E] Барьер: ошибка запуска: {_ownRoleError}");
                }
                return true;
            }

            if (!_pending.Contains(_own[name])) _pending.Add(_own[name]);
            FlushHook?.Invoke();
            return true;
        }

        /// <summary>Шаг сервера (<c>ctx.ServerStep</c>): журнал и рассылка клиентам. Только сервер/хост.</summary>
        public bool ReachServer(E2EContext context, string name, string data, DateTime utc)
        {
            RequireContext(context);
            if (!IsServerSide)
                throw new InvalidOperationException($"ServerStep('{name}') — только у сервера/хоста; роль '{Role}' шлёт ctx.Step.");
            data = data ?? string.Empty;
            string error = E2EStepRules.ValidateStep(name, data);
            if (error != null)
            {
                GameLog.Debug.Error($"[E2E] Барьер: шаг сервера отброшен: {error}");
                return false;
            }

            if (!Record(_serverSteps, _serverOrder, name, data, utc, ref _serverLimitReported, "шагов сервера", out bool isNew, out bool changed))
                return false;
            if (!changed) return true;

            JournalAdd(name, Role, data, utc, isNew ? E2EStepOutcome.Reached : E2EStepOutcome.Updated);
            E2EStepState state = _serverSteps[name];
            if (!_broadcast.Contains(state)) _broadcast.Add(state);
            FlushHook?.Invoke();
            return true;
        }

        /// <summary>
        /// Прервать прогон (провал сервера). Первая причина остаётся; рассылку делает сетевая обвязка, новые
        /// рукопожатия получают abort сразу после e2e-ready.
        /// </summary>
        public void Abort(string reason, DateTime utc)
        {
            if (!IsServerSide || AbortReason != null) return;
            AbortReason = E2EStepRules.Clip(string.IsNullOrEmpty(reason) ? "(причина не указана)" : reason, E2EStepRules.MaxDataLength);
            JournalAdd("abort", Role, AbortReason, utc, E2EStepOutcome.Aborted);
            GameLog.Debug.Warning($"[E2E] Барьер: сервер прерывает прогон: {AbortReason}");
            FlushHook?.Invoke();
        }

        // ── Клиент: рукопожатие и приём ────────────────────────────────────

        /// <summary>
        /// e2e-ready от сервера: обработчики сервера стоят. Новый запуск сервера сбрасывает его прежние шаги и abort.
        /// Все свои достигнутые шаги ставятся в очередь заново — сервер дедуплицирует.
        /// </summary>
        public void AcceptReady(string serverRun)
        {
            if (string.IsNullOrEmpty(serverRun))
            {
                GameLog.Debug.Error("[E2E] Барьер: e2e-ready без id запуска сервера — отброшен");
                return;
            }

            if (_lastServerRun != serverRun)
            {
                _fromServer.Clear();
                _fromServerLimitReported = false;
                AbortReason = null;
                _lastServerRun = serverRun;
            }

            HandshakeRun = serverRun;
            _pending.Clear();
            _pending.AddRange(_ownOrder);
            GameLog.Debug.Info($"[E2E] Барьер: e2e-ready от запуска сервера {serverRun}; к пересылке шагов: {_pending.Count}");
        }

        /// <summary>Соединение потеряно или клиент между сценами: шаги снова копятся до следующего e2e-ready.</summary>
        public void HandshakeLost()
        {
            if (HandshakeRun == null) return;
            GameLog.Debug.Info($"[E2E] Барьер: рукопожатие с запуском {HandshakeRun} потеряно — шаги копятся до нового e2e-ready");
            HandshakeRun = null;
        }

        /// <summary>Шаг сервера принят клиентом. false — отброшен по лимиту.</summary>
        public bool AcceptServerStep(string name, string data, DateTime utc)
        {
            data = data ?? string.Empty;
            string error = E2EStepRules.ValidateStep(name, data);
            if (error != null)
            {
                GameLog.Debug.Error($"[E2E] Барьер: шаг сервера отброшен клиентом: {error}");
                return false;
            }

            if (!Record(_fromServer, null, name, data, utc, ref _fromServerLimitReported, "шагов сервера", out bool isNew, out bool changed))
                return false;
            if (changed)
                JournalAdd(name, E2EStepRules.RoleServer, data, utc, isNew ? E2EStepOutcome.Received : E2EStepOutcome.Updated);
            return true;
        }

        /// <summary>Abort от сервера: клиент завершается именованным провалом (это делает исполнитель).</summary>
        public void AcceptAbort(string reason, DateTime utc)
        {
            if (AbortReason != null) return;
            AbortReason = E2EStepRules.Clip(string.IsNullOrEmpty(reason) ? "(причина не указана)" : reason, E2EStepRules.MaxDataLength);
            JournalAdd("abort", E2EStepRules.RoleServer, AbortReason, utc, E2EStepOutcome.Aborted);
            GameLog.Debug.Warning($"[E2E] Барьер: сервер прервал прогон: {AbortReason}");
        }

        /// <summary>
        /// Клиент: шаги к отправке (после рукопожатия). Переносит очередь в <paramref name="into"/> и очищает её.
        /// </summary>
        public bool TakeOutgoing(List<E2EStepState> into)
        {
            if (!Configured || IsServerSide || _ownRoleError != null || HandshakeRun == null || _pending.Count == 0)
                return false;
            into.AddRange(_pending);
            _pending.Clear();
            return true;
        }

        /// <summary>Клиент: сколько своих шагов ждут отправки.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>Клиент: шаг сервера достигнут; <paramref name="data"/> — последнее data.</summary>
        public bool TryServerStep(string step, out string data)
        {
            if (step != null && _fromServer.TryGetValue(step, out E2EStepState state))
            {
                data = state.Data;
                return true;
            }
            data = null;
            return false;
        }

        // ── Сервер: приём шагов клиентов ───────────────────────────────────

        /// <summary>
        /// Шаг клиента от соединения <paramref name="connectionId"/>. Роль — из ожидаемого набора; одна роль — одно
        /// живое соединение; соединение не меняет роль; лимиты длины и количества. <paramref name="now"/> — часы
        /// прогона, секунды (для отсрочки конфликта ролей при переподключении).
        /// </summary>
        public E2EStepAccept AcceptClientStep(int connectionId, string role, string name, string data, DateTime utc, float now)
        {
            if (!Configured || !IsServerSide)
            {
                GameLog.Debug.Warning($"[E2E] Барьер: шаг '{E2EStepRules.Clip(name)}' от соединения {connectionId} без активного сервера e2e — отброшен");
                return E2EStepAccept.Rejected;
            }

            if (!_connections.TryGetValue(connectionId, out Connection connection))
            {
                connection = new Connection();
                _connections[connectionId] = connection;
            }

            connection.Messages++;
            if (connection.Messages > E2EStepRules.MaxMessagesPerConnection)
            {
                if (connection.Messages == E2EStepRules.MaxMessagesPerConnection + 1)
                    GameLog.Debug.Error($"[E2E] Барьер: соединение {connectionId} ({connection.Role ?? role}) превысило " +
                                        $"{E2EStepRules.MaxMessagesPerConnection} сообщений шагов — дальше отбрасываются");
                return E2EStepAccept.Rejected;
            }

            string roleError = E2EStepRules.ValidateClientRole(role, Context.ClientCount);
            if (roleError != null)
            {
                ProtocolError($"соединение {connectionId}: {roleError}");
                return E2EStepAccept.Rejected;
            }

            if (connection.Role != null && connection.Role != role)
            {
                ProtocolError($"соединение {connectionId} сменило роль {connection.Role} → {role}");
                return E2EStepAccept.Rejected;
            }

            if (_roleOwner.TryGetValue(role, out int owner) && owner != connectionId)
            {
                if (connection.ConflictSince < 0f)
                {
                    connection.ConflictSince = now;
                    connection.ConflictRole = role;
                    GameLog.Debug.Warning($"[E2E] Барьер: роль {role} уже у живого соединения {owner}, соединение {connectionId} " +
                                          $"ждёт его отключения (до {RoleConflictGraceSeconds:F0} с)");
                }
                return E2EStepAccept.Conflict;
            }

            connection.Role = role;
            connection.ConflictSince = -1f;
            connection.ConflictRole = null;
            _roleOwner[role] = connectionId;

            data = data ?? string.Empty;
            string stepError = E2EStepRules.ValidateStep(name, data);
            if (stepError != null)
            {
                GameLog.Debug.Error($"[E2E] Барьер: шаг {role} отброшен: {stepError}");
                return E2EStepAccept.Rejected;
            }

            if (!connection.Names.Contains(name))
            {
                if (connection.Names.Count >= E2EStepRules.MaxStepsPerConnection)
                {
                    if (!connection.LimitReported)
                    {
                        connection.LimitReported = true;
                        GameLog.Debug.Error($"[E2E] Барьер: {role} (соединение {connectionId}) превысил " +
                                            $"{E2EStepRules.MaxStepsPerConnection} шагов — шаг '{name}' и следующие отброшены");
                    }
                    return E2EStepAccept.Rejected;
                }
                connection.Names.Add(name);
            }

            if (!_byRole.TryGetValue(role, out Dictionary<string, E2EStepState> steps))
            {
                steps = new Dictionary<string, E2EStepState>(StringComparer.Ordinal);
                _byRole[role] = steps;
            }

            bool limit = false;
            Record(steps, null, name, data, utc, ref limit, "шагов роли", out bool isNew, out bool changed);
            if (!changed) return E2EStepAccept.Duplicate;

            bool failed = name == E2EStepRules.FailedStep;
            JournalAdd(name, role, data, utc, failed ? E2EStepOutcome.Failed : isNew ? E2EStepOutcome.Received : E2EStepOutcome.Updated);
            if (failed) GameLog.Debug.Warning($"[E2E] Барьер: {role} сообщил о провале: {data}");
            return E2EStepAccept.Accepted;
        }

        /// <summary>
        /// Соединение отключилось: роль освобождается. В <paramref name="rehandshake"/> — соединения, ждавшие эту роль:
        /// обвязка шлёт им e2e-ready повторно, и они пересылают свои шаги.
        /// </summary>
        public void ConnectionGone(int connectionId, List<int> rehandshake)
        {
            if (!_connections.TryGetValue(connectionId, out Connection connection)) return;
            _connections.Remove(connectionId);
            if (connection.Role == null || !_roleOwner.TryGetValue(connection.Role, out int owner) || owner != connectionId) return;
            _roleOwner.Remove(connection.Role);
            foreach (KeyValuePair<int, Connection> pair in _connections)
                if (pair.Value.ConflictRole == connection.Role && rehandshake != null && !rehandshake.Contains(pair.Key))
                    rehandshake.Add(pair.Key);
        }

        /// <summary>Сеть сервера остановлена: соединений нет, роли свободны. Шаги ролей сохраняются.</summary>
        public void ServerNetworkStopped()
        {
            _connections.Clear();
            _roleOwner.Clear();
        }

        /// <summary>Отсрочка конфликта роли истекла — именованная ошибка запуска (один раз на соединение).</summary>
        public void Tick(float now)
        {
            foreach (KeyValuePair<int, Connection> pair in _connections)
            {
                Connection connection = pair.Value;
                if (connection.ConflictSince < 0f || connection.ConflictReported) continue;
                if (now - connection.ConflictSince < RoleConflictGraceSeconds) continue;
                connection.ConflictReported = true;
                _roleOwner.TryGetValue(connection.ConflictRole, out int owner);
                ProtocolError($"роль {connection.ConflictRole}: два живых соединения ({owner} и {pair.Key}) дольше " +
                              $"{RoleConflictGraceSeconds:F0} с");
            }
        }

        /// <summary>Сервер: новые и обновлённые свои шаги к рассылке; очередь очищается.</summary>
        public void TakeServerBroadcast(List<E2EStepState> into)
        {
            into.AddRange(_broadcast);
            _broadcast.Clear();
        }

        /// <summary>
        /// Барьер сервера: все роли <paramref name="expected"/> дошли до <paramref name="step"/>. Не аллоцирует, пока
        /// ждёт. <paramref name="failure"/> — повод провалить барьер сразу: ошибка протокола или шаг <c>failed</c>
        /// ожидаемого клиента.
        /// </summary>
        public bool ClientsReached(string step, List<string> expected, out string failure)
        {
            failure = null;
            if (_errors.Count > 0)
            {
                failure = "ошибка запуска e2e: " + _errors[0] + (_errors.Count > 1 ? $" (и ещё {_errors.Count - 1})" : string.Empty);
                return false;
            }

            bool all = true;
            for (int i = 0; i < expected.Count; i++)
            {
                _byRole.TryGetValue(expected[i], out Dictionary<string, E2EStepState> steps);
                if (step != E2EStepRules.FailedStep && steps != null && steps.TryGetValue(E2EStepRules.FailedStep, out E2EStepState failed))
                {
                    failure = $"{expected[i]} провалился: {failed.Data}";
                    return false;
                }
                if (steps == null || !steps.ContainsKey(step)) all = false;
            }
            return all;
        }

        /// <summary>Роль → последнее data шага для дошедших ролей из <paramref name="expected"/>.</summary>
        public Dictionary<string, string> ClientData(string step, List<string> expected)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string role in expected)
                if (_byRole.TryGetValue(role, out Dictionary<string, E2EStepState> steps) && steps.TryGetValue(step, out E2EStepState state))
                    result[role] = state.Data;
            return result;
        }

        /// <summary>
        /// Диагноз барьера: «дошли …; нет …». У дошедшей роли — data этого шага, у недошедшей — её последний шаг и
        /// его data: видно, кто на какой карте/раунде (пожелание gameplay 6403).
        /// </summary>
        public string DescribeClients(string step, List<string> expected, string lateRole)
        {
            var reached = new List<string>();
            var missing = new List<string>();
            foreach (string role in expected)
            {
                _byRole.TryGetValue(role, out Dictionary<string, E2EStepState> steps);
                E2EStepState state = null;
                bool done = steps != null && steps.TryGetValue(step, out state);
                string label = role == lateRole ? role + " (поздний)" : role;
                if (done)
                {
                    if (!string.IsNullOrEmpty(state.Data)) label += " [" + state.Data + "]";
                }
                else
                {
                    E2EStepState last = null;
                    if (steps != null)
                        foreach (E2EStepState s in steps.Values)
                            if (last == null || s.LastUtc > last.LastUtc) last = s;
                    if (last != null)
                        label += " (последний шаг " + last.Name + (string.IsNullOrEmpty(last.Data) ? string.Empty : " [" + last.Data + "]") + ")";
                    if (!_roleOwner.ContainsKey(role)) label += " — не на связи с барьером";
                }
                (done ? reached : missing).Add(label);
            }
            return $"дошли {(reached.Count > 0 ? string.Join(", ", reached) : "никто")}; " +
                   $"нет {(missing.Count > 0 ? string.Join(", ", missing) : "—")}";
        }

        /// <summary>Сервер: шаг роли достигнут.</summary>
        public bool TryClientStep(string role, string step, out string data)
        {
            if (role != null && step != null && _byRole.TryGetValue(role, out Dictionary<string, E2EStepState> steps) &&
                steps.TryGetValue(step, out E2EStepState state))
            {
                data = state.Data;
                return true;
            }
            data = null;
            return false;
        }

        /// <summary>Запись барьера (пройден/провален) в журнал своего процесса; чужой контекст — без эффекта.</summary>
        public void Note(E2EContext context, string name, string data, string outcome)
        {
            if (context == null || !ReferenceEquals(context, Context)) return;
            JournalAdd(name, Role, E2EStepRules.Clip(data ?? string.Empty, E2EStepRules.MaxDataLength), DateTime.UtcNow, outcome);
        }

        // ── Внутреннее ─────────────────────────────────────────────────────

        private void RequireContext(E2EContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!Configured || !ReferenceEquals(context, Context))
                throw new InvalidOperationException("Шаг e2e вне активного сценария: контекст не запущен через E2ERunner.");
        }

        /// <summary>
        /// Записывает шаг в таблицу: новый (с лимитом), обновление data или повтор. false — отброшен по лимиту.
        /// </summary>
        private static bool Record(Dictionary<string, E2EStepState> table, List<E2EStepState> order, string name, string data,
            DateTime utc, ref bool limitReported, string what, out bool isNew, out bool changed)
        {
            if (table.TryGetValue(name, out E2EStepState state))
            {
                isNew = false;
                changed = state.Data != data;
                if (changed)
                {
                    state.Data = data;
                    state.LastUtc = utc;
                }
                return true;
            }

            isNew = false;
            changed = false;
            if (table.Count >= E2EStepRules.MaxStepsPerConnection)
            {
                if (!limitReported)
                {
                    limitReported = true;
                    GameLog.Debug.Error($"[E2E] Барьер: больше {E2EStepRules.MaxStepsPerConnection} {what} — шаг '{name}' и следующие отброшены");
                }
                return false;
            }

            state = new E2EStepState { Name = name, Data = data, FirstUtc = utc, LastUtc = utc };
            table.Add(name, state);
            order?.Add(state);
            isNew = true;
            changed = true;
            return true;
        }

        private void ProtocolError(string text)
        {
            if (_errors.Count < MaxProtocolErrors) _errors.Add(text);
            GameLog.Debug.Error($"[E2E] Барьер: ошибка запуска: {text}");
        }

        private void JournalAdd(string name, string role, string data, DateTime utc, string outcome)
        {
            Journal?.AddStep(name, role, data, utc, outcome);
        }
    }

    /// <summary>
    /// Именованный провал сценария (барьер, abort): <see cref="E2ERunner"/> ставит красную проверку
    /// <see cref="Check"/> с <see cref="Detail"/> и завершает прогон со статусом completed (вердикт RED, а не «ошибка»).
    /// </summary>
    public sealed class E2EScenarioFailure : Exception
    {
        public string Check { get; }
        public string Detail { get; }

        public E2EScenarioFailure(string check, string detail) : base(check + ": " + detail)
        {
            Check = check;
            Detail = detail;
        }
    }

#if !VRBG_E2E_CORE_ONLY
    // ── Сообщения Mirror только для e2e (условие network 2: под одним #if на всех ролях) ─────────────

    /// <summary>Клиент → сервер: шаг достигнут. Шлётся только после e2e-ready текущего запуска сервера.</summary>
    public struct E2EStepMessage : NetworkMessage
    {
        public string role;
        public string name;
        public string data;
    }

    /// <summary>Сервер → клиент: шаг сервера.</summary>
    public struct E2EServerStepMessage : NetworkMessage
    {
        public string name;
        public string data;
    }

    /// <summary>Сервер → клиент: прогон прерван (провал сервера).</summary>
    public struct E2EAbortMessage : NetworkMessage
    {
        public string reason;
    }

    /// <summary>Сервер → клиент: обработчики e2e сервера стоят; id — текущий запуск сценария сервера.</summary>
    public struct E2EReadyMessage : NetworkMessage
    {
        public string serverRun;
    }

    /// <summary>
    /// Клиент → сервер: «у меня стоят обработчики e2e». Шлётся после штатной аутентификации и готовности соединения;
    /// сервер отвечает e2e-ready только соединениям, приславшим hello (предложение network 6425 (б)): процесс без e2e
    /// ничего не получает и не отключается из-за неизвестного id сообщения.
    /// </summary>
    public struct E2EHelloMessage : NetworkMessage
    {
        public string role;
    }

    /// <summary>
    /// Барьер шагов мультипроцессного сценария: сетевая обвязка <see cref="E2EBarrierCore"/> и ожидания для сценариев.
    ///
    /// <para><b>Рантайм-страж.</b> Обработчики регистрируются только в процессе с e2e-сценарием (<see cref="Activate"/>
    /// из <see cref="E2ERunner"/>) или в прогоне стенда редактора (<see cref="Arm"/>); иначе <see cref="Poll"/> ничего
    /// не делает и клиент ничего не шлёт. Регистрирует каркас сам, опросом <c>NetworkServer.active</c> /
    /// <c>NetworkClient.active</c> на главном потоке, — заново после каждого рестарта сети (Mirror чистит обработчики).
    /// Игровой код и GameNetworkManager не меняются.</para>
    ///
    /// <para><b>Рукопожатие (условие network 1, вариант (б) из 6425).</b> Клиент с обработчиками e2e после
    /// аутентификации и готовности шлёт <see cref="E2EHelloMessage"/> (обработчик сервера стоит с фронта
    /// <c>NetworkServer.active</c>). Сервер шлёт <see cref="E2EReadyMessage"/> только соединению, приславшему hello,
    /// когда оно аутентифицировано и готово (<c>isReady</c>). Только после e2e-ready клиент шлёт шаги; до него они
    /// копятся. Сцена сменилась (NotReady) или соединение потеряно — клиент снова ждёт e2e-ready и пересылает все свои
    /// шаги; сервер дедуплицирует. Процесс без e2e hello не шлёт и ничего e2e не получает — неизвестный id сообщения
    /// его не отключит.</para>
    /// </summary>
    public static class E2EBarrier
    {
        private sealed class Tracked
        {
            public NetworkConnectionToClient Connection;
            public bool Hello;
            public bool Ready;
            public bool Handshaken;
            public bool NeedsReady;
            public bool AbortSent;
        }

        private static readonly Dictionary<int, Tracked> TrackedConnections = new Dictionary<int, Tracked>();
        private static readonly List<int> Gone = new List<int>();
        private static readonly List<int> Rehandshake = new List<int>();
        private static readonly List<E2EStepState> Outgoing = new List<E2EStepState>();
        private static readonly List<E2EStepState> Broadcast = new List<E2EStepState>();

        private static bool _armed;
        private static string _armedRun;
        private static bool _serverWasActive;
        private static bool _clientWasActive;
        private static bool _clientWasConnected;
        private static bool _helloSent;
        private static bool _polling;

        private static E2EBarrierCore Core => E2EBarrierCore.Shared;

        /// <summary>Часы барьера — те же, что у ожиданий: не зависят от timeScale и пауз загрузки.</summary>
        private static float Now => E2EWait.Now;

        // ── Жизненный цикл ─────────────────────────────────────────────────

        /// <summary>Исполнитель сценария активирует барьер для своего контекста (до старта сети).</summary>
        public static void Activate(E2EContext context, E2EResult journal)
        {
            Core.Configure(context, journal);
            Core.FlushHook = Poll;
            // Новый запуск сервера: соединениям, уже прошедшим рукопожатие, — заново e2e-ready с новым id.
            foreach (KeyValuePair<int, Tracked> pair in TrackedConnections) pair.Value.NeedsReady = true;
            GameLog.Debug.Info($"[E2E] Барьер активирован: роль {context.Role}, клиентов {context.ClientCount}" +
                               (context.LateRole != null ? $", поздний {context.LateRole}" : string.Empty) +
                               (Core.ServerRun != null && context.IsServerSide ? $", запуск {Core.ServerRun}" : string.Empty));
        }

        /// <summary>Исполнитель уничтожен: шаги его контекста больше не принимаются.</summary>
        public static void Deactivate(E2EContext context)
        {
            if (!ReferenceEquals(Core.Context, context)) return;
            Core.Deconfigure();
            Core.FlushHook = null;
        }

        /// <summary>
        /// Прогон стенда в редакторе: обработчики ставятся у всех участников заранее, до команды run-e2e. Иначе сервер,
        /// начавший сценарий первым, прислал бы e2e-ready клиенту без обработчика и разорвал бы ему соединение.
        /// Новый <paramref name="runId"/> сбрасывает состояние барьера.
        /// </summary>
        public static void Arm(string runId)
        {
            if (_armedRun != runId)
            {
                Core.ResetAll();
                // Hello живого соединения не стираем: клиент на том же соединении его не повторит (network 6437).
                ResetHandshakes();
                _armedRun = runId;
            }
            _armed = true;
        }

        /// <summary>Прогон стенда завершён. Обработчики Mirror снимет остановка сети.</summary>
        public static void Disarm()
        {
            _armed = false;
            _armedRun = null;
        }

        private static bool Enabled => _armed || Core.Configured;

        /// <summary>Новый прогон: рукопожатие заново со всеми соединениями, отметка hello сохраняется.</summary>
        private static void ResetHandshakes()
        {
            foreach (KeyValuePair<int, Tracked> pair in TrackedConnections)
            {
                Tracked tracked = pair.Value;
                tracked.Ready = false;
                tracked.Handshaken = false;
                tracked.NeedsReady = true;
                tracked.AbortSent = false;
            }
        }

        /// <summary>
        /// Опрос сети на главном потоке: регистрация обработчиков после старта/рестарта сети, e2e-ready новым и снова
        /// готовым соединениям, отправка накопленных шагов, рассылка шагов сервера и abort. Зовут исполнитель (каждый
        /// кадр), участник стенда в редакторе и сам барьер сразу после нового шага.
        /// </summary>
        public static void Poll()
        {
            if (_polling) return;
            if (!Enabled)
            {
                // Вне e2e края сети не отслеживаем: при следующей активации регистрация начнётся заново.
                _serverWasActive = _clientWasActive = _clientWasConnected = _helloSent = false;
                // Hello живых соединений сохраняем (network 6437); мёртвые снимет опрос сервера по connections.
                ResetHandshakes();
                return;
            }

            _polling = true;
            try
            {
                PollServer();
                PollClient();
            }
            catch (Exception e)
            {
                GameLog.Debug.Error($"[E2E] Барьер: сбой опроса сети: {e}");
            }
            finally
            {
                _polling = false;
            }
        }

        private static void PollServer()
        {
            if (!NetworkServer.active)
            {
                if (_serverWasActive)
                {
                    TrackedConnections.Clear();
                    Core.ServerNetworkStopped();
                }
                _serverWasActive = false;
                return;
            }

            if (!_serverWasActive)
            {
                RegisterServerHandlers();
                // Не Clear: фронт бывает и при повторном включении барьера на живом сервере — hello живых соединений
                // сохраняется (network 6437). Соединения прежнего сервера — другие объекты, их снимет проверка ниже.
                ResetHandshakes();
                _serverWasActive = true;
            }

            // Только вооружён (редактор, сценарий сервера ещё не начат): обработчики стоят, e2e-ready не шлём.
            if (!Core.Configured || !Core.IsServerSide) return;

            Gone.Clear();
            foreach (KeyValuePair<int, Tracked> pair in TrackedConnections)
            {
                if (!NetworkServer.connections.TryGetValue(pair.Key, out NetworkConnectionToClient live) ||
                    !ReferenceEquals(live, pair.Value.Connection))
                    Gone.Add(pair.Key);
            }

            Rehandshake.Clear();
            for (int i = 0; i < Gone.Count; i++)
            {
                TrackedConnections.Remove(Gone[i]);
                Core.ConnectionGone(Gone[i], Rehandshake);
            }

            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
            {
                NetworkConnectionToClient connection = pair.Value;
                if (connection == null || connection is LocalConnectionToClient) continue;
                if (!TrackedConnections.TryGetValue(pair.Key, out Tracked tracked))
                {
                    tracked = new Tracked { Connection = connection };
                    TrackedConnections[pair.Key] = tracked;
                }

                // e2e-ready — только соединению, приславшему hello: у него обработчики e2e уже стоят.
                bool ready = connection.isAuthenticated && connection.isReady && tracked.Hello;
                if (!ready)
                {
                    tracked.Ready = false;
                    tracked.Handshaken = false;
                    continue;
                }

                if (!tracked.Ready || tracked.NeedsReady || Rehandshake.Contains(pair.Key))
                    SendReady(connection, tracked);
                tracked.Ready = true;
            }

            Broadcast.Clear();
            Core.TakeServerBroadcast(Broadcast);
            string abort = Core.AbortReason;
            if (Broadcast.Count > 0 || abort != null)
            {
                foreach (KeyValuePair<int, Tracked> pair in TrackedConnections)
                {
                    Tracked tracked = pair.Value;
                    if (!tracked.Handshaken) continue;
                    for (int i = 0; i < Broadcast.Count; i++)
                        tracked.Connection.Send(new E2EServerStepMessage { name = Broadcast[i].Name, data = Broadcast[i].Data });
                    if (abort != null && !tracked.AbortSent)
                    {
                        tracked.Connection.Send(new E2EAbortMessage { reason = abort });
                        tracked.AbortSent = true;
                    }
                }
            }

            Core.Tick(Now);
        }

        /// <summary>e2e-ready, затем все шаги сервера этого запуска и abort, если он был.</summary>
        private static void SendReady(NetworkConnectionToClient connection, Tracked tracked)
        {
            // Повтор регистрации дёшев и редок: закрывает рестарт сервера внутри одного кадра.
            RegisterServerHandlers();
            connection.Send(new E2EReadyMessage { serverRun = Core.ServerRun });
            IReadOnlyList<E2EStepState> steps = Core.ServerSteps;
            for (int i = 0; i < steps.Count; i++)
                connection.Send(new E2EServerStepMessage { name = steps[i].Name, data = steps[i].Data });
            tracked.AbortSent = false;
            if (Core.AbortReason != null)
            {
                connection.Send(new E2EAbortMessage { reason = Core.AbortReason });
                tracked.AbortSent = true;
            }
            tracked.Handshaken = true;
            tracked.NeedsReady = false;
            GameLog.Debug.Info($"[E2E] Барьер: e2e-ready → соединение {connection.connectionId} (запуск {Core.ServerRun}, " +
                               $"шагов сервера {steps.Count})");
        }

        private static void PollClient()
        {
            // Хост и выделенный сервер: своему локальному клиенту e2e-сообщения не шлются.
            if (NetworkServer.active)
            {
                _clientWasActive = _clientWasConnected = false;
                return;
            }

            bool active = NetworkClient.active;
            bool connected = NetworkClient.isConnected;
            if (active && (!_clientWasActive || connected != _clientWasConnected))
                RegisterClientHandlers();
            _clientWasActive = active;
            _clientWasConnected = connected;

            if (!connected || !NetworkClient.ready)
            {
                Core.HandshakeLost();
                // Новое соединение — новый hello; NotReady на смене сцены соединение сохраняет, сервер hello помнит.
                if (!connected) _helloSent = false;
                return;
            }

            // Ready у клиента — после аутентификации: обработчик сервера (requireAuthentication) hello примет.
            if (!_helloSent)
            {
                NetworkClient.Send(new E2EHelloMessage { role = Core.Role ?? string.Empty });
                _helloSent = true;
                GameLog.Debug.Info($"[E2E] Барьер: e2e-hello → сервер (роль {Core.Role})");
            }

            Outgoing.Clear();
            if (!Core.TakeOutgoing(Outgoing)) return;
            string role = Core.Role;
            for (int i = 0; i < Outgoing.Count; i++)
                NetworkClient.Send(new E2EStepMessage { role = role, name = Outgoing[i].Name, data = Outgoing[i].Data });
        }

        // requireAuthentication = true (по умолчанию): шаги принимаются только после штатной аутентификации (условие network 4).
        private static void RegisterServerHandlers()
        {
            NetworkServer.ReplaceHandler<E2EStepMessage>(HandleStep);
            NetworkServer.ReplaceHandler<E2EHelloMessage>(HandleHello);
        }

        // Только отметка соединения: e2e-ready отправит опрос сервера, когда соединение готово и сценарий сервера идёт.
        private static void HandleHello(NetworkConnectionToClient connection, E2EHelloMessage message)
        {
            // Тот же id у соединения нового сервера — новая запись: прежняя относится к другому объекту соединения.
            if (!TrackedConnections.TryGetValue(connection.connectionId, out Tracked tracked) ||
                !ReferenceEquals(tracked.Connection, connection))
            {
                tracked = new Tracked { Connection = connection };
                TrackedConnections[connection.connectionId] = tracked;
            }
            tracked.Hello = true;
            GameLog.Debug.Info($"[E2E] Барьер: e2e-hello ← соединение {connection.connectionId} (запуск {Core.ServerRun})");
        }

        private static void RegisterClientHandlers()
        {
            NetworkClient.ReplaceHandler<E2EReadyMessage>(HandleReady);
            NetworkClient.ReplaceHandler<E2EServerStepMessage>(HandleServerStep);
            NetworkClient.ReplaceHandler<E2EAbortMessage>(HandleAbort);
        }

        // Обработчики только пишут журнал шагов (условие network 5).
        private static void HandleStep(NetworkConnectionToClient connection, E2EStepMessage message) =>
            Core.AcceptClientStep(connection.connectionId, message.role, message.name, message.data, DateTime.UtcNow, Now);

        private static void HandleReady(E2EReadyMessage message) => Core.AcceptReady(message.serverRun);

        private static void HandleServerStep(E2EServerStepMessage message) =>
            Core.AcceptServerStep(message.name, message.data, DateTime.UtcNow);

        private static void HandleAbort(E2EAbortMessage message) => Core.AcceptAbort(message.reason, DateTime.UtcNow);

        // ── Провалы ────────────────────────────────────────────────────────

        /// <summary>Провал сервера: abort всем клиентам сразу (и тем, кто подключится позже).</summary>
        public static void FailServer(string reason)
        {
            if (!Core.Configured || !Core.IsServerSide) return;
            Core.Abort(reason, DateTime.UtcNow);
        }

        /// <summary>Провал клиента: шаг <c>failed</c> с причиной — сервер видит его в своём барьере.</summary>
        public static void ReportClientFailure(E2EContext context, string reason)
        {
            if (context == null || !Core.Configured || !ReferenceEquals(Core.Context, context) || Core.IsServerSide) return;
            Core.Reach(context, E2EStepRules.FailedStep, E2EStepRules.Clip(reason ?? string.Empty, E2EStepRules.MaxDataLength), DateTime.UtcNow);
        }

        // ── Ожидания для сценариев ─────────────────────────────────────────

        /// <summary>
        /// Сервер/хост ждёт, пока все ожидаемые клиенты (client-1..N; поздний — только при <paramref name="includeLate"/>)
        /// дойдут до шага <paramref name="step"/>. <paramref name="onReached"/> получает роль → data. Таймаут, шаг
        /// <c>failed</c> ожидаемого клиента или ошибка запуска — именованный провал «barrier &lt;шаг&gt;: …», abort клиентам.
        /// </summary>
        public static IEnumerator WaitClients(E2EContext ctx, string step, float timeoutSeconds, bool includeLate = false,
            Action<IReadOnlyDictionary<string, string>> onReached = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (!ctx.IsServerSide) throw new InvalidOperationException($"WaitClients('{step}') — только у сервера/хоста, роль '{ctx.Role}'.");
            string error = E2EStepRules.ValidateStep(step, null);
            if (error != null) throw new ArgumentException("WaitClients: " + error, nameof(step));

            List<string> expected = E2EStepRules.ExpectedClientRoles(ctx.ClientCount, ctx.LateRole, includeLate);
            string check = "barrier " + step;
            float started = Now;
            float deadline = started + timeoutSeconds;
            using (E2EWait.Track($"барьер {step}: клиенты {string.Join(", ", expected)}", timeoutSeconds))
            {
                while (true)
                {
                    Poll();
                    if (Core.ClientsReached(step, expected, out string failure))
                    {
                        Dictionary<string, string> data = Core.ClientData(step, expected);
                        Core.Note(ctx, step, string.Join(",", expected), E2EStepOutcome.BarrierPassed);
                        GameLog.Debug.Info($"[E2E] Барьер {step}: дошли все ({expected.Count}) за {Now - started:F1} с");
                        onReached?.Invoke(data);
                        yield break;
                    }

                    if (failure != null || Now >= deadline)
                    {
                        string detail = failure != null
                            ? $"{failure}; {Core.DescribeClients(step, expected, ctx.LateRole)}"
                            : $"{Core.DescribeClients(step, expected, ctx.LateRole)} (ждали {timeoutSeconds:F0} с)";
                        Fail(ctx, check, detail);
                    }

                    yield return null;
                }
            }
        }

        /// <summary>
        /// Клиент ждёт шаг сервера <paramref name="step"/>; <paramref name="onReached"/> получает его data. Таймаут или
        /// abort сервера — именованный провал «barrier &lt;шаг&gt;: …».
        /// </summary>
        public static IEnumerator WaitServer(E2EContext ctx, string step, float timeoutSeconds, Action<string> onReached = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.IsServerSide) throw new InvalidOperationException($"WaitServer('{step}') — только у клиента, роль '{ctx.Role}'.");
            string error = E2EStepRules.ValidateStep(step, null);
            if (error != null) throw new ArgumentException("WaitServer: " + error, nameof(step));

            string check = "barrier " + step;
            float started = Now;
            float deadline = started + timeoutSeconds;
            using (E2EWait.Track($"барьер {step}: шаг сервера", timeoutSeconds))
            {
                while (true)
                {
                    Poll();
                    if (Core.TryServerStep(step, out string data))
                    {
                        Core.Note(ctx, step, data, E2EStepOutcome.BarrierPassed);
                        GameLog.Debug.Info($"[E2E] Барьер {step}: шаг сервера пришёл за {Now - started:F1} с");
                        onReached?.Invoke(data);
                        yield break;
                    }

                    if (Core.AbortReason != null)
                        Fail(ctx, check, "сервер прервал прогон: " + Core.AbortReason);
                    if (Core.OwnRoleError != null)
                        Fail(ctx, check, "ошибка запуска: " + Core.OwnRoleError);
                    if (Now >= deadline)
                        Fail(ctx, check, $"шаг сервера не пришёл за {timeoutSeconds:F0} с; " +
                                         $"рукопожатие: {(Core.HasHandshake ? "есть, запуск " + Core.HandshakeRun : "нет")}, " +
                                         $"подключение: {(NetworkClient.isConnected ? "есть" : "нет")}, " +
                                         $"неотправленных шагов: {Core.PendingCount}");

                    yield return null;
                }
            }
        }

        private static void Fail(E2EContext ctx, string check, string detail)
        {
            Core.Note(ctx, check.Substring("barrier ".Length), E2EStepRules.Clip(detail, E2EStepRules.MaxDataLength), E2EStepOutcome.BarrierFailed);
            GameLog.Debug.Warning($"[E2E] {check}: {detail}");
            if (ctx.IsServerSide) FailServer($"{check}: {detail}");
            throw new E2EScenarioFailure(check, detail);
        }
    }
#endif
}
#endif
