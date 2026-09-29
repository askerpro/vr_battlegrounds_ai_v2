using System.Collections.Generic;
using System.Text;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Чем кончается фаза закупки (<c>Equipment</c>). Правило матча — настройка режима:
    /// механизм готовности сохранён, но его можно выключить, не трогая код.
    /// См. <c>Docs/gameplay.md</c>, «Готовность к раунду».
    /// </summary>
    public enum RoundStartRule
    {
        /// <summary>
        /// Раунд ждёт готовности всех игроков команд, включая идущих на возрождение (жетон на стене арсенала).
        /// Предел ожидания — страховка: по его истечении работает
        /// <see cref="RoundReadinessTimeoutRule" />.
        /// </summary>
        Readiness,

        /// <summary>
        /// Закупка длится ровно предел ожидания, готовность не спрашивается.
        /// Жетон на стене не показывается — объявлять им нечего.
        /// </summary>
        Timer
    }

    /// <summary>
    /// Что делать, когда предел ожидания готовности истёк, а готовы не все.
    /// Правило матча — настройка режима, а не жёстко зашитое решение: см.
    /// <c>Docs/gameplay.md</c>, «Готовность к раунду».
    /// </summary>
    public enum RoundReadinessTimeoutRule
    {
        /// <summary>
        /// Объявить готовность за неготовых и начать раунд полным составом.
        /// Умолчание: состав раунда не меняется, а значит не меняются и условия
        /// победы — отошедший просто входит в бой с тем, что успел взять.
        /// </summary>
        AutoReady,

        /// <summary>
        /// Начать раунд, не объявляя готовность за неготовых. Раунд идёт тем же
        /// составом, но список ожидаемых сохраняется: HUD и логи помнят, кого ждали
        /// и не дождались.
        /// </summary>
        StartWithoutPending
    }

    /// <summary>
    /// Готовность игроков к раунду: кто готов, кого ждём, не пора ли начинать
    /// без опоздавших.
    ///
    /// <para>
    /// <b>Зачем отдельный класс.</b> Ответ на вопрос «все ли готовы» нужен сразу
    /// в трёх местах: машине раунда — чтобы сдвинуть фазу, режиму — чтобы отдать
    /// клиентам состав неготовых, инспектору — чтобы показать, почему раунд стоит.
    /// Разложенный по этим трём местам, он был бы тремя копиями одного цикла,
    /// расходящимися при первой же правке. Здесь он один.
    /// </para>
    ///
    /// <para>
    /// <b>Обычный C#-класс</b>, как <see cref="RoundManager" /> и <see cref="SetManager" />:
    /// об игроках спрашивает <see cref="IPlayerRoster" />, поэтому весь разбор готовности,
    /// включая предел ожидания, гоняется EditMode-тестом без живых аватаров.
    /// </para>
    /// </summary>
    public sealed class RoundReadiness
    {
        /// <summary>
        /// Предел ожидания по умолчанию, секунды. Сорока пяти хватает, чтобы дойти
        /// до стены, выбрать оружие и вернуться на спавн, и мало, чтобы один
        /// отошедший игрок ощутимо задержал остальных.
        /// </summary>
        public const float DefaultTimeLimit = 45f;

        private readonly IPlayerRoster _roster;

        /// <summary>Игроки раунда, не объявившие готовность. Пересобирается каждым <see cref="Evaluate" />.</summary>
        private readonly List<PlayerSession> _pending = new List<PlayerSession>();

        /// <summary>
        /// Предел ожидания, секунды. Ноль и меньше означают «ждать сколько угодно» —
        /// раунд не начнётся, пока не готовы все. В режиме <see cref="RoundStartRule.Timer" />
        /// это длительность закупки, и она всегда положительна.
        /// </summary>
        public float TimeLimit { get; }

        /// <summary>Что делать по истечении предела.</summary>
        public RoundReadinessTimeoutRule TimeoutRule { get; }

        /// <summary>Чем кончается фаза закупки: готовностью или только таймером.</summary>
        public RoundStartRule StartRule { get; }

        /// <param name="roster">Источник данных об игроках. null — боевой PlayersManager.</param>
        /// <param name="timeLimit">
        ///     Предел ожидания в секундах; ноль и меньше — предела нет. Для
        ///     <see cref="RoundStartRule.Timer" /> — длительность закупки.
        /// </param>
        /// <param name="timeoutRule">Правило матча при истечении предела.</param>
        /// <param name="startRule">Чем кончается фаза закупки.</param>
        public RoundReadiness(IPlayerRoster roster = null,
                              float timeLimit = DefaultTimeLimit,
                              RoundReadinessTimeoutRule timeoutRule = RoundReadinessTimeoutRule.AutoReady,
                              RoundStartRule startRule = RoundStartRule.Readiness)
        {
            _roster = roster ?? new PlayersManagerRoster();
            TimeoutRule = timeoutRule;
            StartRule = startRule;

            TimeLimit = EffectiveTimeLimit(startRule, timeLimit);

            if (TimeLimit != timeLimit)
            {
                GameLog.Match.Warning(
                    $"[RoundReadiness] Старт по таймеру с пределом {timeLimit:F0} с — раунд не начался бы никогда. " +
                    $"Взято умолчание {DefaultTimeLimit:F0} с.");
            }
        }

        /// <summary>
        /// Предел, который реально действует при данном правиле. «Ждать без предела» при
        /// старте по таймеру значило бы «никогда не начинать» — такую настройку не исполняем
        /// буквально, а подменяем умолчанием. Одна функция и для машины раунда, и для HUD,
        /// чтобы показанный остаток не расходился с настоящим.
        /// </summary>
        public static float EffectiveTimeLimit(RoundStartRule startRule, float timeLimit) =>
            startRule == RoundStartRule.Timer && timeLimit <= 0f ? DefaultTimeLimit : timeLimit;

        /// <summary>
        /// Игроки, которых раунд ещё ждёт (в том числе погибшие, идущие на базу). Пусто до первого <see cref="Evaluate" />
        /// и всегда пусто при старте по таймеру — там не ждут никого.
        /// </summary>
        public IReadOnlyList<PlayerSession> Pending => _pending;

        /// <summary>Сколько игроков участвует в раунде (живые и идущие на возрождение) на момент последней проверки.</summary>
        public int ParticipantCount { get; private set; }

        /// <summary>
        /// Все игроки раунда объявили готовность (и они вообще есть). При старте по
        /// таймеру готовность не спрашивается, поэтому всегда false.
        /// </summary>
        public bool AllReady => StartRule == RoundStartRule.Readiness && ParticipantCount > 0 && _pending.Count == 0;

        /// <summary>Предел ожидания истёк и правило матча уже применено.</summary>
        public bool LimitExpired { get; private set; }

        /// <summary>Время закупки вышло. Имеет смысл только при старте по таймеру.</summary>
        public bool PurchaseTimeOver { get; private set; }

        /// <summary>
        /// Можно начинать отсчёт: готовы все, истёк предел ожидания либо вышло время
        /// закупки. Без единого игрока — нельзя: начинать раунд не с кем.
        /// </summary>
        public bool IsSatisfied
        {
            get
            {
                if (ParticipantCount == 0) return false;
                if (StartRule == RoundStartRule.Timer) return PurchaseTimeOver;
                return _pending.Count == 0 || LimitExpired;
            }
        }

        /// <summary>
        /// Готовит объект к новому раунду: снимает готовность со всех игроков команд
        /// и забывает про истёкший предел.
        ///
        /// Готовность объявляется заново каждый раунд — иначе она означала бы «я был
        /// готов когда-то», и фаза <c>Equipment</c> второго раунда кончалась бы,
        /// не начавшись.
        ///
        /// Проходим по <see cref="IPlayerRoster.GetPlayers" />, а не по живым: сброс
        /// идёт в фазе <c>Setup</c>, до отложенного респавна, и мёртвый в прошлом
        /// раунде игрок иначе унёс бы старую готовность в новый раунд.
        /// </summary>
        public void Reset(IReadOnlyList<TeamData> teams)
        {
            _pending.Clear();
            ParticipantCount = 0;
            LimitExpired = false;
            PurchaseTimeOver = false;

            if (teams == null) return;

            foreach (TeamData team in teams)
            {
                foreach (PlayerSession session in _roster.GetPlayers(team))
                {
                    if (session != null) session.ServerResetRoundReadiness();
                }
            }
        }

        /// <summary>
        /// Пересчитывает состав ожидаемых и, если пришёл срок, применяет правило предела.
        /// Зовётся ровно в фазе <c>Equipment</c> — в остальных фазах готовности не ждут.
        /// </summary>
        /// <param name="teams">Команды текущего раунда.</param>
        /// <param name="elapsedInPhase">Сколько секунд идёт фаза ожидания готовности.</param>
        public void Evaluate(IReadOnlyList<TeamData> teams, float elapsedInPhase)
        {
            Collect(teams);

            if (StartRule == RoundStartRule.Timer)
            {
                if (!PurchaseTimeOver && elapsedInPhase >= TimeLimit)
                {
                    PurchaseTimeOver = true;
                    GameLog.Match.Info($"[RoundReadiness] Время закупки {TimeLimit:F0} с вышло.");
                }
                return;
            }

            if (LimitExpired) return;
            if (TimeLimit <= 0f) return;
            if (ParticipantCount == 0 || _pending.Count == 0) return;
            if (elapsedInPhase < TimeLimit) return;

            LimitExpired = true;
            ApplyTimeoutRule(teams);
        }

        /// <summary>
        /// Все игроки раунда с телом стоят каждый в своей зоне спавна — можно открывать закупку.
        /// Погибший в прошлом раунде возрождается, дойдя до зоны, поэтому «все на базе» значит и
        /// «все живы». Сессия без аватара (подключается, заглушка теста) не ждётся: стоять ей нечем.
        /// Вне базы — в <paramref name="away"/>, для лога.
        /// </summary>
        public bool AllAtBase(IReadOnlyList<TeamData> teams, List<PlayerSession> away = null) =>
            AtBase(teams, away, aliveOnly: false);

        /// <summary>
        /// Все живые игроки раунда стоят каждый в своей зоне — обратный отсчёт может идти.
        /// Выбывший (опоздал к закупке, оживёт в следующем раунде) отсчёт не держит.
        /// </summary>
        public bool AllAliveAtBase(IReadOnlyList<TeamData> teams, List<PlayerSession> away = null) =>
            AtBase(teams, away, aliveOnly: true);

        private bool AtBase(IReadOnlyList<TeamData> teams, List<PlayerSession> away, bool aliveOnly)
        {
            away?.Clear();
            if (teams == null) return true;

            bool all = true;
            foreach (TeamData team in teams)
            {
                foreach (PlayerSession session in _roster.GetPlayers(team))
                {
                    if (session == null || session.ActiveAvatar == null || session.IsInSpawnZone) continue;
                    if (aliveOnly && !session.ActiveAvatar.IsAlive) continue;

                    all = false;
                    away?.Add(session);
                }
            }
            return all;
        }

        /// <summary>Пересобирает <see cref="_pending" /> и <see cref="ParticipantCount" />.</summary>
        private void Collect(IReadOnlyList<TeamData> teams)
        {
            _pending.Clear();
            ParticipantCount = 0;

            if (teams == null) return;

            foreach (TeamData team in teams)
            {
                // Ждём всех игроков команды, в том числе погибших в прошлом раунде: они
                // возрождаются, только дойдя до своей зоны, и закупка обязана их дождаться.
                // Раньше ждали только живых — готовые союзники (или бот) начинали раунд
                // без идущего на базу, и арсенал для него так и не открывался. Не дошёл —
                // решает предел ожидания, как для любого неготового.
                foreach (PlayerSession session in _roster.GetPlayers(team))
                {
                    if (session == null) continue;

                    ParticipantCount++;

                    // При старте по таймеру не ждут никого: иначе HUD показывал бы
                    // «ждём Петю» там, где Петю никто не ждёт.
                    if (StartRule == RoundStartRule.Readiness && !session.ReadyState)
                        _pending.Add(session);
                }
            }
        }

        /// <summary>Применяет правило матча к тем, кого не дождались.</summary>
        private void ApplyTimeoutRule(IReadOnlyList<TeamData> teams)
        {
            string names = DescribePending();

            if (TimeoutRule == RoundReadinessTimeoutRule.AutoReady)
            {
                foreach (PlayerSession session in _pending)
                    session.ServerSetReady(true, $"предел ожидания {TimeLimit:F0} с истёк");

                // Пересобираем состав: после автоготовности ждать больше некого,
                // и наружу должен уходить уже пустой список.
                Collect(teams);

                GameLog.Match.Warning(
                    $"[RoundReadiness] Предел ожидания {TimeLimit:F0} с истёк. " +
                    $"Готовность объявлена за неготовых: {names}. Раунд начинается полным составом.");
                return;
            }

            GameLog.Match.Warning(
                $"[RoundReadiness] Предел ожидания {TimeLimit:F0} с истёк. " +
                $"Раунд начинается без отошедших: {names}. Готовность за них не объявлялась.");
        }

        /// <summary>Кого ждём — человеческим языком. Для логов, вердиктов и инспектора.</summary>
        public string DescribePending()
        {
            if (_pending.Count == 0) return "никого";

            StringBuilder sb = new StringBuilder();
            foreach (PlayerSession session in _pending)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(session.PlayerName);

                if (session.ActiveAvatar != null && !session.ActiveAvatar.IsAlive) sb.Append(" (выбыл, идёт на базу)");
                else if (!session.IsInSpawnZone) sb.Append(" (вне зоны спавна)");
            }

            return sb.ToString();
        }

        /// <summary>Почему раунд стоит в фазе <c>Equipment</c> — строкой для инспектора.</summary>
        public string Describe()
        {
            if (ParticipantCount == 0) return "Игроков в раунде нет — ждать некого.";

            if (StartRule == RoundStartRule.Timer)
                return $"Старт по таймеру: закупка {TimeLimit:F0} с, готовность не спрашивается ({ParticipantCount} игроков).";

            if (_pending.Count == 0)
                return $"Готовы все игроки ({ParticipantCount}).";

            string limit = TimeLimit > 0f
                ? $"Предел ожидания: {TimeLimit:F0} с, правило: {TimeoutRule}."
                : "Предел ожидания выключен — раунд ждёт всех.";

            return $"Ждём готовности ({_pending.Count} из {ParticipantCount}): {DescribePending()}\n{limit}";
        }
    }
}
