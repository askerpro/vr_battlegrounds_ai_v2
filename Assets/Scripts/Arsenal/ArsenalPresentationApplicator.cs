using System;
using UnityEngine;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Перенос представления слота в трансформы: якоря, предмет в слоте, карточка. Позы — как в раскладке.</summary>
    public static class ArsenalPresentationApplicator
    {
        public const string SupportsContainerName = "PresentationSupports";
        public const string ReturnHintsContainerName = "PresentationReturnHints";
        public static ArsenalPresentationSnapshot Resolve(ArsenalSlotController slot)
        {
            var binding = slot.GetComponentInParent<ArsenalStationPresetBinding>(true);
            if (binding == null) throw new InvalidOperationException("Слот вне станции: " + slot.name);
            return binding.ResolvePresentation(slot);
        }

        /// <summary>Материализует effective AlignTransform target перемещением whole anchor root; дочерние align/proxy TRS сохраняются.</summary>
        public static void MaterializeAnchor(Transform slot, UxrGrabbableObjectAnchor anchor, ArsenalPresentationPose target)
        {
            ArsenalPresentationResolver.ValidatePose(target);
            ArsenalPresentationResolver.ValidateFrame(slot, anchor.transform, anchor.AlignTransform);
            Transform align = anchor.AlignTransform;
            Vector3 offset = Quaternion.Inverse(anchor.transform.rotation) * (align.position - anchor.transform.position);
            Quaternion relative = Quaternion.Inverse(anchor.transform.rotation) * align.rotation;
            Quaternion rotation = slot.rotation * target.Rotation * Quaternion.Inverse(relative);
            Vector3 position = slot.TransformPoint(target.Position) - rotation * offset;
            // Повтор не должен менять serialized floats от inverse/world roundtrip.
            if ((anchor.transform.position - position).sqrMagnitude > 1e-12f || Quaternion.Angle(anchor.transform.rotation, rotation) > .00001f)
                anchor.transform.SetPositionAndRotation(position, rotation);
        }

        public static void MaterializeFrames(ArsenalSlotController slot, ArsenalPresentationSnapshot presentation)
        {
            MaterializeAnchor(slot.transform, slot.ItemAnchor, presentation.ItemTarget);
            var firearm = slot as FirearmSlotController;
            if (firearm != null && firearm.MagAnchor != null)
                MaterializeAnchor(slot.transform, firearm.MagAnchor, presentation.MagazineTarget);
        }

        /// <summary>Pure root pose из prefab DropAlign, без клонирования SDK/physics. Сохранённый prefab scale учитывается ровно один раз.</summary>
        public static ArsenalPresentationPose RootPose(GameObject prefab, Vector3 targetPosition, Quaternion targetRotation, float frameScale = 1f)
        {
            ArsenalPresentationResolver.ValidatePrefabAlignment(prefab);
            var source = prefab.GetComponent<UxrGrabbableObject>();
            Transform drop = source.DropAlignTransform;
            Vector3 dropPosition = source.transform.InverseTransformPoint(drop.position);
            Quaternion dropRotation = Quaternion.Inverse(source.transform.rotation) * drop.rotation;
            Quaternion rotation = targetRotation * Quaternion.Inverse(dropRotation);
            if (float.IsNaN(frameScale) || float.IsInfinity(frameScale) || frameScale <= 0f)
                throw new InvalidOperationException("Недопустимый scale alignment frame.");
            return new ArsenalPresentationPose(targetPosition - rotation * Vector3.Scale(dropPosition, prefab.transform.localScale) * frameScale, rotation);
        }

        /// <summary>Общее вычисление для runtime, render-only preview и builder. Styled target только из immutable snapshot.</summary>
        public static ArsenalPresentationPose ItemLocalPose(ArsenalSlotController slot, bool magazine, ArsenalPresentationSnapshot presentation)
        {
            var anchor = magazine ? ((FirearmSlotController)slot).MagAnchor : slot.ItemAnchor;
            if (anchor == null) throw new InvalidOperationException("Нет item presentation anchor.");
            ArsenalPresentationResolver.ValidateFrame(slot.transform, anchor.transform, anchor.AlignTransform);
            var target = magazine ? presentation.MagazineTarget : presentation.ItemTarget;
            var prefab = magazine ? slot.WeaponData.MagazinePrefab : slot.WeaponData.WeaponPrefab;
            // Styled initial projection сохраняет физический prefab worldScale; parent compensation derived.
            var world = RootPose(prefab, slot.transform.TransformPoint(target.Position), slot.transform.rotation * target.Rotation);
            return new ArsenalPresentationPose(anchor.transform.InverseTransformPoint(world.Position), Quaternion.Inverse(anchor.transform.rotation) * world.Rotation);
        }

        public static Vector3 ProjectionLocalScale(GameObject source, Transform parent, ArsenalPresentationSnapshot presentation)
        {
            ArsenalPresentationResolver.ValidateScale(parent.lossyScale, "projection parent");
            return source.transform.localScale / parent.lossyScale.x;
        }

#if UNITY_EDITOR
        /// <summary>Только временный editor sample; prefab asset scale не изменяется.</summary>
        public static GameObject CreateDisplaySample(ArsenalSlotController slot, bool magazine)
        {
            var presentation = Resolve(slot);
            var source = magazine ? slot.WeaponData.MagazinePrefab : slot.WeaponData.WeaponPrefab;
            var anchor = magazine ? ((FirearmSlotController)slot).MagAnchor : slot.ItemAnchor;
            var sample = UnityEngine.Object.Instantiate(source, anchor.transform, false);
            sample.transform.localScale = ProjectionLocalScale(source, anchor.transform, presentation);
            return sample;
        }
#endif

        public static void ApplyWeapon(ArsenalSlotController slot, Transform item) => ApplyItem(slot, item, false, Resolve(slot));

        public static void ApplyMagazine(ArsenalMagazineOffer offer, Transform item) => ApplyItem(offer.Slot, item, true, Resolve(offer.Slot));

        private static void ApplyItem(ArsenalSlotController slot, Transform item, bool magazine, ArsenalPresentationSnapshot presentation)
        {
            var target = magazine ? presentation.MagazineTarget : presentation.ItemTarget;
            var prefab = magazine ? slot.WeaponData.MagazinePrefab : slot.WeaponData.WeaponPrefab;
            // SDK reparent сохраняет worldScale. Возврат оружия не масштабирует его до anchor scale.
            // Pure root/drop compensation использует actual worldScale предмета, без TRS scale write.
            var world = RootPose(prefab, slot.transform.TransformPoint(target.Position), slot.transform.rotation * target.Rotation,
                item.lossyScale.x / prefab.transform.localScale.x);
            item.SetPositionAndRotation(world.Position, world.Rotation);
        }

        public static void ApplyCard(ArsenalSlotController slot, Transform card, ArsenalPresentationSnapshot presentation)
        {
            if (card.parent != slot.transform) card.SetParent(slot.transform, true);
            card.SetLocalPositionAndRotation(presentation.CardTarget.Position, presentation.CardTarget.Rotation);
        }

        /// <summary>Явная generator projection для инспекции. Runtime/preview cache не авторят.</summary>
        public static ArsenalPriceTag MaterializeCard(ArsenalSlotController slot) => MaterializeCard(slot, Resolve(slot));

        public static ArsenalPriceTag MaterializeCard(ArsenalSlotController slot, ArsenalPresentationSnapshot presentation)
        {
            return ArsenalPriceTag.Create(slot, presentation);
        }
    }
}
