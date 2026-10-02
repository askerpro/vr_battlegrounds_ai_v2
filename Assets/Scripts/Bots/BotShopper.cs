using System.Collections.Generic;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Economy;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Закупка бота на сервере (T-48): решение — чистые правила <see cref="BotBuyPlan"/>, списание — та же точка, что
    /// у стены арсенала, <see cref="MatchEconomy.ServerTryPurchase"/> (без предмета, <c>itemNetId = 0</c>). Ствол бот
    /// берёт сам (<see cref="BotGunner"/>), со стены ничего не снимается: стена закреплена за игроком (T-45), и бот,
    /// снявший с неё ствол, отнял бы слот у человека.
    ///
    /// <para>
    /// Экономики нет (Respawn, разминка) — всё бесплатно: бот берёт лучшее основное оружие без списания.
    /// </para>
    /// </summary>
    public static class BotShopper
    {
        /// <summary>Решает и оплачивает покупку. null — ничего не куплено (эко или не по карману).</summary>
        public static WeaponInfo Buy(PlayerSession bot)
        {
            WeaponRegistry registry = WeaponRegistry.Instance;
            if (bot == null || registry == null) return null;

            var weapons = new List<WeaponInfo>();
            var items = new List<BotShopItem>();
            foreach (WeaponInfo info in registry.Weapons)
            {
                if (info == null || info.WeaponPrefab == null || info.WeaponPrefab.GetComponent<UxrFirearmWeapon>() == null) continue;
                weapons.Add(info);
                items.Add(new BotShopItem(info.Price, info.Category != WeaponCategory.Pistol));
            }

            int starterPrice = registry.DefaultSidearm != null ? registry.DefaultSidearm.Price : 0;
            MatchEconomy economy = MatchEconomy.Current;

            if (economy == null)
            {
                int free = BotBuyPlan.Choose(BotBuyKind.Full, int.MaxValue, items, starterPrice);
                return free >= 0 ? weapons[free] : null;
            }

            int money = economy.GetMoney(bot);
            BotBuyKind kind = KindFor(bot, money);
            int index = BotBuyPlan.Choose(kind, money, items, starterPrice);

            if (index < 0)
            {
                GameLog.Arsenal.Info($"[BotShopper] {bot.PlayerName}: {kind}, ${money} — без покупки.");
                return null;
            }

            WeaponInfo choice = weapons[index];
            if (!economy.ServerTryPurchase(bot, choice, 0)) return null;

            GameLog.Arsenal.Info($"[BotShopper] {bot.PlayerName}: {kind}, ${money} → {choice.DisplayName}.");
            return choice;
        }

        private static BotBuyKind KindFor(PlayerSession bot, int money)
        {
            var mode = Managers.MapReferee.Instance != null ? Managers.MapReferee.Instance.ActiveGameMode as EliminationMode : null;
            if (mode == null) return BotBuyPlan.Kind(money, false, false);

            int round = mode.CurrentRoundNumber;
            bool pistolRound = EliminationMode.IsFirstRoundOfHalf(round, mode.RoundsPerHalf);

            int enemyScore = 0;
            if (mode.Teams != null)
            {
                foreach (TeamData team in mode.Teams)
                {
                    if (team != null && team.teamIndex != bot.TeamIndex)
                        enemyScore = System.Math.Max(enemyScore, mode.GetScore(team));
                }
            }

            bool mustWin = BotBuyPlan.MustWin(round, mode.RoundsPerHalf, enemyScore, mode.RoundsToWin);
            return BotBuyPlan.Kind(money, pistolRound, mustWin);
        }
    }
}
