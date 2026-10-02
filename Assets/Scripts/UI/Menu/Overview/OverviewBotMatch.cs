namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>
    /// Кнопка «Матч с ботами» на «Обзоре» (T-48): игрок в шлеме один запускает матч, без админки и режима отладки.
    /// Видна в лобби, в разминке карты и на итоге карты, если игроку можно (<c>BotMatchPlan.CanRequest</c>).
    /// Чистая функция над планом кнопок — тесты <c>OverviewBotMatchTests</c>.
    /// </summary>
    public static class OverviewBotMatch
    {
        public const string Label = "Матч с ботами";

        /// <summary>
        /// Добавляет кнопку в план: главным действием, если главного нет (игрок без админки), иначе первой в ряду
        /// (у админа главное — «Начать матч» / «Выбрать серию»).
        /// </summary>
        public static void Apply(OverviewAdminPlan plan, OverviewContext context, bool allowed)
        {
            if (plan == null || !allowed || !Offered(context)) return;

            var action = new OverviewAction { Label = Label, BotMatch = true };
            if (plan.Primary == null) plan.Primary = action;
            else plan.Others.Insert(0, action);
        }

        /// <summary>Матча нет: лобби, разминка карты, итог карты.</summary>
        public static bool Offered(OverviewContext context) =>
            context == OverviewContext.Lobby || context == OverviewContext.Warmup || context == OverviewContext.MapFinished;
    }
}
