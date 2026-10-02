using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Bots
{
    /// <summary>Стадия игры глазами бота — сжатие «режим + фаза раунда» до того, что меняет его поведение.</summary>
    public enum BotStage
    {
        /// <summary>Матча нет: лобби, разминка карты, режим ещё не создан.</summary>
        NoMatch,
        /// <summary>Между раундами: подготовка раунда (<c>Setup</c>), итог (<c>Resolution</c>), табло (<c>Scoreboard</c>).</summary>
        BetweenRounds,
        /// <summary>Закупка (<c>Equipment</c>): стена открыта, ждём готовности.</summary>
        Equipment,
        /// <summary>Обратный отсчёт (<c>Countdown</c>): все живые обязаны стоять на базе.</summary>
        Countdown,
        /// <summary>Бой (<c>Combat</c>) или режим без раундов (Respawn): оружие включено.</summary>
        Combat
    }

    /// <summary>Приказ боту на этот тик. Исполняют <see cref="BotNavigator"/> (ноги) и <see cref="BotGunner"/> (оружие).</summary>
    public enum BotOrder
    {
        /// <summary>Стоять где стоит: матча нет.</summary>
        Idle,
        /// <summary>Идти в свою зону спавна (база): вернуться после раунда, к закупке, ожить призраком.</summary>
        GoHome,
        /// <summary>На базе в закупке: купить, взять оружие, объявить готовность.</summary>
        PrepareAtBase,
        /// <summary>Стоять на базе: отсчёт, конец раунда, призрак дошёл.</summary>
        HoldAtBase,
        /// <summary>Бой, врага не видно: идти к ближайшему живому врагу.</summary>
        Hunt,
        /// <summary>Бой, враг виден: стоять и стрелять.</summary>
        Engage
    }

    /// <summary>Что бот знает о себе и мире в этот тик. Собирает <see cref="BotSenses"/>.</summary>
    public struct BotSituation
    {
        public BotStage Stage;
        /// <summary>Жив (не призрак).</summary>
        public bool Alive;
        /// <summary>Голова в зоне спавна своей команды (<c>PlayerSession.IsInSpawnZone</c>).</summary>
        public bool InOwnZone;
        /// <summary>Есть живой враг на карте.</summary>
        public bool EnemyKnown;
        /// <summary>Враг виден из ствола и в пределах дальности стрельбы.</summary>
        public bool EnemyVisible;
    }

    /// <summary>
    /// Решения бота — чистые функции без сцены (T-48). Директор (<see cref="BotDirector"/>) каждый тик собирает
    /// <see cref="BotSituation"/>, спрашивает здесь приказ и раздаёт его исполнителям. Здесь же — когда покупать,
    /// когда брать оружие и когда объявлять готовность. Тесты — <c>BotOrdersTests</c>.
    /// </summary>
    public static class BotOrders
    {
        /// <summary>Приказ по ситуации.</summary>
        public static BotOrder Decide(BotSituation s)
        {
            if (s.Stage == BotStage.NoMatch) return BotOrder.Idle;

            // Призрак оживает, только дойдя до своей зоны (Elimination — к новому раунду): туда и идёт в любой фазе.
            if (!s.Alive) return s.InOwnZone ? BotOrder.HoldAtBase : BotOrder.GoHome;

            switch (s.Stage)
            {
                case BotStage.Equipment:
                    return s.InOwnZone ? BotOrder.PrepareAtBase : BotOrder.GoHome;

                case BotStage.Combat:
                    if (s.EnemyVisible) return BotOrder.Engage;
                    // Врагов нет — раунд вот-вот кончится: стоять, где стоит.
                    return s.EnemyKnown ? BotOrder.Hunt : BotOrder.HoldAtBase;

                default:
                    // Отсчёт (живые обязаны стоять на базе) и между раундами (возврат на базу, как человек).
                    return s.InOwnZone ? BotOrder.HoldAtBase : BotOrder.GoHome;
            }
        }

        /// <summary>Стадия по режиму карты и фазе раунда.</summary>
        /// <param name="hasMode">На карте есть активный режим.</param>
        /// <param name="isWarmup">Режим — разминка.</param>
        /// <param name="hasRounds">Режим с раундами (Elimination); без раундов (Respawn) — всегда бой.</param>
        /// <param name="roundsActive">Раунды идут (матч начался, а не ждёт игроков и не кончился).</param>
        /// <param name="phase">Фаза раунда.</param>
        public static BotStage StageOf(bool hasMode, bool isWarmup, bool hasRounds, bool roundsActive, RoundPhase phase)
        {
            if (!hasMode || isWarmup) return BotStage.NoMatch;
            if (!hasRounds) return BotStage.Combat;
            if (!roundsActive) return BotStage.NoMatch;

            switch (phase)
            {
                case RoundPhase.Equipment: return BotStage.Equipment;
                case RoundPhase.Countdown: return BotStage.Countdown;
                case RoundPhase.Combat: return BotStage.Combat;
                default: return BotStage.BetweenRounds;
            }
        }

        /// <summary>
        /// Пора ли покупать: живой, в этой закупке ещё не покупал и стоит на базе в закупке. Без экономики
        /// (<paramref name="free"/>: Respawn — закупки нет, всё бесплатно) — в любой момент матча.
        /// </summary>
        public static bool ShouldShop(BotOrder order, bool alive, bool shoppedThisRound, bool free = false) =>
            alive && !shoppedThisRound && (order == BotOrder.PrepareAtBase || (free && order != BotOrder.Idle));

        /// <summary>
        /// Пора ли брать оружие в руку. В закупке — только после решения о покупке (иначе пистолет в руке, а купленный
        /// ствол некуда взять); в отсчёте и бою — в любом случае: бот, не дошедший до базы, воюет стартовым пистолетом.
        /// </summary>
        public static bool ShouldArm(BotStage stage, bool alive, bool shoppedThisRound)
        {
            if (!alive) return false;
            switch (stage)
            {
                case BotStage.Equipment: return shoppedThisRound;
                case BotStage.Countdown:
                case BotStage.Combat: return true;
                default: return false;
            }
        }

        /// <summary>Объявлять ли готовность: стоит на базе в закупке, живой, покупка решена.</summary>
        public static bool ShouldDeclareReady(BotOrder order, bool alive, bool inOwnZone, bool shoppedThisRound, bool ready) =>
            order == BotOrder.PrepareAtBase && alive && inOwnZone && shoppedThisRound && !ready;
    }
}
