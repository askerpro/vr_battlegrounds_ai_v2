using System;
using System.Collections.Generic;
using UnityEngine;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Неизменные значения одного разрешения; styled targets не читаются из slot cache.</summary>
    public sealed class ArsenalPresentationSnapshot
    {
        public readonly ArsenalPresentationStyle Style;
        public readonly ArsenalPresentationPose ItemTarget, MagazineTarget, CardTarget;
        public readonly Vector2 CardSize;
        public readonly float CardFontSize;
        public readonly ArsenalPresentationPose LegacyItemPose;
        private readonly ArsenalSupportPose[] _supports;
        public IReadOnlyList<ArsenalSupportPose> Supports => Array.AsReadOnly(_supports);
        public bool IsStyled => Style != null;
        internal ArsenalPresentationSnapshot(ArsenalPresentationStyle style, ArsenalPresentationPose item,
            ArsenalPresentationPose magazine, ArsenalPresentationPose card, Vector2 size, float font,
            IReadOnlyList<ArsenalSupportPose> supports, ArsenalPresentationPose legacy = default)
        {
            Style = style; ItemTarget = item; MagazineTarget = magazine; CardTarget = card;
            CardSize = size; CardFontSize = font; LegacyItemPose = legacy;
            _supports = new ArsenalSupportPose[supports == null ? 0 : supports.Count];
            for (int i = 0; i < _supports.Length; i++) _supports[i] = supports[i];
        }
    }

    /// <summary>Один pure выбор defaults/exceptions. Contact fit не является runtime writer.</summary>
    public static class ArsenalPresentationResolver
    {
        public static ArsenalPresentationSnapshot Resolve(WeaponInfo weapon, ArsenalPresentationZone zone, ArsenalPresentationStyle style)
        {
            if (weapon == null || style == null) throw new InvalidOperationException("Styled resolver требует WeaponInfo и style.");
            Validate(style);
            ArsenalPresentationStyle.ZoneDefaults defaults = null;
            foreach (var candidate in style.Zones) if (candidate.Zone == zone) defaults = candidate;
            if (defaults == null) throw new InvalidOperationException("Неизвестная физическая зона композиции.");
            var item = defaults.ItemTarget; var magazine = defaults.MagazineTarget; var card = defaults.CardTarget;
            var size = defaults.CardSize; float font = defaults.CardFontSize;
            IReadOnlyList<ArsenalSupportPose> supports = defaults.Supports;
            foreach (var exception in style.Exceptions)
            {
                if (exception.Weapon != weapon || exception.Zone != zone) continue;
                if (exception.OverrideItem) item = exception.ItemTarget;
                if (exception.OverrideMagazine) magazine = exception.MagazineTarget;
                if (exception.OverrideCard) { card = exception.CardTarget; size = exception.CardSize; font = exception.CardFontSize; }
                if (exception.OverrideSupports) supports = exception.Supports;
            }
            ValidatePrefabAlignment(weapon.WeaponPrefab);
            ValidatePrefabAlignment(weapon.MagazinePrefab);
            return new ArsenalPresentationSnapshot(style, item, magazine, card, size, font, supports);
        }

        /// <summary>Generated frames проверяются до runtime Configure; cache служит только диагностикой projection.</summary>
        public static void ValidateMaterialized(ArsenalSlotController slot, ArsenalPresentationSnapshot snapshot)
        {
            if (!snapshot.IsStyled) return;
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
            if (slot.CardSize != snapshot.CardSize || slot.CardFontSize != snapshot.CardFontSize)
                throw new InvalidOperationException("Stale generated card size/font: " + slot.name);
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

        public static void Validate(ArsenalPresentationStyle style)
        {
            if (style == null || style.Zones == null || style.Zones.Count != 2)
                throw new InvalidOperationException("Стиль требует ровно Pegboard и Shelf defaults.");
            var zones = new HashSet<ArsenalPresentationZone>();
            foreach (var zone in style.Zones)
            {
                if (zone == null || !Enum.IsDefined(typeof(ArsenalPresentationZone), zone.Zone) || !zones.Add(zone.Zone))
                    throw new InvalidOperationException("Неизвестная или повторная зона стиля.");
                ValidatePose(zone.ItemTarget); ValidatePose(zone.MagazineTarget); ValidatePose(zone.CardTarget);
                ValidateCard(zone.CardSize, zone.CardFontSize); ValidateSupports(zone.Supports);
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exception in style.Exceptions)
            {
                if (exception == null || exception.Weapon == null || !Enum.IsDefined(typeof(ArsenalPresentationZone), exception.Zone) ||
                    !keys.Add(exception.Weapon.GetInstanceID() + ":" + (int)exception.Zone))
                    throw new InvalidOperationException("Недопустимый или повторный WeaponInfo/zone exception.");
                ValidatePose(exception.ItemTarget); ValidatePose(exception.MagazineTarget); ValidatePose(exception.CardTarget);
                ValidateCard(exception.CardSize, exception.CardFontSize); ValidateSupports(exception.Supports);
            }
            if (style.SupportModule != null && (style.SupportModule.GetComponentsInChildren<Collider>(true).Length != 0 ||
                style.SupportModule.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                style.SupportModule.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 ||
                style.SupportModule.GetComponentsInChildren<Animator>(true).Length != 0))
                throw new InvalidOperationException("Support module должен содержать только визуальную геометрию.");
            bool hasSupports = false;
            foreach (var zone in style.Zones) hasSupports |= zone.Supports.Count != 0;
            foreach (var exception in style.Exceptions) hasSupports |= exception.OverrideSupports && exception.Supports.Count != 0;
            if (hasSupports && (style.SupportModule == null || style.ReturnReadyMaterial == null))
                throw new InvalidOperationException("Supports требуют visual module и один Style-owned near material.");
        }

        public static void ValidatePrefabAlignment(GameObject prefab)
        {
            var item = prefab != null ? prefab.GetComponent<UxrGrabbableObject>() : null;
            if (item == null || item.DropAlignTransform == null ||
                (item.DropAlignTransform != item.transform && !item.DropAlignTransform.IsChildOf(item.transform)))
                throw new InvalidOperationException("Предмет не имеет собственного root/drop alignment.");
            if (item.DropSnapMode != UxrSnapToAnchorMode.PositionAndRotation)
                throw new InvalidOperationException("Styled presentation требует SDK PositionAndRotation drop snap: " + prefab.name);
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

    /// <summary>Единственный совместимый адаптер сохранённой legacy presentation; никогда не вызывается styled path.</summary>
    public static class ArsenalLegacyPresentationAdapter
    {
        public static ArsenalPresentationSnapshot Resolve(ArsenalSlotController slot)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));
            Vector3 cardPosition; Quaternion cardRotation; Vector2 size; float font;
            if (slot.HasCustomCardPresentation)
            {
                cardPosition = slot.CardLocalPosition; cardRotation = slot.CardLocalRotation;
                size = slot.CardSize; font = slot.CardFontSize;
            }
            else
            {
                Transform wall = slot.Wall != null ? slot.Wall.transform : slot.transform;
                Vector3 anchor = slot.ItemAnchor != null ? slot.ItemAnchor.transform.position : slot.transform.position;
                Vector3 offset = slot.WeaponData != null && slot.WeaponData.Category == WeaponCategory.Rifle
                    ? new Vector3(.15f, .43f, -.065f) : new Vector3(.18f, .09f, -.1f);
                cardPosition = slot.transform.InverseTransformPoint(anchor + wall.rotation * offset);
                cardRotation = Quaternion.Inverse(slot.transform.rotation) * wall.rotation;
                size = new Vector2(.15f, .16f); font = .16f;
            }
            var legacy = new ArsenalPresentationPose {
                Position = slot.WeaponData != null ? slot.WeaponData.WeaponPositionOffset : Vector3.zero,
                EulerAngles = slot.WeaponData != null ? slot.WeaponData.WeaponRotationOffset : Vector3.zero };
            return new ArsenalPresentationSnapshot(null, default, default,
                new ArsenalPresentationPose(cardPosition, cardRotation), size, font, null, legacy);
        }

        /// <summary>Только явный Build(default) авторит legacy template. Обычный Ensure сохранённую custom pose не сбрасывает.</summary>
        public static void SeedCard(ArsenalSlotController slot, bool explicitDefault)
        {
            if (slot.HasCustomCardPresentation && !explicitDefault) return;
            foreach (var board in slot.GetComponentsInChildren<Transform>(true))
            {
                if (board.name != "PegboardSection") continue;
                bool underside = Vector3.Dot(board.TransformDirection(Vector3.back), Vector3.up) < -.5f;
                Vector3 point = board.TransformPoint(new Vector3(.3f, .25f, underside ? .54f : -.54f));
                slot.ConfigureCardPresentation(slot.transform.InverseTransformPoint(point), new Vector2(.15f, .16f), .16f,
                    Quaternion.Inverse(slot.transform.rotation) * board.rotation * (underside ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity));
                return;
            }
        }
    }
}
