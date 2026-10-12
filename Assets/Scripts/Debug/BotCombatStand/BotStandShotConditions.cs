using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Условия выстрела для эталонных прогонов стенда: <see cref="BotStandRunOptions.ZeroAiAimSpread"/> и
    /// <see cref="BotStandRunOptions.NoRecoil"/>. Действует только на ботов этого стенда и только на экземпляры их оружия;
    /// префабы, код weapon-system и SDK не меняются. Каждый кадр до стадий UXR (<c>UxrManager.AvatarsUpdating</c>):
    /// <list type="bullet">
    /// <item>разброс ИИ: <see cref="BotGunner.ZeroAimSpread"/>;</item>
    /// <item>отдача: на корневой grabbable выбранного оружия вешается <see cref="BotStandNoRecoilPolicy"/> (корневая отдача
    /// SDK — поля триггера _recoilAngle*/_recoilOffset*/_recoilDurationSeconds — не применяется), <c>RecoilAccumulator</c>
    /// экземпляра выключается.</item>
    /// </list>
    /// В отчёт пишет фактическое состояние: флаги, установлена ли политика до первого выстрела, включён ли накопитель,
    /// поля отдачи триггеров (чтение отражением только для отчёта) и наибольшую накопленную отдачу накопителя (с NoRecoil — 0).
    /// При Dispose снимает политику и возвращает накопитель.
    /// </summary>
    public sealed class BotStandShotConditions : IDisposable
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo TriggersField = typeof(UxrFirearmWeapon).GetField("_triggers", Private);
        private static readonly string[] TriggerRecoilFields =
            { "_recoilAngleOneHand", "_recoilAngleTwoHands", "_recoilOffsetOneHand", "_recoilOffsetTwoHands", "_recoilDurationSeconds" };

        private sealed class WeaponState
        {
            public UxrFirearmWeapon Firearm;
            public BotStandNoRecoilPolicy Policy;
            public RecoilAccumulator Accumulator;
            public bool AccumulatorWasEnabled;
            public int AppliedFrame = -1;
            public bool AppliedBeforeFirstShot;
            /// <summary>Выстрелы стрелка на момент выбора этого оружия (смена оружия в case: пистолет → покупка).</summary>
            public int ShotsWhenSelected;
            public float MaxAccumulatorPitch, MaxAccumulatorYaw;
            public object Triggers;
        }

        private readonly BotStandSession _session;
        private readonly bool _zeroSpread;
        private readonly bool _noRecoil;
        private readonly Dictionary<UxrFirearmWeapon, WeaponState> _weapons = new Dictionary<UxrFirearmWeapon, WeaponState>();
        private string _error;
        private bool _disposed;

        public BotStandShotConditions(BotStandSession session, BotStandRunOptions options)
        {
            _session = session;
            _zeroSpread = options != null && options.ZeroAiAimSpread;
            _noRecoil = options != null && options.NoRecoil;
            UxrManager.AvatarsUpdating += Apply;
        }

        private void Apply()
        {
            if (_disposed) return;
            try
            {
                foreach (var session in _session.OwnedBots)
                {
                    var player = session != null ? session.ActiveAvatar : null;
                    var gunner = player != null ? player.GetComponent<BotGunner>() : null;
                    if (gunner == null) continue;
                    gunner.ZeroAimSpread = _zeroSpread;
                    UxrFirearmWeapon firearm = gunner.SelectedFirearm;
                    if (firearm == null) continue;
                    if (!_weapons.TryGetValue(firearm, out WeaponState state))
                    {
                        state = new WeaponState { Firearm = firearm, Triggers = TriggersField?.GetValue(firearm), ShotsWhenSelected = gunner.ShotsFired };
                        _weapons[firearm] = state;
                    }
                    if (_noRecoil && state.AppliedFrame < 0) Suppress(state, gunner);
                    if (state.Accumulator == null) state.Accumulator = firearm.GetComponent<RecoilAccumulator>();
                    if (state.Accumulator != null && state.Accumulator.Pattern != null)
                    {
                        state.MaxAccumulatorPitch = Mathf.Max(state.MaxAccumulatorPitch,
                            Mathf.Abs(state.Accumulator.Pattern.Pitch(false)), Mathf.Abs(state.Accumulator.Pattern.Pitch(true)));
                        state.MaxAccumulatorYaw = Mathf.Max(state.MaxAccumulatorYaw,
                            Mathf.Abs(state.Accumulator.Pattern.Yaw(false)), Mathf.Abs(state.Accumulator.Pattern.Yaw(true)));
                    }
                }
            }
            catch (Exception e)
            {
                _error = e.Message;
                GameLog.Debug.Error("[BotStandShotConditions] " + e.Message);
            }
        }

        private static void Suppress(WeaponState state, BotGunner gunner)
        {
            UxrGrabbableObject root = RootGrabbable(state.Firearm);
            if (root == null) return;
            state.Policy = root.GetComponent<BotStandNoRecoilPolicy>();
            if (state.Policy == null) state.Policy = root.gameObject.AddComponent<BotStandNoRecoilPolicy>();
            state.Accumulator = state.Firearm.GetComponent<RecoilAccumulator>();
            if (state.Accumulator != null)
            {
                state.AccumulatorWasEnabled = state.Accumulator.enabled;
                state.Accumulator.enabled = false;
            }
            state.AppliedFrame = Time.frameCount;
            // До первого выстрела именно этим оружием: выстрелы прежнего (пистолет до покупки) не считаются.
            state.AppliedBeforeFirstShot = gunner.ShotsFired == state.ShotsWhenSelected;
        }

        /// <summary>Корневой grabbable огнестрела по тому же правилу, что у SDK (RootGrabbable) и WeaponUseContext.</summary>
        private static UxrGrabbableObject RootGrabbable(UxrFirearmWeapon firearm)
        {
            if (!firearm.TryGetTriggerGrip(0, out UxrGrabbableObject grip, out _) || grip == null) return null;
            return grip.UsesGrabbableParentDependency && grip.GrabbableParent != null ? grip.GrabbableParent : grip;
        }

        /// <summary>Фактическое состояние условий в <c>result.Metrics["shotConditions"]</c>.</summary>
        public void WriteMetrics(BotStandCaseResult result)
        {
            var weapons = new List<object>();
            foreach (WeaponState state in _weapons.Values)
            {
                weapons.Add(new Dictionary<string, object>
                {
                    ["weapon"] = state.Firearm != null ? state.Firearm.name : null,
                    ["rootRecoilPolicyInstalled"] = state.Policy != null,
                    ["policyAppliedFrame"] = state.AppliedFrame,
                    ["policyAppliedBeforeFirstShot"] = state.AppliedBeforeFirstShot,
                    ["recoilAccumulatorPresent"] = state.Accumulator != null,
                    ["recoilAccumulatorEnabled"] = state.Accumulator != null && state.Accumulator.enabled,
                    ["accumulatorMaxPitchDeg"] = state.MaxAccumulatorPitch,
                    ["accumulatorMaxYawDeg"] = state.MaxAccumulatorYaw,
                    ["triggerRecoil"] = DescribeTriggers(state.Triggers)
                });
            }

            result.Metrics["shotConditions"] = new Dictionary<string, object>
            {
                ["zeroAiAimSpread"] = _zeroSpread,
                ["noRecoil"] = _noRecoil,
                ["weapons"] = weapons,
                ["error"] = _error,
                ["note"] = "NoRecoil: корневая отдача SDK запрещена политикой IUxrFirearmRootRecoilPolicy (SDK patch 70) на экземпляре — " +
                           "значения полей триггера при этом не меняются и выводятся справочно; RecoilAccumulator экземпляра выключен, его " +
                           "накопленная отдача должна оставаться 0. Без NoRecoil накопитель показывает фактическую отдачу."
            };
            if (_noRecoil && _weapons.Count > 0)
            {
                foreach (WeaponState state in _weapons.Values)
                {
                    if (state.Policy == null || !state.AppliedBeforeFirstShot || (state.Accumulator != null && state.Accumulator.enabled))
                    {
                        result.Findings.Add("NoRecoil не применён к оружию бота до первого выстрела: " + (state.Firearm != null ? state.Firearm.name : "?"));
                        break;
                    }
                }
            }
        }

        /// <summary>Поля отдачи триггеров (чтение отражением только для отчёта; управление от них не зависит).</summary>
        private static object DescribeTriggers(object triggers)
        {
            if (TriggersField == null) return "UxrFirearmWeapon._triggers не найден (SDK изменился) — значения не прочитаны";
            if (!(triggers is IEnumerable list)) return "нет триггеров";
            var rows = new List<object>();
            foreach (object item in list)
            {
                if (item == null) continue;
                // UxrFirearmTrigger внутренний в SDK: поля читаются отражением, только для отчёта.
                var row = new Dictionary<string, object>();
                foreach (string field in TriggerRecoilFields)
                {
                    FieldInfo info = item.GetType().GetField(field, Private);
                    object value = info != null ? info.GetValue(item) : "поле не найдено";
                    row[field] = value is Vector3 v ? new[] { v.x, v.y, v.z } : value;
                }
                rows.Add(row);
            }
            return rows;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UxrManager.AvatarsUpdating -= Apply;
            foreach (WeaponState state in _weapons.Values)
            {
                if (state.Policy != null) UnityEngine.Object.Destroy(state.Policy);
                if (state.Accumulator != null && state.AppliedFrame >= 0) state.Accumulator.enabled = state.AccumulatorWasEnabled;
            }
            // Флаг разброса — только на время прогона.
            foreach (var session in _session.OwnedBots)
            {
                var gunner = session != null && session.ActiveAvatar != null ? session.ActiveAvatar.GetComponent<BotGunner>() : null;
                if (gunner != null) gunner.ZeroAimSpread = false;
            }
            _weapons.Clear();
        }
    }
}
