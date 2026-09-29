using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Готовность игрока к раунду как явное состояние — задача T-29, дефект RDY-01.
    ///
    /// <para>
    /// <b>Что проверяется.</b> Раньше готовность выводилась из двух флагов
    /// (<c>IsInSpawnZone &amp;&amp; HasGrabbedDogTag</c>): отменить её было нечем,
    /// сбрасывать её никто не сбрасывал, а предела ожидания не существовало вовсе —
    /// один отошедший игрок останавливал матч навсегда. Здесь проверяется всё это:
    /// объявление, отмена, снятие при выходе из зоны, сброс на новый раунд и оба
    /// правила предела ожидания.
    /// </para>
    ///
    /// <para>
    /// <b>Почему ярус A.</b> Готовность — <c>SyncVar</c>, а <c>ServerTick</c> и
    /// <c>ServerBeginRound</c> помечены <c>[Server]</c>: вне активного сервера Mirror
    /// их молча заглушает, и тест зеленел бы, ничего не проверив.
    /// </para>
    /// </summary>
    public class RoundReadinessTests : MirrorTestHarness
    {
        private EliminationMode _mode;
        private TeamData _teamA;
        private TeamData _teamB;
        private PlayerSession _playerA;
        private PlayerSession _playerB;
        private StubPlayerRoster _roster;
        private RoundFlowDriver _driver;

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

            _playerA = CreateSession("PlayerA");
            _playerB = CreateSession("PlayerB");

            _roster = new StubPlayerRoster();
            _roster.Add(_teamA, _playerA);
            _roster.Add(_teamB, _playerB);
            _mode.PlayerRoster = _roster;

            // Здесь готовность объявляют руками — она и есть предмет проверки,
            // поэтому автоматического DeclareAllReady в тике нет.
            _driver = new RoundFlowDriver(dt => _mode.ServerTick(dt), () => _mode.CurrentRoundPhase);
        }

        /// <summary>Сессия игрока в своей зоне спавна и без объявленной готовности.</summary>
        private PlayerSession CreateSession(string name)
        {
            GameObject go = CreateNetworkObject(name);
            PlayerSession session = go.AddComponent<PlayerSession>();
            EnableNetworking(go);
            SpawnOnServer(session);

            session.PlayerName = name;
            session.ServerEnterSpawnZone(session.TeamIndex);
            return session;
        }

        /// <summary>Запускает матч, минуя ожидание подключения живых игроков.</summary>
        private void StartMatch(float timeLimit = RoundReadiness.DefaultTimeLimit,
                               RoundReadinessTimeoutRule rule = RoundReadinessTimeoutRule.AutoReady,
                               RoundStartRule startRule = RoundStartRule.Readiness)
        {
            SetPrivateField(_mode, "_readinessTimeLimit", timeLimit);
            SetPrivateField(_mode, "_readinessTimeoutRule", rule);
            SetPrivateField(_mode, "_roundStartRule", startRule);

            _mode.Initialize(new[] { _teamA, _teamB });
            InvokePrivateMethod(_mode, "InitializeActiveGame");

            Assert.IsNotNull(_mode.RoundPhases,
                "InitializeActiveGame не создал RoundPhases — значит [Server]-заглушка всё ещё срабатывает.");
        }

        /// <summary>Доводит раунд до фазы закупки — той единственной, где ждут готовности.</summary>
        private void AdvanceToEquipment()
        {
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Equipment, "фазы Equipment");

            // Один лишний тик: состав готовых пересчитывается уже внутри фазы,
            // а не в тот тик, которым в неё вошли.
            _driver.Advance();
        }

        /// <summary>
        /// Погибший в прошлом раунде возрождается, только дойдя до своей зоны. Пока он идёт,
        /// закупка обязана его ждать: иначе готовые союзники (или бот, готовый сразу) начинают
        /// раунд без него, и арсенал для него так и не открывается.
        /// </summary>
        [Test]
        public void Закупка_ждёт_погибшего_пока_он_не_вернулся_на_базу()
        {
            SilenceMirrorNoise();
            StartMatch();
            _roster.MarkDead(_playerA);

            AdvanceToEquipment();
            _playerB.ServerSetReady(true, "тест");
            AdvanceSeconds(5f);

            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase,
                "Закупка кончилась, пока погибший шёл на базу: ждали только живых.");

            // Дошёл, возродился, взял жетон — раунд идёт дальше.
            _roster.MarkDead(_playerA, false);
            _playerA.ServerSetReady(true, "тест");
            AdvanceSeconds(1f);

            Assert.AreNotEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase, "Все готовы, а закупка не кончилась.");
        }

        /// <summary>Даёт сессии живое тело — только таких подготовка ждёт на базе.</summary>
        private void GiveAvatar(PlayerSession session)
        {
            GameObject go = CreateNetworkObject(session.PlayerName + "_Avatar");
            go.AddComponent<UltimateXR.Mechanics.Weapons.UxrActor>();
            PlayerController avatar = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            InvokeLifecycleMethod(avatar, "Awake");
            SpawnOnServer(avatar);
            session.ActiveAvatar = avatar;
            avatar.GetComponent<UltimateXR.Mechanics.Weapons.UxrActor>().Life = 100f;
        }

        /// <summary>
        /// Закупка открывается, когда все вернулись на свою базу. Раньше подготовка длилась ровно
        /// секунду: отошедший (или погибший и идущий на возрождение) пропускал арсенал, а после смены
        /// сторон команда оказывалась на закупке в чужой половине.
        /// </summary>
        [Test]
        public void Закупка_открывается_когда_все_на_своей_базе()
        {
            SilenceMirrorNoise();
            GiveAvatar(_playerA);
            GiveAvatar(_playerB);
            _playerA.ServerExitSpawnZone(_playerA.TeamIndex);
            StartMatch();

            AdvanceSeconds(10f);
            Assert.AreEqual(RoundPhase.Setup, _mode.CurrentRoundPhase, "Закупка открылась, хотя игрок A не на базе.");

            _playerA.ServerEnterSpawnZone(_playerA.TeamIndex);
            AdvanceSeconds(1f);
            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase, "Все на базе, а закупка не открылась.");
        }

        [Test]
        public void Не_вернувшийся_не_держит_матч_дольше_предела()
        {
            SilenceMirrorNoise();
            GiveAvatar(_playerA);
            _playerA.ServerExitSpawnZone(_playerA.TeamIndex);
            StartMatch();

            AdvanceSeconds(RoundPhases.SetupDuration + RoundPhases.ReturnToBaseLimit + 1f);
            Assert.AreNotEqual(RoundPhase.Setup, _mode.CurrentRoundPhase, "Предел возвращения на базу не сработал.");
        }

        /// <summary>Крутит матч заданное игровое время, не ожидая никакого условия.</summary>
        private void AdvanceSeconds(float seconds)
        {
            int steps = RoundFlowDriver.StepsFor(seconds);
            for (int i = 0; i < steps; i++) _driver.Advance();
        }

        // ── Готовность двигает фазу ─────────────────────────────────────────

        [Test]
        public void Пока_готовы_не_все_раунд_стоит_в_закупке()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f);   // предел выключен: проверяем именно ожидание

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");

            AdvanceSeconds(30f);

            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase,
                "Готовность объявил один игрок из двух, а раунд ушёл из закупки.\n" +
                "Тогда второй игрок остаётся без снаряжения — это и есть суть RDY-01.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Когда_готовы_все_раунд_идёт_дальше()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");
            _playerB.ServerSetReady(true, "тест");

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Countdown,
                "обратного отсчёта после готовности обоих игроков");

            Assert.AreEqual(RoundPhase.Countdown, _mode.CurrentRoundPhase,
                "Готовы оба, а отсчёт не начался.\nФактически наблюдалось: " + _driver.DumpSequence());
        }

        // ── Отмена и условие зоны ───────────────────────────────────────────

        [Test]
        public void Готовность_можно_отменить_до_отсчёта()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");
            _playerB.ServerSetReady(true, "тест");
            Assert.IsTrue(_playerA.ReadyState, "Контроль: готовность объявлена.");

            // Передумал раньше, чем машина успела сдвинуть фазу.
            _playerA.ServerSetReady(false, "тест: игрок передумал");

            AdvanceSeconds(30f);

            Assert.IsFalse(_playerA.ReadyState, "Отмена готовности не сохранилась.");
            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase,
                "Игрок отменил готовность, а раунд всё равно ушёл из закупки.\n" +
                "Отмена — половина смысла явного состояния: без неё это по-прежнему жест.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Выход_из_зоны_снимает_готовность()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");
            _playerB.ServerSetReady(true, "тест");

            _playerA.ServerExitSpawnZone(_playerA.TeamIndex);

            Assert.IsFalse(_playerA.ReadyState,
                "Игрок вышел из зоны спавна, а готовность осталась.\n" +
                "Зона — условие готовности: раунд не вправе начаться, пока игрок не у себя на спавне.");

            AdvanceSeconds(30f);

            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase,
                "Готовность снялась, а раунд всё равно ушёл из закупки.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Возврат_в_зону_готовность_не_возвращает()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");
            _playerA.ServerExitSpawnZone(_playerA.TeamIndex);
            _playerA.ServerEnterSpawnZone(_playerA.TeamIndex);

            Assert.IsFalse(_playerA.ReadyState,
                "Возврат в зону сам собой вернул готовность. Готовность — намерение, " +
                "и объявить его игрок обязан заново: иначе снятие при выходе ничего не значит.");
        }

        // ── Готовность живёт один раунд ─────────────────────────────────────

        [Test]
        public void Новый_раунд_сбрасывает_готовность()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");
            _playerB.ServerSetReady(true, "тест");

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "фазы Combat");
            _mode.RoundPhases.RequestRoundEnd(_teamA);

            _driver.AdvanceUntil(() => _mode.CurrentRoundNumber == 2, "начала второго раунда");

            Assert.IsFalse(_playerA.ReadyState,
                "Готовность пережила конец раунда.\n" +
                "Тогда фаза закупки второго раунда кончается, не начавшись: арсенал открывается " +
                "и тут же закрывается, а игрок не успевает ничего взять.");
            Assert.IsFalse(_playerB.ReadyState, "То же самое для второго игрока.");

            AdvanceSeconds(30f);

            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase,
                "Второй раунд проскочил закупку — готовность прошлого раунда всё ещё в силе.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        // ── Предел ожидания ─────────────────────────────────────────────────

        [Test]
        public void Без_предела_раунд_ждёт_сколько_угодно()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");

            AdvanceSeconds(300f);

            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase,
                "Предел выключен (0 с), но раунд всё равно стартовал без второго игрока.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Предел_ожидания_объявляет_готовность_за_отошедших()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 10f, rule: RoundReadinessTimeoutRule.AutoReady);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Countdown,
                "старта отсчёта по истечении предела ожидания");

            Assert.IsTrue(_playerB.ReadyState,
                "Правило AutoReady обязано объявить готовность за того, кого не дождались: " +
                "состав раунда не меняется, отошедший просто входит в бой с тем, что успел взять.");
        }

        [Test]
        public void Правило_старта_без_отошедших_готовность_за_них_не_объявляет()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 10f, rule: RoundReadinessTimeoutRule.StartWithoutPending);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Countdown,
                "старта отсчёта по истечении предела ожидания");

            Assert.IsFalse(_playerB.ReadyState,
                "Правило StartWithoutPending не должно объявлять готовность за отошедшего: " +
                "в этом и вся разница с AutoReady — раунд стартует, но запись «его не дождались» остаётся.");
        }

        [Test]
        public void Предел_ожидания_не_срабатывает_раньше_срока()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 30f, rule: RoundReadinessTimeoutRule.AutoReady);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");

            AdvanceSeconds(20f);

            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase,
                "Предел — 30 с, а раунд стартовал через 20.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
            Assert.IsFalse(_playerB.ReadyState,
                "Готовность объявлена за игрока до истечения предела ожидания.");
        }

        // ── Состав неготовых виден клиентам ─────────────────────────────────

        [Test]
        public void Состав_неготовых_выкладывается_состоянием()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");
            _driver.Advance();

            CollectionAssert.AreEqual(new[] { _playerB.netId }, _mode.PendingReadiness.ToArray(),
                "Сервер обязан выложить состав неготовых состоянием, а не разовым событием: " +
                "вновь подключившийся получает его начальным значением спавна и сразу знает, кого ждут.\n" +
                "Фактически в списке: " + string.Join(", ", _mode.PendingReadiness.Select(id => id.ToString()).ToArray()));

            _playerB.ServerSetReady(true, "тест");
            _driver.Advance();

            Assert.AreEqual(0, _mode.PendingReadiness.Count,
                "Готовы все, а список ожидаемых не опустел — HUD так и будет показывать «ждём Петю».");
        }

        // ── Старт по таймеру (RoundStartRule.Timer) ─────────────────────────

        [Test]
        public void Таймер_закупка_длится_отведённое_время_даже_если_все_готовы()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 10f, startRule: RoundStartRule.Timer);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");
            _playerB.ServerSetReady(true, "тест");

            AdvanceSeconds(5f);

            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase,
                "При старте по таймеру готовность не спрашивается, а раунд ушёл из закупки " +
                "раньше срока, потому что все объявили готовность.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Таймер_раунд_стартует_по_времени_без_единой_готовности()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 10f, startRule: RoundStartRule.Timer);

            AdvanceToEquipment();

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Countdown,
                "старта отсчёта по окончании времени закупки");

            Assert.AreEqual(RoundPhase.Countdown, _mode.CurrentRoundPhase,
                "Время закупки вышло, а отсчёт не начался.\nФактически наблюдалось: " + _driver.DumpSequence());
            Assert.IsFalse(_playerA.ReadyState,
                "Старт по таймеру не должен объявлять готовность за игроков: " +
                "это не правило предела AutoReady, готовность здесь просто не участвует.");
        }

        [Test]
        public void Таймер_никого_не_ждёт()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 10f, startRule: RoundStartRule.Timer);

            AdvanceToEquipment();
            _driver.Advance();

            Assert.AreEqual(0, _mode.PendingReadiness.Count,
                "При старте по таймеру список ожидаемых обязан быть пуст — " +
                "иначе HUD покажет «ждём Петю», хотя Петю никто не ждёт.");
        }

        [Test]
        public void Таймер_без_предела_берёт_умолчание_а_не_ждёт_вечно()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f, startRule: RoundStartRule.Timer);

            AdvanceToEquipment();
            AdvanceSeconds(RoundReadiness.DefaultTimeLimit - 5f);

            Assert.AreEqual(RoundPhase.Equipment, _mode.CurrentRoundPhase,
                "Контроль: до умолчания закупка ещё идёт.\nФактически наблюдалось: " + _driver.DumpSequence());

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Countdown,
                "старта отсчёта по умолчанию времени закупки");

            Assert.AreEqual(RoundPhase.Countdown, _mode.CurrentRoundPhase,
                "Таймер с пределом 0 с понят буквально — раунд не начался бы никогда.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Вне_фазы_закупки_список_ожидаемых_пуст()
        {
            SilenceMirrorNoise();
            StartMatch(timeLimit: 0f);

            AdvanceToEquipment();
            _playerA.ServerSetReady(true, "тест");
            _driver.Advance();
            Assert.AreEqual(1, _mode.PendingReadiness.Count, "Контроль: в закупке кого-то ждут.");

            _playerB.ServerSetReady(true, "тест");
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "фазы Combat");
            _driver.Advance();

            Assert.AreEqual(0, _mode.PendingReadiness.Count,
                "В бою готовности не ждут, а список ожидаемых не пуст.");
        }
    }
}
