using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Проекция представления слота по стилю: якоря, карточка, визуальные опоры и подсказки возврата SDK.
    ///     Единая точка для редакторского сборщика авторских станций (результат сохраняется в префаб) и для
    ///     сборщика сгенерированной станции (на свежем клоне шаблона, до включения). Позы применяются как есть.
    ///     Повтор на уже спроецированном слоте обновляет его на месте; смену состава опор не делает.
    /// </summary>
    public static class ArsenalSupportProjection
    {
        /// <summary>Единый порядок: якоря, карточка, опоры.</summary>
        public static void MaterializePresentation(ArsenalSlotController slot, ArsenalPresentationSnapshot snapshot)
        {
            ArsenalPresentationApplicator.MaterializeFrames(slot, snapshot);
            ArsenalPresentationApplicator.MaterializeCard(slot, snapshot);
            MaterializeSupports(slot, snapshot);
        }

        public static void MaterializeSupports(ArsenalSlotController slot, ArsenalPresentationSnapshot snapshot)
        {
            var module = snapshot.Style.SupportModule;
            var container = slot.transform.Find(ArsenalPresentationApplicator.SupportsContainerName);
            ValidateRoles(container, snapshot.Supports, module);
            var hints = slot.transform.Find(ArsenalPresentationApplicator.ReturnHintsContainerName);
            foreach (ArsenalSupportAnchorKind kind in Enum.GetValues(typeof(ArsenalSupportAnchorKind)))
            {
                var poses = snapshot.Supports.Where(s => s.AnchorKind == kind).ToArray();
                var group = hints != null ? hints.Find(kind.ToString()) : null;
                ValidateRoles(group, poses, module);
                var anchor = Anchor(slot, kind);
                if (anchor == null && poses.Length != 0) throw new InvalidOperationException("Нет SDK anchor для support kind.");
                if (anchor != null && poses.Length != 0 && anchor.ActivateOnCompatibleNear != null &&
                    (group == null || anchor.ActivateOnCompatibleNear != group.gameObject))
                    throw new InvalidOperationException("Чужая SDK near-подсказка: отдельный план замены обязателен.");
            }
            if (snapshot.Supports.Count == 0) return;
            container = EnsureIdentity(slot.transform, ArsenalPresentationApplicator.SupportsContainerName, true);
            hints = EnsureIdentity(slot.transform, ArsenalPresentationApplicator.ReturnHintsContainerName, true);
            foreach (var pose in snapshot.Supports) MaterializeRole(container, pose, module, null);
            foreach (ArsenalSupportAnchorKind kind in Enum.GetValues(typeof(ArsenalSupportAnchorKind)))
            {
                var poses = snapshot.Supports.Where(s => s.AnchorKind == kind).ToArray();
                if (poses.Length == 0) continue;
                var group = hints.Find(kind.ToString());
                bool created = group == null;
                group = EnsureIdentity(hints, kind.ToString(), false);
                foreach (var pose in poses) MaterializeRole(group, pose, module, snapshot.Style.ReturnReadyMaterial);
                // Runtime active-состояние полностью принадлежит SDK. Новый derived объект по умолчанию скрыт.
                if (created) group.gameObject.SetActive(false);
                var anchor = Anchor(slot, kind);
                if (anchor.ActivateOnCompatibleNear != group.gameObject) anchor.ActivateOnCompatibleNear = group.gameObject;
            }
        }

        private static UxrGrabbableObjectAnchor Anchor(ArsenalSlotController slot, ArsenalSupportAnchorKind kind) =>
            kind == ArsenalSupportAnchorKind.Weapon ? slot.ItemAnchor : (slot as FirearmSlotController)?.MagAnchor;

        private static Transform EnsureIdentity(Transform parent, string name, bool active)
        {
            var output = parent.Find(name);
            if (output == null)
            {
                output = new GameObject(name).transform;
                output.SetParent(parent, false);
                output.gameObject.SetActive(active);
            }
            SetPose(output, Vector3.zero, Quaternion.identity, Vector3.one);
            return output;
        }

        private static void MaterializeRole(Transform parent, ArsenalSupportPose pose, GameObject module, Material ready)
        {
            var role = parent.Find(pose.Role);
            if (role == null)
            {
                role = UnityEngine.Object.Instantiate(module, parent).transform;
                role.name = pose.Role;
            }
            var outputs = Nodes(role);
            foreach (var pair in Nodes(module.transform))
            {
                var output = outputs[pair.Key]; var input = pair.Value;
                // У корня только canonical target; не сбрасывать его в source pose на каждом повторе.
                SetPose(output, pair.Key.Length == 0 ? pose.SlotPose.Position : input.localPosition,
                    pair.Key.Length == 0 ? pose.SlotPose.Rotation : input.localRotation, input.localScale);
                if (output.gameObject.activeSelf != input.gameObject.activeSelf) output.gameObject.SetActive(input.gameObject.activeSelf);
                var filter = input.GetComponent<MeshFilter>();
                if (filter != null && output.GetComponent<MeshFilter>().sharedMesh != filter.sharedMesh)
                    output.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var inputRenderer = input.GetComponent<MeshRenderer>();
                if (inputRenderer == null) continue;
                var renderer = output.GetComponent<MeshRenderer>();
                var materials = ready == null ? inputRenderer.sharedMaterials : inputRenderer.sharedMaterials.Select(m => ready).ToArray();
                if (!renderer.sharedMaterials.SequenceEqual(materials)) renderer.sharedMaterials = materials;
                var shadows = ready == null ? inputRenderer.shadowCastingMode : ShadowCastingMode.Off;
                if (renderer.shadowCastingMode != shadows) renderer.shadowCastingMode = shadows;
                bool receive = ready == null && inputRenderer.receiveShadows;
                if (renderer.receiveShadows != receive) renderer.receiveShadows = receive;
            }
        }

        private static void SetPose(Transform output, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (output.localPosition != position || Quaternion.Angle(output.localRotation, rotation) > .00001f)
                output.SetLocalPositionAndRotation(position, rotation);
            if (output.localScale != scale) output.localScale = scale;
        }

        private static void ValidateRoles(Transform container, IReadOnlyList<ArsenalSupportPose> poses, GameObject module)
        {
            if (poses.Count != 0 && module == null) throw new InvalidOperationException("Support poses требуют visual module.");
            if (container == null) return;
            foreach (Transform role in container)
                if (!poses.Any(s => s.Role == role.name))
                    throw new InvalidOperationException("Удаление retained support role требует отдельного плана: " + role.name);
            foreach (var pose in poses)
            {
                var role = container.Find(pose.Role);
                if (role != null) ValidateTopology(role, module.transform);
            }
        }

        /// <summary>Узлы поддерева по относительному пути (корень — пустая строка), как AnimationUtility.CalculateTransformPath.</summary>
        private static Dictionary<string, Transform> Nodes(Transform root) => root.GetComponentsInChildren<Transform>(true)
            .ToDictionary(t => Path(t, root), StringComparer.Ordinal);

        private static string Path(Transform node, Transform root)
        {
            if (node == root) return string.Empty;
            var path = node.name;
            for (var parent = node.parent; parent != root; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }

        private static void ValidateTopology(Transform target, Transform source)
        {
            var outputs = Nodes(target); var inputs = Nodes(source);
            if (outputs.Count != inputs.Count || inputs.Any(p => !outputs.ContainsKey(p.Key)))
                throw new InvalidOperationException("Support topology replacement не входит в in-place refresh.");
            foreach (var pair in inputs)
            {
                var a = outputs[pair.Key].GetComponents<Component>().Select(c => c.GetType()).OrderBy(t => t.FullName);
                var b = pair.Value.GetComponents<Component>().Select(c => c.GetType()).OrderBy(t => t.FullName);
                if (!a.SequenceEqual(b)) throw new InvalidOperationException("Support component replacement запрещён.");
            }
        }
    }
}
