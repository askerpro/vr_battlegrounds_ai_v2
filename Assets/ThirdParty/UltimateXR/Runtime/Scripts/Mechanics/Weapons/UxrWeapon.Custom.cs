using System;
using UltimateXR.Manipulation;
using UnityEngine;

namespace UltimateXR.Mechanics.Weapons
{
    public abstract partial class UxrWeapon
    {
        /// <summary>
        ///     VR Battlegrounds patch 15: запрет использования извне — ствол упёрт в геометрию
        ///     (<c>BarrelObstruction</c>). Учитывается в <see cref="CanUse" />, а через него — в
        ///     <c>UxrFirearmWeapon.TryToShootRound</c>: выстрела нет.
        /// </summary>
        public bool IsUseBlocked { get; set; }

        /// <summary>
        ///     Gets whether the weapon can be used. By default it is true if the owner is not dead and the global check
        ///     in <see cref="UxrWeaponManager" /> allows it.
        /// </summary>
        public virtual bool CanUse
        {
            get
            {
                // VR Battlegrounds patch 15
                if (IsUseBlocked)
                {
                    return false;
                }

                if (Owner != null && Owner.IsDead)
                {
                    return false;
                }

                if (UxrWeaponManager.HasInstance)
                {
                    if (!UxrWeaponManager.Instance.WeaponSystemEnabled)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }


    /// <summary>Неизменяемый local результат до внешнего policy command; не serializable ammo state.</summary>
    public readonly struct UxrFirearmLocalTriggerAttempt
    {
        public int TriggerIndex { get; }
        public UxrGrabber MainGrabber { get; }
        public UltimateXR.Core.UxrHandSide Side { get; }
        public uint PressSequence { get; }
        public uint Revision { get; }
        public UxrGrabbableObject Magazine { get; }
        public UxrGrabbableObjectAnchor Anchor { get; }
        public int MagazineRounds { get; }
        public bool ChamberRound { get; }
        public UxrFirearmTriggerDecision Decision { get; }
        public int PolicyId { get; }
        internal UxrFirearmLocalTriggerAttempt(int trigger, UxrGrabber hand, uint press,
            UxrFirearmReadinessState state, UxrGrabbableObject magazine, UxrGrabbableObjectAnchor anchor,
            int rounds, UxrFirearmTriggerDecision decision, int policyId)
        {
            TriggerIndex=trigger; MainGrabber=hand; Side=hand.Side; PressSequence=press;
            Revision=state.Revision; Magazine=magazine; Anchor=anchor; MagazineRounds=rounds;
            ChamberRound=state.ChamberRound; Decision=decision; PolicyId=policyId;
        }
    }


    public partial class UxrFirearmWeapon
    {
        // VR Battlegrounds patch 15: сведения о спуске без рефлексии (UxrFirearmTrigger — internal).

        /// <summary>Число спусков оружия.</summary>
        public int TriggerCount => _triggers != null ? _triggers.Count : 0;

        /// <summary>VR Battlegrounds: текущий якорь магазина спуска без изменения состояния SDK.</summary>
        public bool TryGetTriggerMagazineAnchor(int triggerIndex, out UxrGrabbableObjectAnchor anchor)
        {
            anchor = triggerIndex >= 0 && triggerIndex < TriggerCount ? _triggers[triggerIndex].AmmunitionMagAnchor : null;
            return anchor != null;
        }

        /// <summary>Локальное нажатие спуска отклонено: в магазине есть патроны, но нужен ручной цикл.</summary>
        public event Action<int, UxrGrabber> ChamberingRequired;

        /// <summary>VR Battlegrounds: состояние механики без ввода и без изменения боезапаса.</summary>
        public bool NeedsManualChambering(int triggerIndex)
        {
            if (UsesReadinessLedger(triggerIndex))
            {
                var decision = QueryReadinessDecision(triggerIndex);
                return decision.Kind == UxrFirearmTriggerDecisionKind.NotReady && decision.Reason == UxrFirearmNotReadyReason.ChamberingRequired;
            }
            if (triggerIndex < 0 || triggerIndex >= TriggerCount ||
                !_runtimeTriggers.TryGetValue(triggerIndex, out RuntimeTriggerInfo runtime) ||
                !HasMagAttached(triggerIndex) || GetAmmoLeft(triggerIndex) <= 0) return false;

            UxrFirearmTrigger trigger = _triggers[triggerIndex];
            return !runtime.HasReloaded &&
                   (trigger.CycleType == UxrShotCycle.ManualReload || trigger.UseHasReloadedForSemiAndFullAuto);
        }

        // ── VR Battlegrounds patch 23: один источник выстрела ─────────────────────
        //
        // Выстрел решает только машина стрелка (TryToShootRound → синхронизируемый Shoot). Остальные
        // получают Shoot событием и по нему же (UxrProjectileSource.ShotFired) играют звук, отдачу и
        // тратят патрон копии — ProjectileShotReplayed. ProjectileShot поднимается только у стрелка:
        // подписчик, который сам стреляет (дробь), по построению не выстрелит на копии.

        /// <summary>
        ///     Выстрел другой машины повторён на этой копии оружия: звук и отдача уже сыграны.
        ///     Для косметики наблюдателя; <see cref="ProjectileShot" /> здесь не поднимается.
        /// </summary>
        public event Action<int> ProjectileShotReplayed;

        /// <summary>Идёт свой выстрел (<see cref="TryToShootRound" />) — его эффекты играются там, повтор не нужен.</summary>
        private bool _shootingLocally;

        private UxrProjectileSource _replaySource;

        private void SubscribeShotReplay(bool subscribe)
        {
            if (subscribe)
            {
                _replaySource = GetCachedComponent<UxrProjectileSource>();
                if (_replaySource != null) _replaySource.ShotFired += Source_ShotFired;
            }
            else if (_replaySource != null)
            {
                _replaySource.ShotFired -= Source_ShotFired;
                _replaySource = null;
            }
        }

        /// <summary>
        ///     Источник выстрелил. Свой выстрел пропускается; чужой (повтор события по сети) — эффекты
        ///     спуска, чей это тип выстрела. Дробинки — другой тип выстрела, на них ничего не играется.
        /// </summary>
        private void Source_ShotFired(int shotTypeIndex)
        {
            if (_shootingLocally)
            {
                return;
            }

            for (int i = 0; i < _triggers.Count; i++)
            {
                UxrFirearmTrigger trigger = _triggers[i];

                if (trigger.ProjectileShotIndex != shotTypeIndex || !_runtimeTriggers.TryGetValue(i, out RuntimeTriggerInfo runtimeTrigger))
                {
                    continue;
                }

                // Патрон копии: Rounds магазина не синхронизируется, пусть копия хотя бы не расходится с выстрелами.
                int ammo = GetAmmoLeft(i);
                if (!UsesReadinessLedger(i) && ammo > 0 && ammo != int.MaxValue)
                {
                    SetAmmoLeft(i, ammo - 1);
                }

                runtimeTrigger.RecoilTimer = trigger.RecoilDurationSeconds;
                trigger.ShotAudio?.Play(_replaySource.GetShotOrigin(shotTypeIndex));
                ProjectileShotReplayed?.Invoke(i);
            }
        }

        /// <summary>За какой граббабл и какую точку хвата держат спуск.</summary>
        public bool TryGetTriggerGrip(int triggerIndex, out UxrGrabbableObject grabbable, out int grabPoint)
        {
            grabbable = null;
            grabPoint = 0;
            if (triggerIndex < 0 || triggerIndex >= TriggerCount) return false;

            grabbable = _triggers[triggerIndex].TriggerGrabbable;
            grabPoint = _triggers[triggerIndex].GrabbableGrabPointIndex;
            return grabbable != null;
        }

        /// <summary>Индекс типа выстрела <c>UxrProjectileSource</c>, которым стреляет спуск.</summary>
        public int GetTriggerShotIndex(int triggerIndex) =>
            triggerIndex >= 0 && triggerIndex < TriggerCount ? _triggers[triggerIndex].ProjectileShotIndex : 0;

        /// <summary>Звук пустого спуска («нет патронов») в точке.</summary>
        public void PlayTriggerNoAmmoSound(int triggerIndex, Vector3 position)
        {
            if (triggerIndex >= 0 && triggerIndex < TriggerCount)
            {
                _triggers[triggerIndex].ShotAudioNoAmmo?.Play(position);
            }
        }
        public Action<int, uint> PrepareLocalTriggerAttempt { get; set; }
        public Func<int, int> CaptureLocalTriggerPolicyId { get; set; }
        public event Action<UxrFirearmLocalTriggerAttempt> LocalTriggerAttemptDecided;

        private sealed class LocalTriggerEpisode
        {
            public RuntimeTriggerInfo Runtime;
            public UxrGrabber Hand;
            public bool MustRelease=true, Accepted, Processing, WasPressed;
            public uint PressSequence;
        }
        private readonly System.Collections.Generic.Dictionary<int, LocalTriggerEpisode> _localTriggerEpisodes =
            new System.Collections.Generic.Dictionary<int, LocalTriggerEpisode>();

        private void ResetLocalTriggerEpisode(int index)
        {
            if(_localTriggerEpisodes.TryGetValue(index,out LocalTriggerEpisode episode))
            { episode.Accepted=false; episode.MustRelease=true; episode.WasPressed=false; }
        }

        private void ResetLocalTriggerEpisodes()
        {
            foreach(var episode in _localTriggerEpisodes.Values)
            {
                episode.Accepted=false; episode.MustRelease=true; episode.WasPressed=false;
                episode.Runtime=null; episode.Hand=null;
            }
        }

        private bool HasLocalTriggerContext(int index, UxrGrabber hand, RuntimeTriggerInfo runtime)
        {
            return isActiveAndEnabled && CanUse && !IsReadinessReplay && CanOwnReadiness(index) &&
                hand != null && hand.Avatar != null && hand.Avatar.AvatarMode == UltimateXR.Avatar.UxrAvatarMode.Local &&
                _runtimeTriggers.TryGetValue(index,out RuntimeTriggerInfo current) && ReferenceEquals(runtime,current) &&
                TryGetTriggerGrip(index,out UxrGrabbableObject grip,out int point) &&
                UxrGrabManager.HasInstance && UxrGrabManager.Instance.GetGrabbingHand(grip,point,out UxrGrabber main) && main==hand;
        }

        private bool IsLocalAttemptSnapshotCurrent(int index, UxrGrabber hand, RuntimeTriggerInfo runtime,
            UxrFirearmReadinessState before, UxrGrabbableObject magazine, UxrGrabbableObjectAnchor anchor, int rounds)
        {
            return HasLocalTriggerContext(index,hand,runtime) && before!=null && before.Equals(GetReadinessState(index)) &&
                TryGetTriggerMagazineAnchor(index,out UxrGrabbableObjectAnchor currentAnchor) && currentAnchor==anchor &&
                magazine==GetCurrentReadinessMagazine(index) && rounds==(magazine!=null?magazine.GetComponent<UxrFirearmMag>().Rounds:0);
        }

        // SDK — единственный владелец press/accepted episode. Game receiver не может принять удержанный спуск.
        private bool ProcessReadinessLocalTrigger(int index, UxrGrabber hand, RuntimeTriggerInfo runtime)
        {
            if (!_localTriggerEpisodes.TryGetValue(index,out LocalTriggerEpisode episode) ||
                !ReferenceEquals(episode.Runtime,runtime) || episode.Hand!=hand)
            {
                episode=new LocalTriggerEpisode {Runtime=runtime,Hand=hand,PressSequence=episode?.PressSequence ?? 0};
                _localTriggerEpisodes[index]=episode;
            }
            if(episode.Processing) return false;
            if(!HasLocalTriggerContext(index,hand,runtime)) { ResetLocalTriggerEpisode(index); return false; }
            bool pressed=runtime.TriggerPressed;
            if(!pressed || runtime.TriggerPressEnded)
            { episode.MustRelease=false; episode.Accepted=false; episode.WasPressed=false; return false; }
            bool fresh=runtime.TriggerPressStarted && !episode.WasPressed && !episode.MustRelease;
            episode.WasPressed=pressed;
            if(!fresh)
            {
                if(!episode.Accepted || episode.MustRelease || _triggers[index].CycleType!=UxrShotCycle.FullyAutomatic) return false;
                RefreshPhysicalActionState?.Invoke(index);
                if(!HasLocalTriggerContext(index,hand,runtime) || QueryReadinessDecision(index).Kind!=UxrFirearmTriggerDecisionKind.FireAllowed)
                { ResetLocalTriggerEpisode(index); return false; }
                return true;
            }

            // Любой fresh press уже потреблён до external classifier; reentry не может породить второй attempt.
            episode.Accepted=false; episode.MustRelease=true; episode.PressSequence++;
            episode.Processing=true;
            try
            {
                RefreshPhysicalActionState?.Invoke(index);
                if(!HasLocalTriggerContext(index,hand,runtime)) return false;
                // Ready принимает Auto episode даже во время cooldown; сам shot ограничивает SDK timer.
                // NotReady при ROF отказе не запускает подготовку и не публикует ammo feedback.
                if(runtime.LastShotTimer>0f && QueryReadinessDecision(index).Kind!=UxrFirearmTriggerDecisionKind.FireAllowed) return false;
                UxrFirearmReadinessState before=GetReadinessState(index);
                UxrGrabbableObject magazine=GetCurrentReadinessMagazine(index);
                TryGetTriggerMagazineAnchor(index,out UxrGrabbableObjectAnchor anchor);
                int rounds=magazine!=null?magazine.GetComponent<UxrFirearmMag>().Rounds:0;
                UxrFirearmTriggerDecision decision=EvaluateLocalTriggerAttempt!=null?
                    EvaluateLocalTriggerAttempt(index,hand):new UxrFirearmTriggerDecision(UxrFirearmTriggerDecisionKind.OtherDenied);
                if(!IsLocalAttemptSnapshotCurrent(index,hand,runtime,before,magazine,anchor,rounds)) return false;
                if(decision.Kind==UxrFirearmTriggerDecisionKind.FireAllowed && IsReadyToFire(index))
                { episode.Accepted=true; episode.MustRelease=false; return true; }
                if(decision.Kind!=UxrFirearmTriggerDecisionKind.NotReady &&
                    decision.Kind!=UxrFirearmTriggerDecisionKind.PrepareOnlyConsumed) return false;
                if(!Enum.IsDefined(typeof(UxrFirearmNotReadyReason),decision.Reason)) return false;
                int policyId=CaptureLocalTriggerPolicyId?.Invoke(index) ?? -1;
                if(!IsLocalAttemptSnapshotCurrent(index,hand,runtime,before,magazine,anchor,rounds)) return false;
                var attempt=new UxrFirearmLocalTriggerAttempt(index,hand,episode.PressSequence,before,magazine,anchor,rounds,decision,policyId);
                if(decision.Kind==UxrFirearmTriggerDecisionKind.PrepareOnlyConsumed)
                    PrepareLocalTriggerAttempt?.Invoke(index,episode.PressSequence);
                if(!HasLocalTriggerContext(index,hand,runtime)) return false;
                if(LocalTriggerAttemptDecided!=null) LocalTriggerAttemptDecided.Invoke(attempt);
                else
                {
                    if(decision.Reason==UxrFirearmNotReadyReason.ChamberingRequired) ChamberingRequired?.Invoke(index,hand);
                    PlayTriggerNoAmmoSound(index,GetTriggerNoAmmoSoundPosition(index,hand));
                }
                return false; // Inline Ready не изменяет consumed текущего press.
            }
            finally { episode.Processing=false; }
        }

        /// <summary>Исходная позиция dry audio SDK, без воспроизведения и изменения состояния.</summary>
        public Vector3 GetTriggerNoAmmoSoundPosition(int index, UxrGrabber hand)
        {
            if(index<0 || index>=TriggerCount) return transform.position;
            var trigger=_triggers[index];
            if(trigger.TriggerTransform!=null) return trigger.TriggerTransform.position;
            return trigger.TriggerGrabbable!=null && hand!=null ?
                trigger.TriggerGrabbable.GetGrabPointGrabProximityTransform(hand,trigger.GrabbableGrabPointIndex).position : transform.position;
        }


    }
}
