using System;
using System.Text;
using UltimateXR.Core;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Network;
using VrBattlegrounds.Weapons.Core;
using VrBattlegrounds.Weapons.Sensors;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Порт учёта SDK (этап D, план п. 2.2): единственное место, где команды машины <see cref="LedgerCommand"/> становятся
    /// вызовами <c>UxrFirearmWeapon.Try*</c>, и владелец портов SDK (<c>CanAuthorReadinessAction</c>,
    /// <c>Validate*</c>, <c>RefreshPhysicalActionState</c>, <c>CanPrepareReadinessForAutomation</c>, <c>CanAcceptLedgerCorrection</c>).
    ///
    /// <para>
    /// <b>Физическое доказательство.</b> SDK перед фиксацией досылания/закрытия/покоя спрашивает игру, закрыт ли Action
    /// на самом деле. Порт отвечает «да» только внутри своей команды того же вида и той же ревизии, когда Action в покое
    /// по <see cref="WeaponMechanismRig"/>, — бывшие <c>ChamberCompletionEvidence</c> и одноразовый резерв контроллера.
    /// Вне команды порта (чужой вызов, повтор) доказательства нет.
    /// </para>
    /// <para>
    /// <b>До этапа E.</b> Выстрел решает спуск SDK (<c>ProcessReadinessLocalTrigger</c>), барьер приёма патрона поднимает
    /// <see cref="CartridgeIntake"/>: команды машины <c>Shoot</c> и <c>RequestAdmission</c> здесь не исполняются — машина
    /// получает их результат фиксацией учёта. Запасной щелчок SDK подавлен подпиской на <c>LocalTriggerAttemptDecided</c>:
    /// отклик даёт машина.
    /// </para>
    /// </summary>
    internal sealed class UxrReadinessLedgerPort
    {
        private readonly WeaponSystem _host;
        private readonly UxrFirearmWeapon _weapon;
        private readonly int _trigger;
        private readonly WeaponMechanismRig _rig;
        private readonly WeaponLedgerReader _reader;
        private Func<int, UxrGrabber, UxrFirearmTriggerDecision> _defaultEvaluate;
        private bool _installed, _initializing, _inFlight;
        private LedgerCommandKind _inFlightKind;
        private uint _inFlightRevision;

        public UxrReadinessLedgerPort(WeaponSystem host, UxrFirearmWeapon weapon, int trigger, WeaponMechanismRig rig, WeaponLedgerReader reader)
        {
            _host = host; _weapon = weapon; _trigger = trigger; _rig = rig; _reader = reader;
        }

        /// <summary>Выключение хоста: Cancel незавершённого цикла ещё разрешён (как прежний <c>_tearingDown</c>).</summary>
        public bool TearingDown { get; set; }

        /// <summary>Команды порта в работе: SDK-события, поднятые ею, — свои фиксации.</summary>
        public bool CommandInFlight => _inFlight || _initializing;

        public bool TryInstall(out string error)
        {
            error = null;
            if (_weapon == null) { error = "Нет UxrFirearmWeapon."; return false; }
            if (Foreign(_weapon.CanAuthorReadinessAction) || Foreign(_weapon.RefreshPhysicalActionState) ||
                Foreign(_weapon.ValidateChamberCompletion) || Foreign(_weapon.ValidatePhysicalActionClosed) ||
                Foreign(_weapon.ValidatePostShotEmptyActionRest) || Foreign(_weapon.CanPrepareReadinessForAutomation) ||
                Foreign(_weapon.CanAcceptLedgerCorrection))
            { error = "Порты учёта SDK уже принадлежат другому владельцу."; return false; }
            _weapon.CanAuthorReadinessAction = CanAuthor;
            _weapon.RefreshPhysicalActionState = Refresh;
            _weapon.ValidateChamberCompletion = ValidateChamberCompletion;
            _weapon.ValidatePhysicalActionClosed = ValidateClosed;
            _weapon.ValidatePostShotEmptyActionRest = ValidateEmptyRest;
            _weapon.CanPrepareReadinessForAutomation = CanPrepareAutomation;
            _weapon.CanAcceptLedgerCorrection = CanAcceptCorrection;
            // До этапа E классификация нажатия — запрос учёта SDK; сам отклик выдаёт машина.
            if (_weapon.EvaluateLocalTriggerAttempt == null)
            {
                _defaultEvaluate = (trigger, hand) => _weapon.QueryReadinessDecision(trigger);
                _weapon.EvaluateLocalTriggerAttempt = _defaultEvaluate;
            }
            _weapon.LocalTriggerAttemptDecided -= OnAttemptDecided;
            _weapon.LocalTriggerAttemptDecided += OnAttemptDecided;
            _installed = true;
            if (!_weapon.TryEnableReadiness(_trigger)) { error = "Спуск или гнездо магазина SDK ещё не готовы."; return false; }
            return true;
        }

        /// <summary>Снять порты, в которых нет смысла без хоста (автоматика бота, подписка на попытку спуска).</summary>
        public void Uninstall()
        {
            if (!_installed || _weapon == null) return;
            _weapon.LocalTriggerAttemptDecided -= OnAttemptDecided;
            if (ReferenceEquals(_weapon.CanPrepareReadinessForAutomation?.Target, this)) _weapon.CanPrepareReadinessForAutomation = null;
            _installed = false;
        }

        /// <summary>Исполнить команду машины. false — учёт отклонил (машина получит <c>CommandRejected</c>).</summary>
        public bool Execute(in LedgerCommand command)
        {
            uint revision = command.ExpectedRevision;
            switch (command.Kind)
            {
                case LedgerCommandKind.Initialize:
                {
                    bool accepted;
                    _initializing = true;
                    try { accepted = _weapon.TryInitializeReadiness(_trigger); }
                    finally { _initializing = false; }
                    LastInitializeRefusal = accepted ? null : DescribeInitializeRefusal();
                    return accepted;
                }
                case LedgerCommandKind.BeginAction:
                    return _weapon.TryBeginManualAction(_trigger, revision, command.CycleSequence);
                case LedgerCommandKind.Extract:
                    return _weapon.TryExtractChamberRound(_trigger, revision, command.CycleSequence);
                case LedgerCommandKind.Cancel:
                    return _weapon.TryCancelReadinessCycle(_trigger, revision);
                case LedgerCommandKind.CompleteChamber:
                {
                    UxrGrabbableObject magazine = _reader.CurrentAnchorMagazine();
                    if (Token(magazine) != command.ExpectedMagazineToken) return false;
                    return WithEvidence(command.Kind, revision, () => _weapon.TryCompleteChamber(_trigger, revision, magazine));
                }
                case LedgerCommandKind.CloseOnly:
                {
                    UxrGrabbableObject magazine = _reader.CurrentAnchorMagazine();
                    return WithEvidence(command.Kind, revision, () => _weapon.TryConfirmPhysicalActionClosed(_trigger, revision, magazine));
                }
                case LedgerCommandKind.AckEmptyRest:
                {
                    UxrGrabbableObject magazine = _reader.CurrentAnchorMagazine();
                    return WithEvidence(command.Kind, revision, () => _weapon.TryAcknowledgePostShotEmptyActionRest(_trigger, revision, magazine));
                }
                case LedgerCommandKind.RefillForAutomation:
                    if (_weapon.GetReadinessState(_trigger)?.Revision != revision) return false;
                    return WithEvidence(command.Kind, revision, () => _weapon.TryRefillAndPrepareForAutomation(_trigger));
                case LedgerCommandKind.Shoot:
                case LedgerCommandKind.RequestAdmission:
                    return true; // до этапа E: выстрел — спуск SDK, барьер — CartridgeIntake (см. класс)
                default:
                    return false;
            }
        }

        /// <summary>Видимые условия, не выполненные при последнем отказе Initialize (null — последний Initialize принят).</summary>
        public string LastInitializeRefusal { get; private set; }

        /// <summary>
        /// Какие из видимых снаружи условий <c>TryInitializeReadiness</c> не выполнены (SDK не меняется: только публичные
        /// признаки и наш порт <c>CanAuthorReadinessAction</c>). Строка собирается только при отказе.
        /// </summary>
        private string DescribeInitializeRefusal()
        {
            var failed = new StringBuilder();
            void Add(string what) { if (failed.Length > 0) failed.Append(", "); failed.Append(what); }
            if (!_weapon.UsesReadinessLedger(_trigger)) Add("учёт спуска не включён");
            if (!_weapon.isActiveAndEnabled) Add("ствол не isActiveAndEnabled");
            if (_weapon.IsUseBlocked) Add("CanUse=false: IsUseBlocked (упор ствола)");
            if (_weapon.Owner != null && _weapon.Owner.IsDead) Add($"CanUse=false: владелец {_weapon.Owner.name} мёртв");
            if (UltimateXR.Mechanics.Weapons.UxrWeaponManager.HasInstance && !UltimateXR.Mechanics.Weapons.UxrWeaponManager.Instance.WeaponSystemEnabled)
                Add("CanUse=false: UxrWeaponManager.WeaponSystemEnabled=false");
            if (!CanAuthor(_trigger))
            {
                Add("CanAuthorReadinessAction=false");
                if (_host == null || !_host.IsConfigured) Add("хост не настроен");
                else if (!_host.isActiveAndEnabled) Add("хост выключен");
                if (!_rig.IsPrepared) Add("механизм не подготовлен");
                else if (_rig.HasAction && !_rig.IsCurrent()) Add("механизм не текущий");
                if (!StateEventAuthority.IsAuthorOfItem(_weapon)) Add("не автор предмета (StateEventAuthority)");
            }
            if (!_weapon.TryGetTriggerMagazineAnchor(_trigger, out UxrGrabbableObjectAnchor anchor)) Add("нет гнезда магазина");
            else if (!anchor.isActiveAndEnabled) Add($"гнездо {anchor.name} не isActiveAndEnabled");
            if (UxrStateSyncImplementer.SyncCallDepth != 0) Add($"SyncCallDepth={UxrStateSyncImplementer.SyncCallDepth}");
            if (UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync) Add("внутри replay (IsInsideStateSync)");
            if (_weapon.GetReadinessState(_trigger)?.ReadinessInitialized == true) Add("учёт уже инициализирован");
            if (failed.Length == 0) Add("видимые условия выполнены (скрытые: приём патрона, фиксированный запас, повторная проверка покоя)");
            return failed.ToString();
        }

        private bool WithEvidence(LedgerCommandKind kind, uint revision, Func<bool> sdkCall)
        {
            _inFlight = true; _inFlightKind = kind; _inFlightRevision = revision;
            try { return sdkCall(); }
            finally { _inFlight = false; } // доказательство одноразовое и при отказе/исключении
        }

        // ── Порты SDK ───────────────────────────────────────────────────────────────────────

        private bool CanAuthor(int trigger) =>
            trigger == _trigger && _host != null && _host.IsConfigured && (_host.isActiveAndEnabled || TearingDown) && _rig.IsPrepared &&
            (!_rig.HasAction || _rig.IsCurrent()) && (!UxrManager.HasInstance || !UxrManager.Instance.IsInsideStateSync) &&
            StateEventAuthority.IsAuthorOfItem(_weapon);

        private void Refresh(int trigger)
        {
            if (trigger == _trigger && _host != null) _host.SampleBeforeShot();
        }

        private bool ValidateChamberCompletion(int trigger, uint revision, UxrGrabbableObject magazine)
        {
            if (trigger != _trigger || !CanAuthor(trigger) || !_rig.IsAtRest() || magazine != _reader.CurrentAnchorMagazine()) return false;
            if (_initializing)
            {
                UxrFirearmReadinessState initial = _weapon.GetReadinessState(trigger);
                return initial?.ReadinessInitialized != true && (initial?.Revision ?? 0) == revision;
            }
            return Evidence(LedgerCommandKind.CompleteChamber, revision) || Evidence(LedgerCommandKind.RefillForAutomation, revision);
        }

        private bool ValidateClosed(int trigger, uint revision, UxrGrabbableObject magazine) =>
            trigger == _trigger && CanAuthor(trigger) && _rig.IsAtRest() && magazine == _reader.CurrentAnchorMagazine() &&
            Evidence(LedgerCommandKind.CloseOnly, revision);

        private bool ValidateEmptyRest(int trigger, uint revision, UxrGrabbableObject magazine) =>
            trigger == _trigger && CanAuthor(trigger) && _rig.IsAtRest() && magazine == _reader.CurrentAnchorMagazine() &&
            Evidence(LedgerCommandKind.AckEmptyRest, revision);

        private bool CanPrepareAutomation(int trigger) =>
            trigger == _trigger && StateEventAuthority.IsWorldAuthority && Evidence(LedgerCommandKind.RefillForAutomation, _inFlightRevision);

        // Поправку учёта из сети принимает только клиент: сервер — арбитр порядка и сам её публикует (п. 3a, В-Л6).
        private bool CanAcceptCorrection(int trigger) => trigger == _trigger && _host != null && _host.IsConfigured && !Mirror.NetworkServer.active;

        private bool Evidence(LedgerCommandKind kind, uint revision) => _inFlight && _inFlightKind == kind && _inFlightRevision == revision;

        private static void OnAttemptDecided(UxrFirearmLocalTriggerAttempt attempt)
        {
            // Пусто намеренно: подписчик гасит запасной щелчок SDK. Отклик на попытку выдаёт машина (T53/T57).
        }

        private bool Foreign(Delegate current) => current != null && !ReferenceEquals(current.Target, this);

        private static int Token(UxrGrabbableObject magazine) => magazine != null ? WeaponLedgerReader.Token(magazine.UniqueId) : 0;
    }
}
