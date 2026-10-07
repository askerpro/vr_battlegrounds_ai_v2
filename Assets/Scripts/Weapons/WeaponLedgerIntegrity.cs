using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Целостность учёта патронов одного ствола (этап C2, SDK-патч 53; <c>Docs/tasks/weapon-ledger-single-source.md</c>, п. 3a).
    /// Игровой владелец двух SDK-событий: <c>ReadinessReplayDiverged</c> и <c>ReadinessFaulted</c>.
    ///
    /// <para>
    /// <b>Что делает.</b> Пишет <c>GameLog.WeaponSystem.Error</c> с данными инцидента (раз в 10 с на вид, со счётчиком
    /// повторов) и на сервере публикует поправку учёта после форка ревизии. Стрельбу не блокирует ничто: расхождение —
    /// непредвиденная ситуация, причину устраняют по логу, а игроку игра не ломается (решение 2026-10-07).
    /// </para>
    ///
    /// <para>
    /// <b>Поправка — не второй писатель.</b> Сервер публикует только своё уже принятое состояние (учёт спуска и M
    /// упомянутых магазинов) и только при отказе replay. Вход один — событие форка. Публикация откладывается до
    /// <c>LateUpdate</c> контроллера: внутри replay событие не уходит в сеть. Клиент принимает поправку портом
    /// <c>CanAcceptLedgerCorrection</c> контроллера; сервер поправку из сети не принимает.
    /// </para>
    /// </summary>
    public sealed class WeaponLedgerIntegrity
    {
        private const float ReportInterval = 10f;
        private const string Tag = "[WeaponLedger]";

        /// <summary>Счётчики процесса — для диагностики и пробы (п. 7 анализа).</summary>
        public static int DeltaMismatchTotal { get; private set; }
        public static int ForkTotal { get; private set; }
        public static int CorrectionsPublished { get; private set; }
        public static int CorrectionsApplied { get; private set; }
        public static int NotificationFailures { get; private set; }

        private sealed class Throttle { public float Last = float.NegativeInfinity; public int Suppressed; }

        private readonly UxrFirearmWeapon _weapon;
        private readonly WeaponReadinessController _owner;
        private readonly Dictionary<string, Throttle> _throttle = new Dictionary<string, Throttle>();
        private readonly List<UxrGrabbableObject> _correctionMagazines = new List<UxrGrabbableObject>();
        private bool _correctionPending, _attached;

        public WeaponLedgerIntegrity(UxrFirearmWeapon weapon, WeaponReadinessController owner)
        {
            _weapon = weapon; _owner = owner;
        }

        public void Attach()
        {
            if (_attached || _weapon == null) return;
            _weapon.ReadinessReplayDiverged += HandleDiverged;
            _weapon.ReadinessFaulted += HandleFaulted;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached) return;
            if (_weapon != null)
            {
                _weapon.ReadinessReplayDiverged -= HandleDiverged;
                _weapon.ReadinessFaulted -= HandleFaulted;
            }
            _attached = false;
            _correctionPending = false;
            _correctionMagazines.Clear();
        }

        /// <summary>Сервер: опубликовать отложенную поправку. Вызывает контроллер с верхнего уровня (<c>LateUpdate</c>).</summary>
        public void PublishPendingCorrection()
        {
            if (!_correctionPending) return;
            _correctionPending = false;
            if (!NetworkServer.active || _weapon == null) { _correctionMagazines.Clear(); return; }
            int trigger = _owner.TriggerIndex;
            if (_weapon.TryGetTriggerMagazineAnchor(trigger, out UxrGrabbableObjectAnchor anchor) && anchor != null && anchor.CurrentPlacedObject != null)
                _correctionMagazines.Add(anchor.CurrentPlacedObject);
            bool published = _weapon.TryPublishLedgerCorrection(trigger, _correctionMagazines);
            UxrFirearmReadinessState state = _weapon.GetReadinessState(trigger);
            if (published) CorrectionsPublished++;
            Report("correction-published", (published ? "Сервер опубликовал поправку учёта" : "Сервер НЕ смог опубликовать поправку учёта") +
                $": ревизия {state?.Revision ?? 0}, магазинов {_correctionMagazines.Count}. {Context(trigger, false)}");
            _correctionMagazines.Clear();
        }

        private void HandleDiverged(UxrFirearmReplayDivergence d)
        {
            if (d == null || d.TriggerIndex != _owner.TriggerIndex) return;
            string what;
            bool correctionQueued = false;
            switch (d.Kind)
            {
                case UxrFirearmReplayDivergenceKind.DeltaMismatch:
                    DeltaMismatchTotal++;
                    what = "M получателя не сходится с дельтой операции — применено значение автора";
                    break;
                case UxrFirearmReplayDivergenceKind.RevisionFork:
                    ForkTotal++;
                    what = "форк ревизии — фиксация не применена";
                    if (NetworkServer.active)
                    {
                        if (d.Magazine != null && !_correctionMagazines.Contains(d.Magazine)) _correctionMagazines.Add(d.Magazine);
                        _correctionPending = correctionQueued = true;
                    }
                    break;
                default:
                    CorrectionsApplied++;
                    what = "применена поправка сервера";
                    break;
            }
            string rounds = d.Kind == UxrFirearmReplayDivergenceKind.CorrectionApplied
                ? $"M до поправки {d.ReceiverRoundsBefore}, по поправке {d.AuthorRoundsAfter}"
                : $"M ожидалось на получателе {Show(d.ReceiverExpectedRoundsAfter)} (до операции {Show(d.ReceiverRoundsBefore)}), у автора после {d.AuthorRoundsAfter}";
            Report(d.Kind.ToString(),
                $"Расхождение учёта: {what}. Операция {d.Operation}, магазин {Name(d.Magazine)} ({d.MagazineIdentity}); " +
                $"ревизии: ожидаемая {d.ExpectedRevision}, следующая {d.NextRevision}, локальная {d.LocalRevision}; {rounds}; " +
                $"replay={d.Replay}, поправка {(correctionQueued ? "поставлена в публикацию" : d.Kind == UxrFirearmReplayDivergenceKind.CorrectionApplied ? "применена" : "нет")}. " +
                Context(d.TriggerIndex, d.Replay));
        }

        // В-Л5: сбой уведомления после записи. Учёт записан и разослан, стрельба не блокируется.
        private void HandleFaulted(int trigger, UxrFirearmShotEmissionOutcome outcome, Exception failure)
        {
            if (trigger != _owner.TriggerIndex) return;
            NotificationFailures++;
            UxrFirearmReadinessState state = _weapon.GetReadinessState(trigger);
            Report("notification-failure",
                $"Сбой уведомления учёта (исход выстрела {outcome}): {failure?.GetType().Name}: {failure?.Message}. " +
                $"Учёт записан (ревизия {state?.Revision ?? 0}), стрельба не блокируется. {Context(trigger, false)}");
        }

        private void Report(string kind, string message)
        {
            if (!_throttle.TryGetValue(kind, out Throttle throttle)) _throttle[kind] = throttle = new Throttle();
            float now = Time.unscaledTime;
            if (now - throttle.Last < ReportInterval) { throttle.Suppressed++; return; }
            string repeats = throttle.Suppressed > 0 ? $" Повторов за прошлый период: {throttle.Suppressed}." : "";
            throttle.Last = now; throttle.Suppressed = 0;
            GameLog.WeaponSystem.Error($"{Tag} {message}{repeats}", _owner);
        }

        private string Context(int trigger, bool replay)
        {
            string role = NetworkServer.active ? (NetworkClient.active ? "хост" : "выделенный сервер") : NetworkClient.active ? "клиент" : "офлайн";
            bool author = StateEventAuthority.IsAuthorOfItem(_weapon);
            return $"Ствол {_weapon.name} ({Path(_weapon.transform)}, UniqueId {_weapon.UniqueId}), спуск {trigger}; " +
                   $"машина: {role}, {(author ? "автор" : "наблюдатель")}; держатель netId {HolderNetId(trigger)}; replay={replay}.";
        }

        private uint HolderNetId(int trigger)
        {
            if (!UxrGrabManager.HasInstance || !_weapon.TryGetTriggerGrip(trigger, out UxrGrabbableObject grip, out int point) ||
                !UxrGrabManager.Instance.GetGrabbingHand(grip, point, out UxrGrabber hand) || hand == null || hand.Avatar == null) return 0;
            NetworkIdentity identity = hand.Avatar.GetComponent<NetworkIdentity>();
            return identity != null ? identity.netId : 0;
        }

        private static string Show(int value) => value < 0 ? "—" : value.ToString();
        private static string Name(UnityEngine.Object value) => value != null ? value.name : "нет";

        private static string Path(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
    }
}
