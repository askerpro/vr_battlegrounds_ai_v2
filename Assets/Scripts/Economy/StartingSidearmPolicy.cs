using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Economy
{
    /// <summary>
    /// Стартовый пистолет (T-45): живой игрок команды без пистолета получает
    /// <see cref="WeaponRegistry.DefaultSidearm"/> (Viper, роль Glock-18 в CS2) бесплатно в кобуру
    /// второго оружия — один раз за раунд, в подготовке или закупке.
    ///
    /// <para>
    /// <b>Отличие от CS2.</b> В CS2 выживший сохраняет оружие, и пистолет выдаётся только тому, у кого
    /// его нет. У нас оружие раунд не переживает (<c>EquipmentStrip</c> на входе в <c>Resolution</c>), поэтому
    /// пистолет получает каждый, каждый раунд. Правило «нет пистолета» всё равно проверяется — оно
    /// заработает само, если сохранение оружия выживших когда-нибудь появится.
    /// </para>
    ///
    /// <para>
    /// Сервер, опрос раз в <see cref="Interval"/> секунд: погибший оживает в зоне в подготовке или
    /// закупке и получает пистолет там же. Кому выдать — чистое правило <see cref="ShouldGrant"/>;
    /// выдача — <see cref="PlayerLoadoutManager.ServerGiveWeapon"/>. Боты — обычные игроки и получают
    /// его так же.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StartingSidearmPolicy : MonoBehaviour
    {
        private const float Interval = 0.25f;

        private EliminationMode _mode;
        private float _nextUpdate;

        /// <summary>Ключи игроков, получивших пистолет в этом раунде.</summary>
        private readonly HashSet<string> _granted = new HashSet<string>();

        private void Awake()
        {
            _mode = GetComponent<EliminationMode>();
        }

        private void OnEnable()
        {
            EliminationMode.RoundPhaseChangedServer += HandleRoundPhase;
        }

        private void OnDisable()
        {
            EliminationMode.RoundPhaseChangedServer -= HandleRoundPhase;
        }

        private void HandleRoundPhase(RoundPhase phase)
        {
            // Новый раунд — новая выдача. Оружие прошлого раунда уже изъято на входе в Resolution.
            if (phase == RoundPhase.Setup) _granted.Clear();
        }

        private void Update()
        {
            if (!NetworkServer.active || Time.unscaledTime < _nextUpdate) return;
            _nextUpdate = Time.unscaledTime + Interval;
            ServerGrant();
        }

        /// <summary>
        /// Выдавать ли пистолет: только живому, только в подготовке/закупке, один раз за раунд,
        /// и только тому, у кого пистолета нет.
        /// </summary>
        public static bool ShouldGrant(bool alive, bool grantedThisRound, bool hasPistol, RoundPhase phase) =>
            alive && !grantedThisRound && !hasPistol &&
            (phase == RoundPhase.Setup || phase == RoundPhase.Equipment);

        /// <summary>Один проход выдачи. Для тестов — напрямую.</summary>
        public void ServerGrant()
        {
            if (_mode == null) _mode = GetComponent<EliminationMode>();
            if (_mode == null || _mode.CurrentState != EliminationState.Active) return;

            WeaponRegistry registry = WeaponRegistry.Instance;
            WeaponInfo sidearm = registry != null ? registry.DefaultSidearm : null;
            if (sidearm == null) return;

            foreach (TeamRuntimeData state in _mode.TeamStates.Values)
            {
                if (state == null) continue;

                foreach (PlayerSession session in state.Sessions)
                {
                    PlayerController avatar = session != null ? session.ActiveAvatar : null;
                    PlayerLoadoutManager loadout = avatar != null ? avatar.GetComponent<PlayerLoadoutManager>() : null;
                    if (loadout == null) continue;

                    string key = MatchEconomy.KeyOf(session);
                    bool hasPistol = loadout.HasWeaponOfCategory(WeaponCategory.Pistol);

                    if (!ShouldGrant(avatar.IsAlive, _granted.Contains(key), hasPistol, _mode.CurrentRoundPhase))
                    {
                        // Пистолет уже есть (купил сам или сохранил) — этот раунд выдача ему не нужна.
                        if (hasPistol && avatar.IsAlive) _granted.Add(key);
                        continue;
                    }

                    if (loadout.ServerGiveWeapon(sidearm))
                    {
                        _granted.Add(key);
                        GameLog.Match.Info($"[Economy] {session.PlayerName}: стартовый {sidearm.DisplayName} в кобуру.");
                    }
                }
            }
        }
    }
}
