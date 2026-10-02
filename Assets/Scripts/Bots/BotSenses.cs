using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Чтение мира для решений бота (T-48): стадия игры, ближайший живой враг, где стоит игрок. Сервер.
    /// Решения — чистая <see cref="BotOrders"/>; здесь только то, что требует сцены.
    /// </summary>
    public static class BotSenses
    {
        /// <summary>Стадия игры на этой карте.</summary>
        public static BotStage Stage()
        {
            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            if (mode == null) return BotStage.NoMatch;

            var elimination = mode as EliminationMode;
            return BotOrders.StageOf(true, mode.IsWarmup, elimination != null,
                                     elimination != null && elimination.CurrentState == EliminationState.Active,
                                     elimination != null ? elimination.CurrentRoundPhase : RoundPhase.Setup);
        }

        /// <summary>Номер закупки: раунд Elimination; без раундов — само тело (новая жизнь — новая закупка).</summary>
        public static int ShopToken(PlayerController body)
        {
            var elimination = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode as EliminationMode : null;
            if (elimination != null) return elimination.CurrentRoundNumber;
            return body != null ? body.GetInstanceID() : 0;
        }

        /// <summary>
        /// Где стоит игрок — точка пола под головой. У человека корень стоит на месте калибровки, по залу идёт
        /// голова; у бота так же (<see cref="BotBody"/>).
        /// </summary>
        public static Vector3 FeetOf(PlayerController player)
        {
            var avatar = player.GetComponent<UxrAvatar>();
            return avatar != null && avatar.CameraTransform != null ? avatar.CameraFloorPosition : player.transform.position;
        }

        /// <summary>Ближайший живой враг (любая дальность); null — врагов нет.</summary>
        public static PlayerController NearestEnemy(PlayerSession own, Vector3 from, float maxDistance = float.MaxValue)
        {
            PlayersManager players = PlayersManager.Instance;
            if (players == null || own == null) return null;

            PlayerController best = null;
            float bestDistance = maxDistance < float.MaxValue ? maxDistance * maxDistance : float.MaxValue;

            foreach (PlayerSession session in players.Sessions)
            {
                if (session == null || session == own || session.TeamIndex == own.TeamIndex || session.TeamIndex == 0) continue;
                if (session.Role != Core.GameRole.Player) continue;

                PlayerController other = session.ActiveAvatar;
                if (other == null || !other.IsAlive) continue;

                float distance = (FeetOf(other) - from).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
