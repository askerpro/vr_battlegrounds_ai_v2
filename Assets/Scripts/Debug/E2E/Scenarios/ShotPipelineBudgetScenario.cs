// Ярус C (два процесса) — сценарий shot-pipeline-budget: замеры для решений T-23 и T-24.
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Mirror;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>shot-pipeline-budget</c> — <b>измерительный</b>, а не сторожевой.
    ///
    /// <para>
    /// <b>Зачем.</b> Решения [T-23] (компенсация задержки) и [T-24] (где симулировать
    /// пули) требуют чисел, а не рассуждений. Часть чисел добывается только в шлеме
    /// (кадровое время на Quest, задержка по Wi-Fi) — их снимает человек. Но два
    /// вопроса решаются здесь, на трёх процессах одной машины, и стоят они один прогон:
    /// </para>
    ///
    /// <list type="number">
    /// <item><b>Доезжает ли выстрел до чужой машины.</b> Вся посылка T-24 —
    ///       «каждый клиент спавнит свою копию снаряда и трассирует её» — держится
    ///       на том, что <c>UxrProjectileSource.Shoot</c> уезжает каналом состояния
    ///       и переигрывается на всех. До выравнивания <c>UniqueId</c> (NET-16)
    ///       не переигрывалось вообще ничего, поэтому утверждение надо проверить,
    ///       а не унаследовать. Сервер стреляет из оружия арсенала, клиент считает
    ///       снаряды у себя. Ноль снарядов у клиента опровергает посылку T-24.</item>
    /// <item><b>Сколько снарядов живёт одновременно.</b> Снаряд удаляется только
    ///       при попадании или по исчерпании <c>ProjectileMaxDistance</c>. Отношение
    ///       дальности к скорости даёт время жизни, а оно вместе с темпом стрельбы —
    ///       установившееся число снарядов на машину. Это и есть цена вопроса T-24,
    ///       выраженная в величине, не зависящей от платформы: трассировок в кадре.</item>
    /// </list>
    ///
    /// <para>
    /// Попутно снимаются сетевые задержки (T-23): <c>NetworkTime.rtt</c> и буфер
    /// интерполяции <c>NetworkClient.bufferTime</c>. По петле loopback это <b>нижняя
    /// граница</b> — то, что остаётся, когда сети фактически нет. Всё, что человек
    /// намеряет в шлеме, ляжет сверху.
    /// </para>
    ///
    /// <para>
    /// <b>Что этот сценарий не проверяет.</b> Он не выносит суждения «хорошо/плохо»:
    /// красной проверка становится только тогда, когда замер невозможен (нет оружия,
    /// нет клиента, выстрел не состоялся). Пороги живут в документации решения,
    /// а не здесь: измерительный сценарий, который сам себе назначил порог,
    /// краснеет на смене железа и его перестают читать.
    /// </para>
    /// </summary>
    public class ShotPipelineBudgetScenario : IE2EScenario
    {
        public string Name => "shot-pipeline-budget";

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients   = "клиент подключился и сервер создал сессию";
        private const string CheckMap       = "карта загружена, у сессии есть живой аватар";
        private const string CheckWeapon    = "на карте нашлось оружие с UxrProjectileSource";
        private const string CheckShotConf  = "замер: настройки выстрела (скорость, дальность, маска, время жизни)";
        private const string CheckServerRtt = "замер: RTT подключений на сервере";
        private const string CheckBurst     = "очередь произведена, сервер посчитал свои снаряды";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected   = "клиент подключился к серверу";
        private const string CheckClientSession     = "сервер создал сессию для клиента";
        private const string CheckClientMap         = "клиент переехал на карту вместе с сервером";
        private const string CheckClientWeapon      = "клиент видит то же оружие, что и сервер";
        private const string CheckClientTiming      = "замер: RTT и буфер интерполяции на клиенте";
        private const string CheckClientProjectiles = "выстрел сервера доехал: у клиента появились свои снаряды";

        // ── Параметры замера ──────────────────────────────────────────────

        /// <summary>Сколько секунд копить выборку по задержкам.</summary>
        private const float TimingSampleSeconds = 6f;

        /// <summary>Шаг выборки по задержкам.</summary>
        private const float TimingSampleStep = 0.05f;

        /// <summary>Сколько выстрелов в очереди.</summary>
        private const int BurstShots = 8;

        /// <summary>
        /// Пауза между выстрелами. 0.1 с — это <c>_maxShotFrequency = 10</c>
        /// с префаба M16, то есть настоящий темп автоматического огня, а не выдуманный.
        /// </summary>
        private const float BurstInterval = 0.1f;

        /// <summary>Сколько сервер ждёт отчёта «клиент готов считать снаряды».</summary>
        private const float ArmedWait = 120f;

        /// <summary>Сколько клиент наблюдает за появлением снарядов после того, как поднял флаг.</summary>
        private const float WatchWindow = 25f;

        /// <summary>Сколько сервер ждёт, пока клиент запишет вердикт.</summary>
        private const float ClientVerdictWait = 60f;

        // ── Наблюдение ────────────────────────────────────────────────────

        /// <summary>Имя инстанса снаряда, по которому он опознаётся в корне сцены.</summary>
        private string _projectileInstanceName = string.Empty;

        /// <summary>Наибольшее число одновременно живых снарядов, увиденное на этой машине.</summary>
        private int _peakProjectiles;

        /// <summary>Сумма замеров и их разброс — заполняется обеими ролями.</summary>
        private readonly Stat _rtt        = new Stat();
        private readonly Stat _bufferTime = new Stat();
        private readonly Stat _bufferMult = new Stat();


        public IEnumerator Run(E2EContext context, E2EResult result)
        {
            if (context.IsServerRole)
                return RunServer(context, result);

            return RunClient(context, result);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль сервера
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckDedicated, CheckClients, CheckMap, CheckWeapon,
                           CheckShotConf, CheckServerRtt, CheckBurst);

            float deadline = Now + 60f;
            while (!NetworkServer.active && Now < deadline)
                yield return null;

            bool dedicated = NetworkServer.active && !NetworkClient.active;
            result.Set(CheckDedicated, dedicated,
                dedicated
                    ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                    : $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}. " +
                      "На хосте замер бессмыслен: сервер и клиент делят одни объекты в памяти, " +
                      "и «доехал ли выстрел» проверить нечем.");

            if (!dedicated)
            {
                result.Summary = "конфигурация не выделенный сервер, замер недействителен";
                yield break;
            }

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");

            deadline = Now + 90f;
            while (SessionCount() < context.ExpectedClients && Now < deadline)
                yield return null;

            int sessions = SessionCount();
            bool clientsOk = sessions >= context.ExpectedClients;
            result.Set(CheckClients, clientsOk,
                clientsOk
                    ? $"сессий на сервере: {sessions} (ждали {context.ExpectedClients})"
                    : $"за 90 с подключилось сессий: {sessions} из {context.ExpectedClients}");

            if (!clientsOk)
            {
                result.Summary = "клиент не подключился, стрелять некому и считать некому";
                yield break;
            }

            SessionManager sessionManager = SessionManager.Instance;
            if (sessionManager == null || MapLoader.Instance == null)
            {
                result.Set(CheckMap, false,
                    $"SessionManager.Instance={(sessionManager == null ? "null" : "есть")}, " +
                    $"MapLoader.Instance={(MapLoader.Instance == null ? "null" : "есть")}");
                result.Summary = "менеджеры не поднялись, замер недействителен";
                yield break;
            }

            // Команды назначаются не ради матча, а ради AvatarManager.ChangeAvatar:
            // без известного TeamRegistry индекса он молча выходит и аватар не спавнится.
            sessionManager.SetSession(context.Map, "elimination");
            yield return null;
            GameLog.Debug.Info($"[E2E] Команды распределены: {AssignTeams(sessionManager)}");

            MapLoader.Instance.LoadMap(context.Map);

            deadline = Now + 120f;
            while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                yield return null;

            PlayerSession withAvatar = null;
            deadline = Now + 90f;
            while (Now < deadline)
            {
                withAvatar = FirstSessionWithAvatar();
                if (withAvatar != null) break;

                yield return null;
            }

            bool mapOk = SceneManager.GetActiveScene().name == context.Map && withAvatar != null;
            result.Set(CheckMap, mapOk,
                mapOk
                    ? $"сцена='{SceneManager.GetActiveScene().name}', аватар есть: netId={withAvatar.ActiveAvatar.netId}"
                    : $"сцена='{SceneManager.GetActiveScene().name}', сессии с живым аватаром нет за 90 с");

            if (!mapOk)
            {
                result.Summary = "карта или аватар не поднялись, замер недействителен";
                yield break;
            }

            // ── Оружие ────────────────────────────────────────────────────
            // Стены арсенала раскладывают оружие не мгновенно: ждём появления
            // хотя бы одного заспавненного предмета с источником снарядов.
            NetworkIdentity item = null;
            UxrProjectileSource source = null;

            deadline = Now + 60f;
            while (Now < deadline)
            {
                item = PickWeaponWithProjectileSource();
                source = item != null ? PickUsableSource(item) : null;
                if (source != null) break;

                yield return null;
            }

            bool weaponOk = source != null && source.ShotTypes.Count > 0;
            result.Set(CheckWeapon, weaponOk,
                weaponOk
                    ? $"оружие '{item.name}' netId={item.netId}; {DescribeSources(item)}"
                    : $"за 60 с на карте не нашлось заспавненного предмета с пригодным UxrProjectileSource. " +
                      $"Заспавненных предметов: {CountSpawnedItems()}. " +
                      "Пригодным считается источник с заполненным ShotSource — только он умеет стрелять.");

            if (!weaponOk)
            {
                result.Summary = "стрелять не из чего, замер T-24 невозможен";
                yield break;
            }

            UxrShotDescriptor shot = source.ShotTypes[0];
            _projectileInstanceName = InstanceName(shot);

            // ── Замер: настройки выстрела ─────────────────────────────────
            // Это не проверка, а снятие показаний с префаба на живой сцене:
            // ровно те числа, из которых считается нагрузка на кадр.
            float life = shot.ProjectileSpeed > 0f ? shot.ProjectileMaxDistance / shot.ProjectileSpeed : -1f;
            float steady = life > 0f ? life / BurstInterval : -1f;
            int mask = shot.CollisionLayerMask.value;

            result.Set(CheckShotConf, true,
                $"скорость={shot.ProjectileSpeed:F0} м/с, дальность={shot.ProjectileMaxDistance:F0} м, " +
                $"время жизни={life:F2} с, авто-траектория={shot.UseAutomaticProjectileTrajectory}, " +
                $"CollisionLayerMask=0x{mask:X} ({(mask == 0 ? "НИЧЕГО: Physics.Raycast с нулевой маской не попадает никуда" : "есть слои")}), " +
                $"CreateDecalLayerMask=0x{shot.CreateDecalLayerMask.value:X}, " +
                $"урон {shot.ProjectileDamageNear:F0}..{shot.ProjectileDamageFar:F0}. " +
                $"При темпе {1f / BurstInterval:F0} выстр/с установившееся число живых снарядов на одного стрелка " +
                $"= время жизни / период = {steady:F0}; каждый из них — один Physics.Raycast в кадре на каждой машине.");

            // ── Замер: RTT подключений ────────────────────────────────────
            yield return SampleServerTiming();

            result.Set(CheckServerRtt, _rtt.Count > 0,
                _rtt.Count > 0
                    ? $"RTT подключений (петля loopback, {_rtt.Count} выборок за {TimingSampleSeconds:F0} с): {_rtt.Describe("мс", 1000.0)}. " +
                      "Это нижняя граница: в шлеме по Wi-Fi сюда добавится реальная сеть."
                    : "выборка пуста: подключений на сервере не оказалось");

            // ── Отмашка клиента ───────────────────────────────────────────
            E2EWaitOutcome armed = new E2EWaitOutcome();
            yield return E2EWait.Until(armed,
                "клиент отчитался, что готов считать снаряды",
                ArmedWait,
                () => ReportedFlags() >= context.ExpectedClients,
                () => $"отчитались {ReportedFlags()} из {context.ExpectedClients}",
                () => NetworkServer.connections.Count > 0
                    ? null
                    : "на сервере не осталось подключений — считать снаряды уже некому");

            if (!armed.Succeeded)
            {
                result.Set(CheckBurst, false, armed.Diagnosis);
                result.Summary = "клиент не подтвердил готовность, стрелять рано";
                yield break;
            }

            // ── Очередь ───────────────────────────────────────────────────
            int before = CountProjectiles(_projectileInstanceName);
            int fired = 0;
            string shootError = null;

            Transform muzzle = shot.ShotSource;

            for (int i = 0; i < BurstShots; i++)
            {
                try
                {
                    // Явные позиция и ориентация, а не перегрузка Shoot(int):
                    // так выстрел не зависит от того, куда смотрит лежащее на стене
                    // оружие, и одинаково воспроизводится от прогона к прогону.
                    source.Shoot(0, muzzle.position, muzzle.rotation);
                    fired++;
                }
                catch (Exception e)
                {
                    shootError = $"{e.GetType().Name}: {e.Message}";
                    break;
                }

                _peakProjectiles = Mathf.Max(_peakProjectiles, CountProjectiles(_projectileInstanceName));
                yield return E2EWait.Hold(BurstInterval);
            }

            // Дать снарядам прожить пару кадров и посчитать пик ещё раз.
            for (int i = 0; i < 30; i++)
            {
                _peakProjectiles = Mathf.Max(_peakProjectiles, CountProjectiles(_projectileInstanceName));
                yield return null;
            }

            bool burstOk = fired == BurstShots && _peakProjectiles > 0;
            result.Set(CheckBurst, burstOk,
                burstOk
                    ? $"выстрелов: {fired}, снарядов в корне сцены до очереди {before}, пик после {_peakProjectiles} " +
                      $"(имя инстанса '{_projectileInstanceName}'). " +
                      $"Сервер симулирует их сам: UxrWeaponManager.UpdateProjectiles не смотрит на роль. " +
                      $"WeaponSystemEnabled={(UxrWeaponManager.HasInstance ? UxrWeaponManager.Instance.WeaponSystemEnabled.ToString() : "менеджера нет")}."
                    : shootError != null
                        ? $"выстрел упал на {fired}-м: {shootError}"
                        : $"выстрелов: {fired} из {BurstShots}, снарядов в корне сцены: {_peakProjectiles}. " +
                          $"Ноль снарядов при состоявшихся выстрелах значит, что снаряд рождается не корнем сцены " +
                          $"или зовётся иначе, чем '{_projectileInstanceName}' — счётчик надо чинить, а не выводы делать.");

            yield return WaitForClientVerdicts(context);

            result.Summary = result.AllChecksGreen
                ? $"замер снят: {fired} выстрелов, пик снарядов на сервере {_peakProjectiles}, " +
                  $"RTT {_rtt.Describe("мс", 1000.0)}. Ответ по доставке — в client-1.json"
                : "есть красные проверки, замер неполон — см. detail";
        }

        /// <summary>
        /// Держит серверный процесс живым, пока клиент не запишет вердикт: обрыв связи
        /// посреди клиентского наблюдения выглядит как «снаряды не приехали».
        /// </summary>
        private static IEnumerator WaitForClientVerdicts(E2EContext context)
        {
            E2EWaitOutcome barrier = new E2EWaitOutcome();

            yield return E2EWait.Until(barrier,
                $"все {context.ExpectedClients} клиент(а) отчитались, что записали вердикт",
                ClientVerdictWait,
                () => ReportedFlags() == 0 && SessionCount() >= context.ExpectedClients,
                () => $"держат флаг: {ReportedFlags()} из {SessionCount()} сессий; " +
                      $"подключений: {NetworkServer.connections.Count}");

            GameLog.Debug.Info($"[E2E] Барьер клиентских вердиктов: {barrier.Diagnosis}");
        }

        /// <summary>Копит RTT по всем подключениям сервера.</summary>
        private IEnumerator SampleServerTiming()
        {
            float until = Now + TimingSampleSeconds;

            while (Now < until)
            {
                foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
                {
                    if (conn != null)
                        _rtt.Add(conn.rtt);
                }

                yield return E2EWait.Hold(TimingSampleStep);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap,
                           CheckClientWeapon, CheckClientTiming, CheckClientProjectiles);

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");

            float deadline = Now + 30f;
            while (!NetworkClient.isConnected && Now < deadline)
                yield return null;

            if (!NetworkClient.isConnected && !string.IsNullOrEmpty(context.ServerAddress))
            {
                GameLog.Debug.Info($"[E2E] Discovery молчит 30 с, подключаюсь напрямую к {context.ServerAddress}");

                Mirror.Discovery.NetworkDiscovery discovery = UnityEngine.Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>();
                if (discovery != null)
                    discovery.StopDiscovery();

                if (NetworkManager.singleton != null && !NetworkClient.active)
                {
                    NetworkManager.singleton.networkAddress = context.ServerAddress;
                    NetworkManager.singleton.StartClient();
                }

                deadline = Now + 30f;
                while (!NetworkClient.isConnected && Now < deadline)
                    yield return null;
            }

            bool connected = NetworkClient.isConnected;
            result.Set(CheckClientConnected, connected,
                connected
                    ? $"подключён к {(NetworkManager.singleton != null ? NetworkManager.singleton.networkAddress : "?")}"
                    : $"за 60 с подключиться не удалось: ни Discovery, ни прямое подключение к '{context.ServerAddress}'");

            if (!connected)
            {
                result.Summary = "клиент не подключился";
                yield break;
            }

            deadline = Now + 60f;
            while (PlayerSession.LocalSession == null && Now < deadline)
                yield return null;

            PlayerSession local = PlayerSession.LocalSession;
            result.Set(CheckClientSession, local != null,
                local != null
                    ? $"PlayerSession.LocalSession netId={local.netId}"
                    : "за 60 с сервер не создал PlayerSession для этого клиента");

            if (local == null)
            {
                result.Summary = "сессии нет, отчитываться нечем";
                yield break;
            }

            deadline = Now + 120f;
            while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                yield return null;

            bool onMap = SceneManager.GetActiveScene().name == context.Map;
            result.Set(CheckClientMap, onMap,
                onMap
                    ? $"активная сцена='{SceneManager.GetActiveScene().name}'"
                    : $"за 120 с клиент не переехал на '{context.Map}', остался в '{SceneManager.GetActiveScene().name}'");

            if (!onMap)
            {
                result.Summary = "клиент не на карте, наблюдать нечего";
                yield break;
            }

            // ── То же оружие, что у сервера ───────────────────────────────
            // Обе стороны берут предмет с наименьшим netId: netId раздаёт сервер,
            // поэтому выбор одинаков на всех машинах без всякого согласования.
            NetworkIdentity item = null;
            UxrProjectileSource source = null;

            deadline = Now + 60f;
            while (Now < deadline)
            {
                item = PickWeaponWithProjectileSource();
                source = item != null ? PickUsableSource(item) : null;
                if (source != null) break;

                yield return null;
            }

            bool weaponOk = source != null && source.ShotTypes.Count > 0;
            if (weaponOk)
                _projectileInstanceName = InstanceName(source.ShotTypes[0]);

            result.Set(CheckClientWeapon, weaponOk,
                weaponOk
                    ? $"оружие '{item.name}' netId={item.netId}; {DescribeSources(item)}; " +
                      $"снаряд опознаётся как '{_projectileInstanceName}'. " +
                      "Сверь netId с серверным: разные netId значат, что стороны считают разное оружие."
                    : $"за 60 с у клиента не нашлось заспавненного предмета с пригодным UxrProjectileSource. " +
                      $"Заспавненных предметов: {CountSpawnedItems()}");

            if (!weaponOk)
            {
                result.Summary = "оружие не найдено, считать снаряды не по чему";
                ReportFlag(true);
                yield return E2EWait.Hold(1f);
                ReportFlag(false);
                yield break;
            }

            // ── Замер: RTT и буфер интерполяции ───────────────────────────
            yield return SampleClientTiming();

            result.Set(CheckClientTiming, _rtt.Count > 0,
                _rtt.Count > 0
                    ? $"RTT: {_rtt.Describe("мс", 1000.0)}; " +
                      $"буфер интерполяции NetworkClient.bufferTime: {_bufferTime.Describe("мс", 1000.0)} " +
                      $"(множитель {_bufferMult.Describe("x", 1.0)}, базовый из настроек " +
                      $"{NetworkClient.initialBufferTime * 1000.0:F1} мс, sendRate={NetworkClient.sendRate}). " +
                      "Буфер — это задержка отрисовки чужого объекта на этой машине, и она есть даже при нулевом RTT. " +
                      "Такой же буфер сервер держит для клиент-авторитетных трансформов, " +
                      "поэтому на пути «где стрелок видит цель» → «где сервер её трассирует» он считается дважды."
                    : "выборка пуста");

            // ── Наблюдение за снарядами ───────────────────────────────────
            // Флаг поднимается только теперь: сервер стреляет сразу после него,
            // и счётчик обязан быть готов раньше выстрела.
            ReportFlag(true);

            int atStart = CountProjectiles(_projectileInstanceName);
            E2EWaitOutcome seen = new E2EWaitOutcome();

            yield return E2EWait.Until(seen,
                "на клиентской машине появились снаряды от выстрела сервера",
                WatchWindow,
                () =>
                {
                    _peakProjectiles = Mathf.Max(_peakProjectiles, CountProjectiles(_projectileInstanceName));
                    return _peakProjectiles > atStart;
                },
                () => $"снарядов сейчас: {CountProjectiles(_projectileInstanceName)}, пик за окно: {_peakProjectiles}, " +
                      $"было на старте: {atStart}",
                () => NetworkClient.isConnected ? null : "связь с сервером пропала — выстрел сюда уже не приедет");

            // Дособрать пик: снаряды рождаются пачкой, а условие выше выходит на первом.
            float until = Now + (BurstShots * BurstInterval) + 1.5f;
            while (Now < until)
            {
                _peakProjectiles = Mathf.Max(_peakProjectiles, CountProjectiles(_projectileInstanceName));
                yield return null;
            }

            result.Set(CheckClientProjectiles, seen.Succeeded,
                seen.Succeeded
                    ? $"пик снарядов на клиенте: {_peakProjectiles} (на старте наблюдения {atStart}), " +
                      $"первый замечен через {seen.Elapsed:F2} с. " +
                      "Посылка T-24 подтверждена: выстрел уезжает каналом состояния, клиент спавнит свою копию снаряда, " +
                      "двигает её и трассирует Physics.Raycast в UxrWeaponManager.UpdateProjectiles — " +
                      "при том, что урон вычитает только сервер."
                    : seen.Diagnosis + " Ни одного снаряда: выстрел не переигрался на этой машине. " +
                      "Тогда посылка T-24 («каждый клиент считает трассировки») неверна, " +
                      "и экономить нечего — проверь доставку канала состояния (NET-16/NET-17).");

            result.Summary = seen.Succeeded
                ? $"выстрел доехал, пик снарядов на клиенте {_peakProjectiles}; RTT {_rtt.Describe("мс", 1000.0)}, " +
                  $"буфер {_bufferTime.Describe("мс", 1000.0)}"
                : "выстрел до клиента не доехал — см. detail";

            ReportFlag(false);
        }

        /// <summary>Копит RTT и буфер интерполяции на клиенте.</summary>
        private IEnumerator SampleClientTiming()
        {
            float until = Now + TimingSampleSeconds;

            while (Now < until)
            {
                _rtt.Add(NetworkTime.rtt);
                _bufferTime.Add(NetworkClient.bufferTime);
                _bufferMult.Add(NetworkClient.bufferTimeMultiplier);

                yield return E2EWait.Hold(TimingSampleStep);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        /// <summary>
        /// Имя, под которым инстанс снаряда лежит в корне сцены.
        /// <c>Instantiate</c> добавляет к имени префаба «(Clone)», а
        /// <c>ProjectileInfo</c> сразу же обнуляет родителя — значит снаряд всегда
        /// корневой объект активной сцены.
        /// </summary>
        private static string InstanceName(UxrShotDescriptor shot)
        {
            return shot != null && shot.ProjectilePrefab != null
                ? shot.ProjectilePrefab.name + "(Clone)"
                : string.Empty;
        }

        /// <summary>
        /// Считает живые снаряды по корню активной сцены. Без рефлексии в приватный
        /// <c>UxrWeaponManager._projectiles</c>: наблюдаемо ровно то же, а зависимость
        /// от закрытого поля SDK не заводится.
        /// </summary>
        private static int CountProjectiles(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName))
                return 0;

            int count = 0;
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name.StartsWith(instanceName, StringComparison.Ordinal))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Заспавненный в рантайме предмет с источником снарядов и наименьшим
        /// <c>netId</c>. Именно наименьший: <c>netId</c> раздаёт сервер, поэтому выбор
        /// одинаков на всех машинах и согласовывать его не нужно.
        /// </summary>
        private static NetworkIdentity PickWeaponWithProjectileSource()
        {
            Dictionary<uint, NetworkIdentity> spawned = NetworkServer.active
                ? NetworkServer.spawned
                : NetworkClient.spawned;

            NetworkIdentity best = null;

            foreach (NetworkIdentity identity in spawned.Values)
            {
                if (identity == null || identity.sceneId != 0) continue;
                if (PickUsableSource(identity) == null) continue;
                if (best == null || identity.netId < best.netId) best = identity;
            }

            return best;
        }

        /// <summary>
        /// Источник снарядов, которым действительно можно стрелять. На префабах M16
        /// и Gun_real таких компонентов <b>два</b> на одном объекте, и у второго
        /// не заполнены ни <c>ShotSource</c>, ни <c>Tip</c> — стрелять им нельзя.
        /// </summary>
        private static UxrProjectileSource PickUsableSource(NetworkIdentity item)
        {
            if (item == null)
                return null;

            UxrProjectileSource[] sources = item.GetComponentsInChildren<UxrProjectileSource>(true);

            foreach (UxrProjectileSource source in sources)
            {
                if (source == null || source.ShotTypes == null || source.ShotTypes.Count == 0) continue;

                UxrShotDescriptor shot = source.ShotTypes[0];
                if (shot == null || shot.ShotSource == null || shot.ProjectilePrefab == null) continue;

                // Дульная вспышка цепляется к Tip: без него Shoot() упадёт NRE.
                if (shot.PrefabInstantiateOnTipWhenShot != null && shot.Tip == null) continue;

                return source;
            }

            return null;
        }

        /// <summary>Состав источников снарядов на предмете — здесь всплывает дубль компонента.</summary>
        private static string DescribeSources(NetworkIdentity item)
        {
            if (item == null)
                return "предмета нет";

            UxrProjectileSource[] sources = item.GetComponentsInChildren<UxrProjectileSource>(true);
            StringBuilder builder = new StringBuilder();
            builder.Append("UxrProjectileSource на предмете: ").Append(sources.Length);

            for (int i = 0; i < sources.Length; i++)
            {
                UxrShotDescriptor shot = sources[i].ShotTypes != null && sources[i].ShotTypes.Count > 0
                    ? sources[i].ShotTypes[0]
                    : null;

                builder.Append("; #").Append(i).Append(' ')
                       .Append(shot == null
                           ? "без описаний выстрела"
                           : $"ShotSource={(shot.ShotSource == null ? "нет" : "есть")}, " +
                             $"скорость={shot.ProjectileSpeed:F0}, дальность={shot.ProjectileMaxDistance:F0}, " +
                             $"маска=0x{shot.CollisionLayerMask.value:X}");
            }

            return builder.ToString();
        }

        private static int CountSpawnedItems()
        {
            Dictionary<uint, NetworkIdentity> spawned = NetworkServer.active
                ? NetworkServer.spawned
                : NetworkClient.spawned;

            int count = 0;
            foreach (NetworkIdentity identity in spawned.Values)
            {
                if (identity != null && identity.sceneId == 0) count++;
            }

            return count;
        }

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

        private static PlayerSession FirstSessionWithAvatar()
        {
            if (PlayersManager.Instance == null)
                return null;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.ActiveAvatar != null && session.ActiveAvatar.IsAlive)
                    return session;
            }

            return null;
        }

        private static string AssignTeams(SessionManager sessionManager)
        {
            GameModeData mode = sessionManager.SelectedGameModeData;
            if (mode == null || mode.teams == null || mode.teams.Length == 0)
                return "режим не отдал список команд — команды не назначены";

            IReadOnlyList<PlayerSession> sessions = PlayersManager.Instance.Sessions;
            List<string> report = new List<string>();

            for (int i = 0; i < sessions.Count; i++)
            {
                TeamData team = mode.teams[i % mode.teams.Length];
                if (team == null) continue;

                sessions[i].TeamIndex = team.teamIndex;
                report.Add($"{sessions[i].PlayerName}->{team.displayName}({team.teamIndex})");
            }

            return string.Join(", ", report.ToArray());
        }

        private static int ReportedFlags()
        {
            if (PlayersManager.Instance == null)
                return 0;

            int reported = 0;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    reported++;
            }

            return reported;
        }

        /// <summary>
        /// Обратный канал клиент → сервер, тот же, что в <c>player-death-signal</c>:
        /// <c>HasGrabbedDogTag</c> — единственное поле сессии, которое клиент вправе
        /// менять командой. Матч здесь не запускается, игрового смысла у флага нет.
        /// </summary>
        private static void ReportFlag(bool raised)
        {
            PlayerSession local = PlayerSession.LocalSession;
            if (local == null)
            {
                GameLog.Debug.Info("[E2E] Отчитаться нечем: локальной сессии нет");
                return;
            }

            local.CmdSetDogTagGrabbed(raised);
        }


        /// <summary>
        /// Накопитель выборки. Отдельный класс, потому что в вердикт обязаны попасть
        /// min/среднее/max, а не одно мгновенное значение: одиночный отсчёт по сети
        /// ничего не значит.
        /// </summary>
        private sealed class Stat
        {
            public int Count { get; private set; }

            private double _min = double.MaxValue;
            private double _max = double.MinValue;
            private double _sum;

            public void Add(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                    return;

                Count++;
                _sum += value;
                if (value < _min) _min = value;
                if (value > _max) _max = value;
            }

            /// <summary>Строка вида «мин 0.4 / сред 0.9 / макс 2.1 мс».</summary>
            public string Describe(string unit, double scale)
            {
                if (Count == 0)
                    return "нет выборки";

                return $"мин {_min * scale:F1} / сред {_sum / Count * scale:F1} / макс {_max * scale:F1} {unit}";
            }
        }
    }
}
#endif
