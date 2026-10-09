using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Представление одного слота: раскладка слота (своя у оружия или умолчание слота) и внешний вид арсенала.
    ///     Неизменно после разрешения; слот читает его через <see cref="ArsenalStationPresetBinding" />.
    /// </summary>
    public sealed class ArsenalPresentationSnapshot
    {
        public readonly ArsenalPresentationStyle Style;
        /// <summary>Ассет раскладки, из которого собрано представление.</summary>
        public readonly ArsenalSlotLayout Layout;
        /// <summary>Раскладка принесена оружием; ложь — умолчание слота.</summary>
        public readonly bool FromWeapon;
        public readonly ArsenalPresentationPose ItemTarget, MagazineTarget, CardTarget;
        public readonly Vector2 CardSize;
        public readonly float CardFontSize;
        public readonly ArsenalPlaceZone PlaceZone;
        private readonly ArsenalSupportPose[] _supports;
        public IReadOnlyList<ArsenalSupportPose> Supports => Array.AsReadOnly(_supports);
        internal ArsenalPresentationSnapshot(ArsenalPresentationStyle style, ArsenalSlotLayout layout, bool fromWeapon)
        {
            Style = style; Layout = layout; FromWeapon = fromWeapon;
            ItemTarget = layout.ItemTarget; MagazineTarget = layout.MagazineTarget; CardTarget = layout.CardTarget;
            CardSize = layout.CardSize; CardFontSize = layout.CardFontSize; PlaceZone = layout.PlaceZone;
            _supports = layout.Supports != null ? layout.Supports.ToArray() : Array.Empty<ArsenalSupportPose>();
        }
    }

    /// <summary>
    ///     Разрешение представления слота: своя раскладка оружия для вида слота, иначе раскладка слота по умолчанию;
    ///     плюс внешний вид арсенала.
    /// </summary>
    public static class ArsenalPresentationResolver
    {
        public static ArsenalPresentationSnapshot Resolve(WeaponInfo weapon, ArsenalPresentationZone slotKind, ArsenalPresentationStyle style,
            ArsenalSlotLayout slotDefault)
        {
            if (weapon == null || style == null) throw new InvalidOperationException("Представление требует оружие и стиль арсенала.");
            bool fromWeapon = weapon.TryGetSlotLayout(slotKind, out ArsenalSlotLayout layout);
            if (!fromWeapon) layout = slotDefault;
            if (layout == null)
                throw new InvalidOperationException("Нет раскладки для слота " + slotKind + ": ни у оружия " + weapon.WeaponId + ", ни по умолчанию у слота.");
            if (layout.SlotKind != slotKind)
                throw new InvalidOperationException("Раскладка " + layout.name + " для слота " + layout.SlotKind + ", а слот " + slotKind + ".");
            ValidatePose(layout.ItemTarget); ValidatePose(layout.MagazineTarget); ValidatePose(layout.CardTarget);
            ValidateCard(layout.CardSize, layout.CardFontSize); ValidateSupports(layout.Supports);
            if (layout.Supports != null && layout.Supports.Count != 0 && (style.SupportModule == null || style.ReturnReadyMaterial == null))
                throw new InvalidOperationException("Опоры раскладки " + layout.name + " требуют модуль опор и материал подсказки в стиле арсенала.");
            ValidateStyle(style);
            ValidatePrefabAlignment(weapon.WeaponPrefab);
            ValidatePrefabAlignment(weapon.MagazinePrefab);
            return new ArsenalPresentationSnapshot(style, layout, fromWeapon);
        }

        /// <summary>Generated frames проверяются до runtime Configure; cache служит только диагностикой projection.</summary>
        public static void ValidateMaterialized(ArsenalSlotController slot, ArsenalPresentationSnapshot snapshot)
        {
            void Match(Transform frame, ArsenalPresentationPose target)
            {
                if (frame == null || (frame.position - slot.transform.TransformPoint(target.Position)).sqrMagnitude > 1e-10f ||
                    Quaternion.Angle(frame.rotation, slot.transform.rotation * target.Rotation) > .001f)
                    throw new InvalidOperationException("Stale generated presentation target: " + slot.name);
            }
            Match(slot.ItemAnchor.AlignTransform, snapshot.ItemTarget);
            Match(((FirearmSlotController)slot).MagAnchor.AlignTransform, snapshot.MagazineTarget);
            var card = slot.GetComponentInChildren<ArsenalPriceTag>(true);
            Match(card != null ? card.transform : null, snapshot.CardTarget);
            var container = slot.transform.Find(ArsenalPresentationApplicator.SupportsContainerName);
            if (snapshot.Supports.Count == 0)
            {
                if (container != null && container.childCount != 0) throw new InvalidOperationException("Лишние generated supports.");
                ValidateReturnHints(slot, snapshot);
                return;
            }
            if (container == null || container.childCount != snapshot.Supports.Count || snapshot.Style.SupportModule == null)
                throw new InvalidOperationException("Stale generated supports.");
            foreach (var support in snapshot.Supports)
            {
                var role = container.Find(support.Role);
                Match(role, support.SlotPose);
                ValidateSupportTree(role, snapshot.Style.SupportModule.transform, true);
            }
            ValidateReturnHints(slot, snapshot);
        }

        private static void ValidateReturnHints(ArsenalSlotController slot, ArsenalPresentationSnapshot snapshot)
        {
            var hints = slot.transform.Find(ArsenalPresentationApplicator.ReturnHintsContainerName);
            int groups = 0;
            foreach (ArsenalSupportAnchorKind kind in Enum.GetValues(typeof(ArsenalSupportAnchorKind)))
            {
                var roles = new List<ArsenalSupportPose>();
                foreach (var support in snapshot.Supports) if (support.AnchorKind == kind) roles.Add(support);
                var group = hints != null ? hints.Find(kind.ToString()) : null;
                if (roles.Count == 0)
                {
                    if (group != null) throw new InvalidOperationException("Лишняя generated near-группа.");
                    continue;
                }
                groups++;
                if (hints == null || group == null || group.childCount != roles.Count)
                    throw new InvalidOperationException("Stale generated near-группа.");
                ValidateIdentityContainer(hints); ValidateIdentityContainer(group);
                var anchor = kind == ArsenalSupportAnchorKind.Weapon ? slot.ItemAnchor : (slot as FirearmSlotController)?.MagAnchor;
                if (anchor == null || anchor.ActivateOnCompatibleNear != group.gameObject)
                    throw new InvalidOperationException("Stale SDK near-binding.");
                foreach (var support in roles)
                {
                    var role = group.Find(support.Role);
                    if (role == null || (role.localPosition - support.SlotPose.Position).sqrMagnitude > 1e-10f ||
                        Quaternion.Angle(role.localRotation, support.SlotPose.Rotation) > .001f)
                        throw new InvalidOperationException("Stale generated near target.");
                    ValidateSupportTree(role, snapshot.Style.SupportModule.transform, true, snapshot.Style.ReturnReadyMaterial);
                }
            }
            if (hints != null && hints.childCount != groups) throw new InvalidOperationException("Лишние generated near-группы.");
        }

        private static void ValidateIdentityContainer(Transform container)
        {
            if (container.localPosition != Vector3.zero || Quaternion.Angle(container.localRotation, Quaternion.identity) > .001f ||
                container.localScale != Vector3.one || container.GetComponents<Component>().Length != 1)
                throw new InvalidOperationException("Stale generated near container frame.");
        }

        private static void ValidateSupportTree(Transform output, Transform input, bool root, Material ready = null)
        {
            if (output == null || output.childCount != input.childCount || output.localScale != input.localScale ||
                (!root && (output.localPosition != input.localPosition || Quaternion.Angle(output.localRotation, input.localRotation) > .001f)))
                throw new InvalidOperationException("Stale generated support topology/TRS.");
            var sourceComponents = input.GetComponents<Component>(); var outputComponents = output.GetComponents<Component>();
            if (sourceComponents.Length != outputComponents.Length) throw new InvalidOperationException("Stale generated support components.");
            for (int i = 0; i < sourceComponents.Length; i++)
                if (sourceComponents[i] == null || outputComponents[i] == null || sourceComponents[i].GetType() != outputComponents[i].GetType())
                    throw new InvalidOperationException("Stale generated support component types.");
            var sourceMesh = input.GetComponent<MeshFilter>(); var outputMesh = output.GetComponent<MeshFilter>();
            if ((sourceMesh == null) != (outputMesh == null) || (sourceMesh != null && sourceMesh.sharedMesh != outputMesh.sharedMesh))
                throw new InvalidOperationException("Stale generated support mesh.");
            var sourceRenderer = input.GetComponent<MeshRenderer>(); var outputRenderer = output.GetComponent<MeshRenderer>();
            if ((sourceRenderer == null) != (outputRenderer == null)) throw new InvalidOperationException("Stale generated support renderer.");
            if (sourceRenderer != null)
            {
                var a = sourceRenderer.sharedMaterials; var b = outputRenderer.sharedMaterials;
                if (a.Length != b.Length) throw new InvalidOperationException("Stale generated support materials.");
                for (int i = 0; i < a.Length; i++) if ((ready != null ? ready : a[i]) != b[i])
                    throw new InvalidOperationException("Stale generated support material.");
                if (ready != null && (outputRenderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off || outputRenderer.receiveShadows))
                    throw new InvalidOperationException("Near-подсказка не должна отбрасывать тени.");
            }
            for (int i = 0; i < input.childCount; i++)
                ValidateSupportTree(output.Find(input.GetChild(i).name), input.GetChild(i), false, ready);
        }

        /// <summary>Модуль опор — только визуальная геометрия: без коллайдеров, физики и поведения.</summary>
        public static void ValidateStyle(ArsenalPresentationStyle style)
        {
            if (style.SupportModule != null && (style.SupportModule.GetComponentsInChildren<Collider>(true).Length != 0 ||
                style.SupportModule.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                style.SupportModule.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 ||
                style.SupportModule.GetComponentsInChildren<Animator>(true).Length != 0))
                throw new InvalidOperationException("Модуль опор должен содержать только визуальную геометрию.");
        }

        public static void ValidatePrefabAlignment(GameObject prefab)
        {
            var item = prefab != null ? prefab.GetComponent<UxrGrabbableObject>() : null;
            if (item == null || item.DropAlignTransform == null ||
                (item.DropAlignTransform != item.transform && !item.DropAlignTransform.IsChildOf(item.transform)))
                throw new InvalidOperationException("Предмет не имеет собственного root/drop alignment.");
            if (item.DropSnapMode != UxrSnapToAnchorMode.PositionAndRotation)
                throw new InvalidOperationException("Представление требует SDK PositionAndRotation drop snap: " + prefab.name);
            ValidateScale(prefab.transform.localScale, "item root");
            for (var node = item.DropAlignTransform; node != null && node != item.transform; node = node.parent)
                ValidateScale(node.localScale, "drop alignment chain");
        }

        public static void ValidateFrame(Transform slot, Transform anchor, Transform alignment)
        {
            if (slot == null || anchor == null || alignment == null || !anchor.IsChildOf(slot) ||
                (alignment != anchor && !alignment.IsChildOf(anchor)))
                throw new InvalidOperationException("Anchor/AlignTransform не принадлежит своему slot frame.");
            for (var node = alignment; node != null; node = node.parent) ValidateScale(node.localScale, "anchor parent chain");
        }

        public static void ValidateScale(Vector3 scale, string context)
        {
            ValidateVector(scale);
            if (scale.x <= 0f || Mathf.Abs(scale.x - scale.y) > .00001f || Mathf.Abs(scale.x - scale.z) > .00001f)
                throw new InvalidOperationException("Не поддерживается negative/nonuniform scale: " + context);
        }
        public static void ValidatePose(ArsenalPresentationPose pose) { ValidateVector(pose.Position); ValidateVector(pose.EulerAngles); }
        public static void ValidateVector(Vector3 value)
        {
            if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z)) throw new InvalidOperationException("NaN/Infinity в presentation pose.");
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void ValidateCard(Vector2 size, float font)
        {
            if (!Finite(size.x) || !Finite(size.y) || !Finite(font) || size.x < .05f || size.y < .05f || font < .05f)
                throw new InvalidOperationException("Недопустимый размер/шрифт карточки.");
        }
        private static void ValidateSupports(IReadOnlyList<ArsenalSupportPose> supports)
        {
            if (supports == null) throw new InvalidOperationException("Пустой список supports.");
            var roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var support in supports)
            {
                if (string.IsNullOrWhiteSpace(support.Role) || support.Role.Contains("/") || !roles.Add(support.Role))
                    throw new InvalidOperationException("Пустая/повторная/path support role.");
                if (!Enum.IsDefined(typeof(ArsenalSupportAnchorKind), support.AnchorKind))
                    throw new InvalidOperationException("Неизвестный support anchor kind.");
                ValidatePose(support.SlotPose);
            }
        }
    }
}
