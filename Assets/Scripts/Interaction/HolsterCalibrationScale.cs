using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Сохраняет мировой размер оружия в кобурах при калибровке роста.
    /// Подписчик контракта calibration-scale-events r1 на каждой машине, включая сервер:
    /// SDK-якорь и grab-state не меняются, корректируется только локальный масштаб предмета.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HolsterCalibrationScale : MonoBehaviour
    {
        private readonly struct StoredItem
        {
            public readonly UxrGrabbableObjectAnchor Anchor;
            public readonly UxrGrabbableObject Item;
            public readonly Transform Parent;
            public readonly Vector3 LocalScale;

            public StoredItem(UxrGrabbableObjectAnchor anchor, UxrGrabbableObject item)
            {
                Anchor = anchor;
                Item = item;
                Parent = item.transform.parent;
                LocalScale = item.transform.localScale;
            }
        }

        private readonly List<UxrGrabbableObjectAnchor> _anchors = new List<UxrGrabbableObjectAnchor>();
        private readonly List<StoredItem> _stored = new List<StoredItem>();
        private UxrAvatar _avatar;
        private AvatarCalibrationApplier _applier;
        private Transform _skeleton;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // Повторный вход в Play без domain reload не должен удваивать подписку.
            UxrAvatar.GlobalEnabled -= OnAvatarEnabled;
            UxrAvatar.GlobalEnabled += OnAvatarEnabled;
        }

        private static void OnAvatarEnabled(UxrAvatar avatar)
        {
            if (!Application.isPlaying || avatar == null) return;
            if (avatar.GetComponent<HolsterCalibrationScale>() == null)
                avatar.gameObject.AddComponent<HolsterCalibrationScale>();
        }

        private void OnEnable()
        {
            _avatar = GetComponent<UxrAvatar>();
            _applier = AvatarCalibrationApplier.For(_avatar);
            if (_applier == null) return;

            _applier.ScaleChanging += OnScaleChanging;
            _applier.ScaleChanged += OnScaleChanged;
        }

        private void OnDisable()
        {
            if (_applier != null)
            {
                _applier.ScaleChanging -= OnScaleChanging;
                _applier.ScaleChanged -= OnScaleChanged;
            }

            _applier = null;
            _skeleton = null;
            _anchors.Clear();
            _stored.Clear();
        }

        private void OnScaleChanging(AvatarCalibrationApplier applier, float oldScale, float newScale)
        {
            _stored.Clear();
            _skeleton = transform.Find(AvatarCalibrationApplier.DummyForwardName);
            if (_skeleton == null) return;

            // Только при калибровке: новые/выключенные якоря тоже видны, покадрового опроса нет.
            _anchors.Clear();
            GetComponentsInChildren(true, _anchors);
            foreach (UxrGrabbableObjectAnchor anchor in _anchors)
            {
                AnchorRoleKind role = AnchorRole.Get(anchor);
                if (role != AnchorRoleKind.Primary && role != AnchorRoleKind.Secondary) continue;
                if (anchor.GetComponentInParent<UxrAvatar>(true) != _avatar) continue;

                UxrGrabbableObject item = anchor.CurrentPlacedObject;
                if (item == null || item.CurrentAnchor != anchor) continue;
                // UseParenting=false не наследует масштаб скелета. Родителем может быть AlignTransform.
                if (!item.transform.IsChildOf(_skeleton)) continue;
                _stored.Add(new StoredItem(anchor, item));
            }
        }

        private void OnScaleChanged(AvatarCalibrationApplier applier, float oldScale, float newScale)
        {
            if (_stored.Count == 0) return;

            if (!IsPositiveFinite(oldScale) || !IsPositiveFinite(newScale))
            {
                GameLog.Error($"[HolsterCalibrationScale] Некорректная смена масштаба {oldScale} → {newScale} у {name}.", this);
                _stored.Clear();
                return;
            }

            // Dummy Forward масштабируется равномерно. Обратное отношение сохраняет мировой
            // размер даже при повёрнутых/неравномерно масштабированных промежуточных узлах.
            float ratio = oldScale / newScale;
            foreach (StoredItem stored in _stored)
            {
                if (stored.Anchor == null || stored.Item == null) continue;
                if (stored.Anchor.CurrentPlacedObject != stored.Item || stored.Item.CurrentAnchor != stored.Anchor) continue;
                if (stored.Item.transform.parent != stored.Parent || !stored.Item.transform.IsChildOf(_skeleton)) continue;

                stored.Item.transform.localScale = stored.LocalScale * ratio;
            }

            _stored.Clear();
        }

        private static bool IsPositiveFinite(float value) => value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
