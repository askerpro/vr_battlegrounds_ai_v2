using System;
using System.Collections.Generic;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// «Матч с ботами» (T-48) — чистые правила: кто вправе попросить, сколько ботов добавить, какой режим взять.
    /// Тесты <c>BotMatchPlanTests</c>. Исполняет <see cref="BotMatchStarter"/>.
    /// </summary>
    public static class BotMatchPlan
    {
        /// <summary>Меньше двух в команде — нет союзника, неинтересно.</summary>
        public const int MinTeamSize = 2;

        /// <summary>В зоне спавна 4 стены арсенала — по одной на игрока (T-45).</summary>
        public const int MaxTeamSize = 4;

        /// <summary>Режим матча с ботами по умолчанию: логика ботов написана под раунды Elimination.</summary>
        public const string PreferredModeId = "elimination";

        /// <summary>
        /// Вправе ли игрок запустить матч с ботами: админ — всегда; обычный игрок — только если он один из людей на
        /// сервере (боты никому не мешают).
        /// </summary>
        public static bool CanRequest(bool isAdmin, bool isPlayer, int otherHumans) =>
            isAdmin || (isPlayer && otherHumans == 0);

        /// <summary>Игроков в команде: не меньше <see cref="MinTeamSize"/>, людей поровну, не больше <see cref="MaxTeamSize"/>.</summary>
        public static int TeamSize(int humans, int teams)
        {
            if (teams <= 0) return 0;
            int perTeam = (humans + teams - 1) / teams;
            return Math.Min(MaxTeamSize, Math.Max(MinTeamSize, perTeam));
        }

        /// <summary>Сколько ботов добавить, чтобы команды были полными (существующие боты засчитываются).</summary>
        public static int BotsToAdd(int humans, int bots, int teams) =>
            Math.Max(0, TeamSize(humans, teams) * Math.Max(0, teams) - humans - bots);

        /// <summary>Что делать с запросом, ждущим боевую карту (<see cref="BotMatchStarter"/>, каждый кадр).</summary>
        public enum PendingStep
        {
            /// <summary>Карта ещё грузится или не встала в разминку.</summary>
            Wait,
            /// <summary>Карта в разминке — «Начать матч», затем боты и команды.</summary>
            GoLive,
            /// <summary>Матч уже запущен кем-то другим (админ, автозапуск) — только боты и команды людям.</summary>
            Fill,
            /// <summary>Время вышло, карта так и не встала.</summary>
            Expire
        }

        /// <summary>
        /// Шаг ожидающего запроса. Матч, уже идущий на новой карте, — не повод терять запрос: раньше запрос ждал
        /// строго разминку и через таймаут снимался с ложным «карта не загрузилась», без ботов и команд людям.
        /// </summary>
        public static PendingStep Step(bool loadingScene, float waited, float timeout, bool canGoLive, bool isLive)
        {
            if (loadingScene) return PendingStep.Wait;
            if (isLive) return PendingStep.Fill;
            if (canGoLive) return PendingStep.GoLive;
            return waited > timeout ? PendingStep.Expire : PendingStep.Wait;
        }

        /// <summary>Режим из доступных: <see cref="PreferredModeId"/>, если есть, иначе первый; нет режимов — null.</summary>
        public static string PickMode(IReadOnlyList<string> matchModeIds)
        {
            if (matchModeIds == null || matchModeIds.Count == 0) return null;

            foreach (string id in matchModeIds)
                if (string.Equals(id, PreferredModeId, StringComparison.OrdinalIgnoreCase)) return id;

            return matchModeIds[0];
        }
    }
}
