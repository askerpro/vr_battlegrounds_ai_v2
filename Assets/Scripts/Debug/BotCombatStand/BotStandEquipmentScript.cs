using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Сценарий снаряжения проверяемого бота (этап pf-equipment): что у него в начале case и после возрождения
    /// (покупка у кисти, пистолет в кобуре, ничего), когда приходит покупка, когда он погибает и оживает. Только штатные
    /// серверные пути: выдача в кобуру — <c>PlayerLoadoutManager.ServerGiveWeapon</c> (как <c>StartingSidearmPolicy</c>),
    /// оплата — <see cref="BotShopper.TryPay"/> (экономика матча; без неё бесплатно), смерть — урон
    /// <c>UxrActor.ReceiveDamage</c> (дальше смерть/призрак — правила gameplay, deferred-body), оживление —
    /// <c>PlayerController.Respawn</c> (как раунд Elimination). Решения бота (взять, убрать, поднять) — его собственные.
    /// Вызывается из сопрограммы стенда (верхний уровень, вне событий UXR). Ведёт журнал своих действий для отчёта.
    /// </summary>
    public sealed class BotStandEquipmentScript
    {
        private const int MaxLog = 32;

        private readonly BotStandRunOptions _options;
        private readonly WeaponInfo _weapon;
        private readonly WeaponInfo _purchase;
        private readonly float _start;
        private readonly List<Dictionary<string, object>> _log = new List<Dictionary<string, object>>();
        private bool _purchased, _killed, _respawned;

        /// <summary>Действия сценария по времени — в отчёт (<c>playerFlow.equipment.script</c>).</summary>
        public IReadOnlyList<Dictionary<string, object>> Log => _log;

        public BotStandEquipmentScript(BotStandRunOptions options, WeaponInfo weapon, WeaponInfo purchase)
        {
            _options = options;
            _weapon = weapon;
            _purchase = purchase;
            _start = Time.time;
        }

        /// <summary>Проверка опций до создания акторов; ошибка — InvalidFixture case.</summary>
        public static void Validate(BotStandRunOptions options, WeaponInfo weapon, WeaponInfo purchase)
        {
            if (options == null) return;
            bool equipment = options.HolsterStart || options.Unarmed || options.PurchaseAtSeconds >= 0f;
            if (equipment && !options.PlayerFlow)
                throw new InvalidOperationException("HolsterStart/Unarmed/покупка по времени — только режим PlayerFlow.");
            if (options.HolsterStart && options.Unarmed)
                throw new InvalidOperationException("HolsterStart и Unarmed взаимоисключающие.");
            if (options.HolsterStart && (weapon == null || weapon.Category != WeaponCategory.Pistol))
                throw new InvalidOperationException("HolsterStart: в кобуру второго оружия кладётся только пистолет (Rifle=false).");
            if (options.PurchaseAtSeconds >= 0f && purchase == null)
                throw new InvalidOperationException("Покупка по времени: PurchaseWeaponName не найден в BotCombatStand.Weapons.");
        }

        /// <summary>Снаряжение тела бота: начало case и новое тело после возрождения.</summary>
        public void Equip(PlayerSession bot, string why)
        {
            PlayerController avatar = bot != null ? bot.ActiveAvatar : null;
            BotGunner gunner = avatar != null ? avatar.GetComponent<BotGunner>() : null;
            if (gunner == null) throw new InvalidOperationException("Нет стрелка у тела бота (" + why + ").");

            if (_options.Unarmed)
            {
                // Кобура пуста (в Respawn стартовый пистолет не выдаётся) — Arm(null) ничего не найдёт: Unarmed.
                gunner.Arm(null);
                Note("equip", why, "unarmed", avatar);
                return;
            }

            if (_options.HolsterStart)
            {
                PlayerLoadoutManager loadout = avatar.GetComponent<PlayerLoadoutManager>();
                if (loadout == null || !loadout.ServerGiveWeapon(_weapon))
                    throw new InvalidOperationException("Пистолет '" + (_weapon != null ? _weapon.name : "?") + "' не лёг в кобуру (" + why + ").");
                gunner.Arm(null);
                Note("equip", why, "holster:" + _weapon.name, avatar);
                return;
            }

            if (_weapon != null) gunner.Arm(_weapon);
            Note("equip", why, "spawnAtHand:" + (_weapon != null ? _weapon.name : "-"), avatar);
        }

        /// <summary>Шаг сценария по времени case (сопрограмма стенда, ~10 Гц).</summary>
        public void Tick(PlayerSession bot, float elapsed)
        {
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority || bot == null) return;
            PlayerController avatar = bot.ActiveAvatar;
            if (avatar == null) return;

            if (!_purchased && _options.PurchaseAtSeconds >= 0f && elapsed >= _options.PurchaseAtSeconds && avatar.IsAlive)
            {
                _purchased = true;
                BotGunner gunner = avatar.GetComponent<BotGunner>();
                bool paid = BotShopper.TryPay(bot, _purchase);
                bool accepted = paid && gunner != null && gunner.OfferPurchase(_purchase);
                Note("purchase", _purchase.name, paid ? (accepted ? "accepted" : "rejected") : "notPaid", avatar);
            }

            if (!_killed && _options.KillAtSeconds >= 0f && elapsed >= _options.KillAtSeconds && avatar.IsAlive)
            {
                _killed = true;
                Note("kill", "ReceiveDamage", "health " + avatar.Health.ToString("F0"), avatar);
                // Штатная смерть: урон больше здоровья; смену тела и отпускание делает gameplay (deferred-body).
                avatar._actor.ReceiveDamage(avatar.Health + 1000f);
            }

            if (!_respawned && _options.RespawnAtSeconds >= 0f && elapsed >= _options.RespawnAtSeconds && !avatar.IsAlive)
            {
                _respawned = true;
                Note("respawn", "PlayerController.Respawn", "from " + avatar.name, avatar);
                avatar.Respawn();
                PlayerController body = bot.ActiveAvatar;
                if (body != null && body != avatar && body.IsAlive) Equip(bot, "новое тело");
                else Note("respawn", "noNewBody", body != null ? body.name : "null", body);
            }
        }

        private void Note(string kind, string what, string detail, PlayerController avatar)
        {
            if (_log.Count >= MaxLog) return;
            _log.Add(new Dictionary<string, object>
            {
                ["event"] = kind, ["what"] = what, ["detail"] = detail, ["time"] = Time.time - _start, ["frame"] = Time.frameCount,
                ["body"] = avatar != null ? avatar.GetInstanceID() : 0, ["alive"] = avatar != null && avatar.IsAlive
            });
        }
    }
}
