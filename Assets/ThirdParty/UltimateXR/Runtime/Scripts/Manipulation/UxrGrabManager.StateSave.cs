// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrGrabManager.StateSave.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using UltimateXR.Core;
using UltimateXR.Core.Components;
using UltimateXR.Core.Settings;
using UltimateXR.Core.StateSave;
using UltimateXR.Extensions.Unity; // VR Battlegrounds patch 75: ApplyAlignment
using UnityEngine;

namespace UltimateXR.Manipulation
{
    public partial class UxrGrabManager
    {
        #region Protected Overrides UxrComponent

        /// <inheritdoc />
        protected override void SerializeState(bool isReading, int stateSerializationVersion, UxrStateSaveLevel level, UxrStateSaveOptions options)
        {
            bool restoreGrabState = isReading && level > UxrStateSaveLevel.ChangesSincePreviousSave;
            if (restoreGrabState) UxrGrabber.BeginGrabStateMutation();
            try
            {
                if (restoreGrabState) AdvanceCurrentGrabLifetimes(); // VR Battlegrounds patch 71: старые записи до base/ref write.
                base.SerializeState(isReading, stateSerializationVersion, level, options);

                // Manipulations are already handled through events, we don't serialize them in incremental changes

                if (level > UxrStateSaveLevel.ChangesSincePreviousSave)
                {
                    // We don't want to compare dictionaries, we save the manipulations info always by using null as name to avoid overhead.
                    SerializeStateValue(level, options, null, ref _currentManipulations);
                }
            }
            finally
            {
                if (restoreGrabState)
                {
                    // VR Battlegrounds patch 71: восстановленные/частично прочитанные записи; тот же payload.
                    try { AdvanceCurrentGrabLifetimes(); }
                    finally { UxrGrabber.EndGrabStateMutation(); }
                }
            }
        }

        #endregion

        #region Internal Methods (VR Battlegrounds patch 75)

        /// <summary>
        ///     VR Battlegrounds patch 75: согласует размещение предмета после чтения его снимка
        ///     (<c>ChangesSinceBeginning</c>/<c>Complete</c>). Чтение присваивает только <c>_currentAnchor</c>;
        ///     здесь предмет ставится в якорь так же, как <see cref="PlaceObject" /> без анимации, либо снимается
        ///     со старого якоря. Событий и синхронизации нет: состояние уже серверное, его лишь доводят до вида.
        /// </summary>
        /// <param name="grabbableObject">Прочитанный предмет</param>
        /// <param name="anchorBeforeLoad">Якорь предмета до чтения</param>
        /// <param name="unresolvedAnchorId">
        ///     Id якоря из снимка, которого у этой машины ещё нет (аватар появится позже), или <see cref="Guid.Empty" />
        /// </param>
        internal void ReconcileLoadedPlacement(UxrGrabbableObject grabbableObject, UxrGrabbableObjectAnchor anchorBeforeLoad, Guid unresolvedAnchorId)
        {
            if (grabbableObject == null)
            {
                return;
            }

            // Новый снимок предмета отменяет прежнее ожидание его якоря.
            ForgetPendingPlacement(grabbableObject);

            UxrGrabbableObjectAnchor anchor = grabbableObject.CurrentAnchor;

            if (anchor != null)
            {
                ApplyLoadedPlacement(grabbableObject, anchor, anchorBeforeLoad);
            }
            else if (anchorBeforeLoad != null)
            {
                DetachLoadedPlacement(grabbableObject, anchorBeforeLoad);
            }

            if (unresolvedAnchorId != Guid.Empty)
            {
                AddPendingPlacement(unresolvedAnchorId, grabbableObject);
            }
        }

        /// <summary>
        ///     VR Battlegrounds patch 75: согласует якорь после чтения <c>_currentPlacedObject</c>. Порядок
        ///     предметов и якорей в снимке произвольный, поэтому обе стороны приводят пару к одному виду; значения
        ///     обеих пишутся всегда, и тот, кто прочитан позже, повторяет то же размещение.
        /// </summary>
        /// <param name="anchor">Прочитанный якорь</param>
        /// <param name="placedBeforeLoad">Предмет в якоре до чтения</param>
        /// <param name="unresolvedPlacedId">Id предмета из снимка, которого у этой машины ещё нет, или <see cref="Guid.Empty" /></param>
        internal void ReconcileLoadedAnchor(UxrGrabbableObjectAnchor anchor, UxrGrabbableObject placedBeforeLoad, Guid unresolvedPlacedId)
        {
            if (anchor == null)
            {
                return;
            }

            if (unresolvedPlacedId != Guid.Empty && UxrGlobalSettings.Instance.LogLevelManipulation >= UxrLogLevel.Warnings)
            {
                // Отложить нельзя: предмет может зарегистрироваться до своего Awake, который перезапишет CurrentAnchor.
                // Свой снимок предмета (если придёт) поставит его в якорь с предметной стороны.
                Debug.LogWarning($"{UxrConstants.ManipulationModule} State load: anchor {anchor.name} holds object {unresolvedPlacedId} that is not registered on this machine; anchor left empty (patch 75).");
            }

            UxrGrabbableObject placed = anchor.CurrentPlacedObject;

            if (placedBeforeLoad != null && placedBeforeLoad != placed && placedBeforeLoad.CurrentAnchor == anchor)
            {
                // Прежний предмет ещё считает себя здесь: он либо прочитается позже и встанет по своим данным,
                // либо в снимке его нет, и тогда в якоре его нет и на сервере.
                placedBeforeLoad.CurrentAnchor = null;
                DetachLoadedPlacement(placedBeforeLoad, anchor);
            }

            if (placed != null)
            {
                ApplyLoadedPlacement(placed, anchor, placed.CurrentAnchor);
            }
        }

        #endregion

        #region Private Methods (VR Battlegrounds patch 75)

        /// <summary>
        ///     VR Battlegrounds patch 75: взаимные ссылки, kinematic, родитель и выравнивание размещённого предмета —
        ///     те же шаги, что у <see cref="PlaceObject" /> с мгновенным размещением, без событий и без
        ///     <c>BeginSync</c>. Повторный вызов для уже размещённого предмета ничего не меняет.
        /// </summary>
        private void ApplyLoadedPlacement(UxrGrabbableObject grabbableObject, UxrGrabbableObjectAnchor anchor, UxrGrabbableObjectAnchor previousAnchor)
        {
            UxrGrabbableObject displaced = anchor.CurrentPlacedObject;
            bool               restored  = displaced != grabbableObject || previousAnchor != anchor ||
                                           (grabbableObject.UseParenting && grabbableObject.transform.parent != anchor.AlignTransform);

            if (displaced != null && displaced != grabbableObject && displaced.CurrentAnchor == anchor)
            {
                // Вытесненный предмет прочитается по своим данным; ссылку на чужой якорь он не держит.
                displaced.CurrentAnchor = null;
            }

            if (previousAnchor != null && previousAnchor != anchor && previousAnchor.CurrentPlacedObject == grabbableObject)
            {
                previousAnchor.CurrentPlacedObject = null;
            }

            grabbableObject.CurrentAnchor = anchor;
            anchor.CurrentPlacedObject    = grabbableObject;

            if (grabbableObject.RigidBodySource != null)
            {
                grabbableObject.RigidBodySource.isKinematic = true;
            }

            if (grabbableObject.UseParenting && grabbableObject.transform.parent != anchor.AlignTransform)
            {
                ChangeGrabbableObjectParent(grabbableObject, anchor.AlignTransform);
            }

            grabbableObject.StopSmoothAnchorPlacement();
            grabbableObject.transform.ApplyAlignment(grabbableObject.DropAlignTransform,
                                                     anchor.AlignTransform,
                                                     UxrUtils.BuildTransformations(UxrGrabbableObject.GetSnapModeAffectsPosition(grabbableObject.DropSnapMode),
                                                                                   UxrGrabbableObject.GetSnapModeAffectsRotation(grabbableObject.DropSnapMode)));

            grabbableObject.LocalPositionBeforeUpdate = grabbableObject.transform.localPosition;
            grabbableObject.LocalRotationBeforeUpdate = grabbableObject.transform.localRotation;

            if (anchor.ActivateOnPlaced)
            {
                anchor.ActivateOnPlaced.SetActive(true);
            }

            if (anchor.ActivateOnEmpty)
            {
                anchor.ActivateOnEmpty.SetActive(false);
            }

            if (anchor.ActivateOnCompatibleNear != null)
            {
                anchor.ActivateOnCompatibleNear.SetActive(false);
            }

            if (anchor.ActivateOnCompatibleNotNear != null)
            {
                anchor.ActivateOnCompatibleNotNear.SetActive(false);
            }

            if (restored && UxrGlobalSettings.Instance.LogLevelManipulation >= UxrLogLevel.Relevant)
            {
                Transform parent = grabbableObject.transform.parent;
                Debug.Log($"{UxrConstants.ManipulationModule} State load placed {grabbableObject.name} on anchor {anchor.name} (patch 75, parent {(parent != null ? parent.name : "null")}).");
            }
        }

        /// <summary>
        ///     VR Battlegrounds patch 75: снимает с якоря предмет, которого там больше нет. Ссылка якоря очищается,
        ///     только если указывает на этот предмет. Родитель меняется, только если предмет всё ещё висит на
        ///     якоре: если снимок уже переставил его родителя, его не трогают. Kinematic — значение снимка.
        /// </summary>
        private void DetachLoadedPlacement(UxrGrabbableObject grabbableObject, UxrGrabbableObjectAnchor anchor)
        {
            if (anchor.CurrentPlacedObject == grabbableObject)
            {
                anchor.CurrentPlacedObject = null;
            }

            grabbableObject.StopSmoothAnchorPlacement();

            if (grabbableObject.CurrentAnchor == null && grabbableObject.UseParenting && !IsBeingGrabbed(grabbableObject) &&
                grabbableObject.transform.parent == anchor.AlignTransform)
            {
                ChangeGrabbableObjectParent(grabbableObject, anchor.transform.parent);
                grabbableObject.LocalPositionBeforeUpdate = grabbableObject.transform.localPosition;
                grabbableObject.LocalRotationBeforeUpdate = grabbableObject.transform.localRotation;
            }

            if (UxrGlobalSettings.Instance.LogLevelManipulation >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.ManipulationModule} State load removed {grabbableObject.name} from anchor {anchor.name} (patch 75).");
            }
        }

        /// <summary>
        ///     VR Battlegrounds patch 75: запоминает предмет, чей якорь из снимка ещё не зарегистрирован (аватар или бот
        ///     появился у этой машины позже снимка; id якоря — через <c>CombineUniqueId</c> при спавне аватара).
        ///     Это не копия grab-state: только неразрешённая ссылка до появления якоря. Подписка на штатное
        ///     <see cref="UxrComponent.GlobalRegistered" /> — только пока есть ожидание.
        /// </summary>
        private void AddPendingPlacement(Guid anchorId, UxrGrabbableObject grabbableObject)
        {
            _pendingAnchorPlacements[anchorId] = grabbableObject;

            if (!_pendingRegistrationSubscribed)
            {
                UxrComponent.GlobalRegistered += PendingPlacement_ComponentRegistered;
                _pendingRegistrationSubscribed = true;
            }

            if (UxrGlobalSettings.Instance.LogLevelManipulation >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.ManipulationModule} State load: anchor {anchorId} of {grabbableObject.name} is not registered on this machine yet; placement deferred until it registers (patch 75).");
            }
        }

        /// <summary>
        ///     VR Battlegrounds patch 75: снимает ожидание якоря для предмета (его новый снимок уже прочитан).
        /// </summary>
        private void ForgetPendingPlacement(UxrGrabbableObject grabbableObject)
        {
            if (_pendingAnchorPlacements.Count == 0)
            {
                return;
            }

            Guid stale = Guid.Empty;

            foreach (KeyValuePair<Guid, UxrGrabbableObject> pair in _pendingAnchorPlacements)
            {
                if (pair.Value == grabbableObject)
                {
                    stale = pair.Key;
                    break;
                }
            }

            if (stale != Guid.Empty)
            {
                _pendingAnchorPlacements.Remove(stale);
            }

            UnsubscribeIfNoPendingPlacements();
        }

        /// <summary>
        ///     VR Battlegrounds patch 75: якорь зарегистрировался под ожидаемым id — довести отложенное размещение.
        ///     Если предмет за это время взяли или положили событием, ожидание устарело и снимается без размещения.
        /// </summary>
        private void PendingPlacement_ComponentRegistered(UxrComponent component)
        {
            if (this == null)
            {
                UxrComponent.GlobalRegistered -= PendingPlacement_ComponentRegistered;
                return;
            }

            if (component is UxrGrabbableObjectAnchor anchor && _pendingAnchorPlacements.TryGetValue(anchor.UniqueId, out UxrGrabbableObject grabbableObject))
            {
                _pendingAnchorPlacements.Remove(anchor.UniqueId);

                if (grabbableObject != null && grabbableObject.CurrentAnchor == null && !IsBeingGrabbed(grabbableObject) &&
                    (anchor.CurrentPlacedObject == null || anchor.CurrentPlacedObject == grabbableObject))
                {
                    ApplyLoadedPlacement(grabbableObject, anchor, null);
                }
                else if (UxrGlobalSettings.Instance.LogLevelManipulation >= UxrLogLevel.Warnings)
                {
                    Debug.LogWarning($"{UxrConstants.ManipulationModule} State load: deferred placement on anchor {anchor.name} ({anchor.UniqueId}) dropped, object changed meanwhile (patch 75).");
                }

                UnsubscribeIfNoPendingPlacements();
            }
        }

        /// <summary>
        ///     VR Battlegrounds patch 75: без ожиданий подписки нет.
        /// </summary>
        private void UnsubscribeIfNoPendingPlacements()
        {
            if (_pendingRegistrationSubscribed && _pendingAnchorPlacements.Count == 0)
            {
                UxrComponent.GlobalRegistered -= PendingPlacement_ComponentRegistered;
                _pendingRegistrationSubscribed = false;
            }
        }

        #endregion

        #region Private Types & Data (VR Battlegrounds patch 75)

        // Неразрешённые ссылки снимка: id якоря → предмет, ждущий его регистрации.
        private readonly Dictionary<Guid, UxrGrabbableObject> _pendingAnchorPlacements = new Dictionary<Guid, UxrGrabbableObject>();
        private          bool                                 _pendingRegistrationSubscribed;

        #endregion
    }
}