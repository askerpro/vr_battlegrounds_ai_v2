using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Копия рига ног (<c>&lt;префаб&gt;_LocomotionRig</c>) — невидимый скелет аватара с humanoid-Animator, на котором
    /// UltimateXR (<c>UxrAnimatedLegs</c>, патч 37) играет клипы ходьбы и берёт позы стоп, таза и груди. Печётся из
    /// ассета префаба, а не берётся у живого аватара: к первому кадру UltimateXR уже перенёс его скелет под
    /// <c>Dummy Forward</c>, и Animator на аватаре писал бы кости в старых координатах.
    ///
    /// <list type="bullet">
    /// <item>Копия объекта humanoid-Animator аватара без скриптов и рендереров; Avatar — тот же, кости аватара находятся по
    /// именам.</item>
    /// <item>Оставлены только кости скелета из описания гуманоида и их предки: всё прочее (руки-интеграции, снаряжение,
    /// меши) — лишние объекты на каждом аватаре.</item>
    /// <item>Предки Hips переименовываются по описанию гуманоида, если модель переименовали после импорта: у Heavy
    /// корень костей «root» в префабе назван «Bones», и humanoid-разметка не находила ни одной кости
    /// (<c>GetBoneTransform</c> — null, клипы не играли бы).</item>
    /// </list>
    /// </summary>
    public static class AvatarLegsRigBaker
    {
        public const string RigFolder = "Assets/Art/Avatars/Locomotion";

        public static string RigPath(string avatarPrefabPath) =>
            $"{RigFolder}/{Path.GetFileNameWithoutExtension(avatarPrefabPath)}_LocomotionRig.prefab";

        /// <summary>Печёт копию рига humanoid-Animator <paramref name="animatorObject"/> в <paramref name="rigPath"/>.</summary>
        public static GameObject Bake(GameObject animatorObject, RuntimeAnimatorController controller, string rigPath, List<string> report)
        {
            GameObject copy = Object.Instantiate(animatorObject);
            try
            {
                copy.name = Path.GetFileNameWithoutExtension(rigPath);
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                DestroyComponents(copy, c => !(c is Transform) && !(c is Animator));
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

                Animator animator = copy.GetComponent<Animator>();
                HumanDescription description = animator.avatar.humanDescription;
                RepairHipsAncestors(copy.transform, description, report);
                StripToSkeleton(copy.transform, description);

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                if (animator.GetBoneTransform(HumanBodyBones.Hips) == null || animator.GetBoneTransform(HumanBodyBones.LeftFoot) == null)
                    report?.Add($"{copy.name}: humanoid-разметка не нашла таз или стопу — клипы ходьбы на этой копии не заиграют");

                EnsureFolder(RigFolder);
                return PrefabUtility.SaveAsPrefabAsset(copy, rigPath);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        /// <summary>
        /// Предки Hips (между корнем копии и Hips), которых нет в описании гуманоида, получают имена недостающих в копии
        /// костей описания (не гуманоидных) по порядку — описание хранит скелет в порядке обхода, корень первым.
        /// </summary>
        private static void RepairHipsAncestors(Transform root, HumanDescription description, List<string> report)
        {
            string hipsName = description.human.FirstOrDefault(h => h.humanName == "Hips").boneName;
            if (string.IsNullOrEmpty(hipsName)) return;
            Transform hips = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == hipsName);
            if (hips == null) return;

            var skeleton = new HashSet<string>(description.skeleton.Skip(1).Select(b => b.name));
            var human = new HashSet<string>(description.human.Select(h => h.boneName));
            var present = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            Queue<string> missing = new Queue<string>(description.skeleton.Skip(1).Select(b => b.name)
                .Where(n => !present.Contains(n) && !human.Contains(n)));

            var ancestors = new List<Transform>();
            for (Transform t = hips.parent; t != null && t != root; t = t.parent) ancestors.Insert(0, t);
            foreach (Transform t in ancestors)
            {
                if (skeleton.Contains(t.name) || missing.Count == 0) continue;
                string name = missing.Dequeue();
                report?.Add($"{root.name}: кость '{t.name}' переименована в '{name}' (так её зовёт описание гуманоида)");
                t.name = name;
            }
        }

        /// <summary>Только кости описания гуманоида и их предки: прочие поддеревья удаляются.</summary>
        private static void StripToSkeleton(Transform root, HumanDescription description)
        {
            var skeleton = new HashSet<string>(description.skeleton.Select(b => b.name));
            var keep = new HashSet<Transform> { root };
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!skeleton.Contains(t.name)) continue;
                for (Transform a = t; a != null && keep.Add(a); a = a.parent) { }
            }

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true).Reverse())
                if (t != null && !keep.Contains(t) && (t.parent == null || keep.Contains(t.parent)))
                    Object.DestroyImmediate(t.gameObject);
        }

        /// <summary>
        /// Удаляет компоненты под <paramref name="root"/>, подходящие под <paramref name="filter"/>, в порядке зависимостей:
        /// компонент, который требует другой (<see cref="RequireComponent"/>), уходит раньше — без ошибок «Can't remove … because
        /// … depends on it» в консоли. Возвращает удалённые (тип и объект).
        /// </summary>
        public static List<string> DestroyComponents(GameObject root, System.Func<Component, bool> filter)
        {
            var removed = new List<string>();
            for (int pass = 0; pass < 8; pass++)
            {
                bool any = false;
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || !filter(c) || IsRequiredByOthers(c)) continue;
                    removed.Add($"{c.GetType().Name} на '{c.name}'");
                    Object.DestroyImmediate(c, true);
                    any = true;
                }

                if (!any) break;
            }

            return removed;
        }

        private static bool IsRequiredByOthers(Component c)
        {
            foreach (Component other in c.GetComponents<Component>())
            {
                if (other == null || other == c) continue;
                foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                    foreach (System.Type t in new[] { require.m_Type0, require.m_Type1, require.m_Type2 })
                        if (t != null && t.IsInstanceOfType(c) && c.GetComponents(t).Length == 1) return true;
            }

            return false;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
