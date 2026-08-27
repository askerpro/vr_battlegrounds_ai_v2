using Mirror;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Таймеры фаз раунда после перевода на <c>NetworkTime</c> (находка NET-09, задача T-19).
    ///
    /// Раньше остаток таймера был <c>SyncVar</c>-float'ом, которому <c>ServerTick</c>
    /// присваивал значение каждый кадр: объект помечался грязным до конца матча, в сеть
    /// шёл постоянный фоновый трафик, а таймер в HUD дёргался — значение приезжало
    /// порциями раз в <c>syncInterval</c>. Теперь синхронизируется момент старта фазы,
    /// а остаток каждый клиент считает локально.
    ///
    /// Почему ярус A. Запись <c>SyncVar</c> и <c>ServerTick</c> идут через <c>[Server]</c>:
    /// вне активного сервера Mirror их молча заглушает, и тест зеленел бы впустую.
    ///
    /// Чего этот тест не проверяет: плавность хода. <c>NetworkTime.localTime</c> — это
    /// <c>Time.unscaledTimeAsDouble</c>, а он внутри одного EditMode-теста не меняется
    /// вовсе. Поэтому ход времени задаётся сдвигом отметки старта фазы, а сама плавность
    /// на клиенте остаётся за ярусом C.
    /// </summary>
    public class RoundTimerNetworkTimeTests : MirrorTestHarness
    {
        /// <summary>Допуск сравнения секунд: сравниваем double-время, а отдаём float.</summary>
        private const float Tolerance = 0.05f;

        private EliminationMode _mode;
        private TeamData _teamA;
        private TeamData _teamB;
        private RoundFlowDriver _driver;
        private StubPlayerRoster _roster;

        [SetUp]
        public void PrepareMatch()
        {
            SilenceMirrorNoise();

            TeamRegistry registry = TeamRegistry.Instance;
            Assert.IsNotNull(registry, "TeamRegistry.Instance не загрузился из Resources");

            _teamA = registry.GetByIndex(1);
            _teamB = registry.GetByIndex(2);
            Assert.IsNotNull(_teamA, "В реестре нет команды с teamIndex=1");
            Assert.IsNotNull(_teamB, "В реестре нет команды с teamIndex=2");

            _mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(_mode);

            // Живых аватаров в EditMode не поднять, а без них фаза Equipment вечна.
            _roster = new StubPlayerRoster();
            _roster.Add(_teamA, CreateReadySession("PlayerA"));
            _roster.Add(_teamB, CreateReadySession("PlayerB"));
            _mode.PlayerRoster = _roster;

            // Готовность объявляется перед каждым тиком: с T-29 она живёт один раунд,
            // а эти тесты прогоняют их несколько подряд. Так заглушка играет роль
            // игроков, которые каждый раунд заново берут жетон.
            _driver = new RoundFlowDriver(
                dt =>
                {
                    _roster.DeclareAllReady();
                    _mode.ServerTick(dt);
                },
                () => _mode.CurrentRoundState);
        }

        /// <summary>
        /// Сессия игрока, который стоит в своей зоне спавна. Готовность здесь не
        /// объявляется: с T-29 она сбрасывается каждый раунд, поэтому её объявляет
        /// заново перед каждым тиком <c>StubPlayerRoster.DeclareAllReady</c>.
        /// </summary>
        private PlayerSession CreateReadySession(string name)
        {
            GameObject go = CreateNetworkObject(name);
            PlayerSession session = go.AddComponent<PlayerSession>();
            EnableNetworking(go);

            // Игрок стоит в зоне спавна СВОЕЙ команды: с RDY-04 признак выводится из того,
            // чья это зона, поэтому проставляем его тем же входом, что и TeamSpawnZone.
            session.ServerEnterSpawnZone(session.TeamIndex);
            return session;
        }

        private void StartMatch()
        {
            _mode.Initialize(new[] { _teamA, _teamB });
            InvokePrivateMethod(_mode, "InitializeActiveGame");

            Assert.IsNotNull(_mode.RoundManager,
                "InitializeActiveGame не создал RoundManager — значит [Server]-заглушка всё ещё срабатывает.");
        }

        /// <summary>
        /// Отматывает отметку старта фазы назад: как будто фаза идёт уже <paramref name="seconds"/> секунд.
        /// Реального времени в EditMode-тесте не течёт, поэтому ход времени задаётся так.
        /// </summary>
        private void PretendPhaseRunsFor(double seconds)
        {
            SetPrivateField(_mode, "_phaseStartTime", NetworkTime.time - seconds);
        }

        // ── Фоновый трафик (NET-09) ─────────────────────────────────────────

        [Test]
        public void Тик_внутри_фазы_не_помечает_объект_грязным()
        {
            SilenceMirrorNoise();
            StartMatch();

            _driver.AdvanceUntil(() => _mode.CurrentRoundState == RoundState.Combat, "фазы Combat");

            // Сброс после входа в бой: сама смена фазы обязана быть грязной — это
            // единственная отправка, ради которой всё и затевалось.
            _mode.ClearAllDirtyBits();

            // Тик посреди боя: фаза та же, счёт тот же, номер раунда тот же.
            // Отправлять нечего, значит и грязнить объект нечем.
            _mode.ServerTick(RoundFlowDriver.Step);

            Assert.IsFalse(_mode.IsDirty(),
                "Тик внутри фазы пометил объект грязным — значит SyncVar пишется каждый кадр (NET-09).\n" +
                "Так остаток таймера уезжает в сеть десять раз в секунду до конца матча,\n" +
                "а в HUD приходит ступеньками раз в syncInterval.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Смена_фазы_помечает_объект_грязным()
        {
            SilenceMirrorNoise();
            StartMatch();

            _driver.AdvanceUntil(() => _mode.CurrentRoundState == RoundState.Countdown, "фазы Countdown");
            _mode.ClearAllDirtyBits();

            _driver.AdvanceUntil(() => _mode.CurrentRoundState == RoundState.Combat, "фазы Combat");

            Assert.IsTrue(_mode.IsDirty(),
                "Смена фазы обязана уехать клиентам. Чистый объект здесь означает,\n" +
                "что предыдущий тест зелёный по недосмотру: не отправляется вообще ничего.");
        }

        // ── Сам подсчёт остатка ─────────────────────────────────────────────

        [Test]
        public void Остаток_отсчёта_считается_от_момента_старта_фазы()
        {
            SilenceMirrorNoise();
            StartMatch();

            _driver.AdvanceUntil(() => _mode.CurrentRoundState == RoundState.Countdown, "фазы Countdown");

            // Отсчёт идёт 3 секунды (_countdownDuration по умолчанию). Прошла одна.
            PretendPhaseRunsFor(1.0d);

            Assert.AreEqual(2.0f, _mode.CountdownTimeRemaining, Tolerance,
                "Остаток обратного отсчёта обязан считаться от момента старта фазы по NetworkTime.\n" +
                "Значение не сдвинулось — таймер по-прежнему берётся из SyncVar-поля, которое\n" +
                "меняет только серверный тик, и на клиенте между отправками стоит на месте.");
        }

        [Test]
        public void Остаток_боя_считается_от_момента_старта_фазы()
        {
            SilenceMirrorNoise();
            StartMatch();

            _driver.AdvanceUntil(() => _mode.CurrentRoundState == RoundState.Combat, "фазы Combat");

            // Раунд идёт 90 секунд (_roundDuration по умолчанию). Прошло десять.
            PretendPhaseRunsFor(10.0d);

            Assert.AreEqual(80.0f, _mode.RoundTimeRemaining, Tolerance,
                "Остаток времени раунда обязан считаться от момента старта фазы Combat.");
        }

        [Test]
        public void До_боя_показывается_полная_длительность_раунда()
        {
            SilenceMirrorNoise();
            StartMatch();

            _driver.AdvanceUntil(() => _mode.CurrentRoundState == RoundState.Countdown, "фазы Countdown");

            // Отсчёт идёт, но бой ещё не начался — боевое время не расходуется.
            PretendPhaseRunsFor(2.0d);

            Assert.AreEqual(90.0f, _mode.RoundTimeRemaining, Tolerance,
                "До начала боя HUD обязан показывать полную длительность раунда: " +
                "боевой таймер тикает только в фазе Combat, как и в RoundManager.");
        }

        [Test]
        public void После_боя_остаток_замирает_на_значении_конца_боя()
        {
            SilenceMirrorNoise();
            StartMatch();

            _driver.AdvanceUntil(() => _mode.CurrentRoundState == RoundState.Combat, "фазы Combat");

            // Бой шёл 30 секунд и на этом закончился.
            PretendPhaseRunsFor(30.0d);
            _mode.RoundManager.RequestRoundEnd(_teamA);
            _driver.AdvanceUntil(() => _mode.CurrentRoundState == RoundState.Resolution, "фазы Resolution");

            // Экран итогов живёт своей жизнью, но боевого времени больше не расходует.
            PretendPhaseRunsFor(2.0d);

            Assert.AreEqual(60.0f, _mode.RoundTimeRemaining, Tolerance,
                "После конца боя остаток обязан замереть на том, что оставалось в момент\n" +
                "последнего удара, — так вело себя старое поле, и HUD на экране итогов\n" +
                "показывает именно это.");
        }
    }
}
