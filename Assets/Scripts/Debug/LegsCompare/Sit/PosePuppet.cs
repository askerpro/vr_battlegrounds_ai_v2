using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Манекен поз для стендов подбора клипов (<see cref="SitCandidateStand"/>, <see cref="ClipScrubStand"/>): временная
    /// (DontSave) копия модели аватара без скриптов, коллайдеров и камер — остаются меш и humanoid Animator, позу которому
    /// задаёт PlayableGraph стенда. Над манекеном — текстовая подпись.
    /// </summary>
    public static class PosePuppet
    {
        /// <summary>
        /// Копия <paramref name="prefab"/> под <paramref name="parent"/>. Родитель должен быть неактивен: Awake скриптов модели
        /// не вызывается, они удаляются до включения. Возвращает humanoid Animator модели (без контроллера, без root motion) или null.
        /// </summary>
        public static Animator Create(GameObject prefab, Transform parent, string name, out GameObject root)
        {
            root = Object.Instantiate(prefab, parent);
            root.name = name;
            root.hideFlags = HideFlags.DontSave;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            StripBehaviours(root);

            foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
            {
                if (a.avatar == null || !a.avatar.isHuman) continue;
                a.runtimeAnimatorController = null;
                a.applyRootMotion = false;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                return a;
            }

            return null;
        }

        /// <summary>Подпись над манекеном, развёрнута к камере стенда (камера смотрит на ряд спереди, против +Z манекенов).</summary>
        public static TextMesh AddLabel(Transform parent, float height = 2.3f)
        {
            var go = new GameObject("Label") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.characterSize = 0.02f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.black;
            return tm;
        }

        private static void StripBehaviours(GameObject root)
        {
            // По зависимостям: компонент, который требует (RequireComponent) другой компонент того же объекта, удаляется
            // раньше требуемого — иначе Unity отказывает и пишет ошибку в консоль.
            var remaining = new List<MonoBehaviour>(root.GetComponentsInChildren<MonoBehaviour>(true));
            while (remaining.Count > 0)
            {
                int removed = 0;
                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    MonoBehaviour mb = remaining[i];
                    if (mb != null && IsRequiredByOther(mb, remaining)) continue;
                    if (mb != null) Object.DestroyImmediate(mb);
                    remaining.RemoveAt(i);
                    removed++;
                }

                if (removed == 0) break; // цикл зависимостей — остаток не трогаем
            }

            foreach (Collider col in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (Camera cam in root.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(cam.gameObject);
        }

        private static bool IsRequiredByOther(MonoBehaviour target, List<MonoBehaviour> remaining)
        {
            foreach (MonoBehaviour other in remaining)
            {
                if (other == null || other == target || other.gameObject != target.gameObject) continue;
                foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    if (Requires(req.m_Type0, target) || Requires(req.m_Type1, target) || Requires(req.m_Type2, target)) return true;
                }
            }

            return false;
        }

        private static bool Requires(System.Type required, MonoBehaviour target) => required != null && required.IsInstanceOfType(target);

        /// <summary>Гизмо ног: кости и куда смотрит колено (поперечная к оси «бедро → стопа» составляющая) — резкий разворот — перекрут.</summary>
        public static void DrawLegs(Animator a)
        {
            if (a == null) return;
            DrawKnee(a, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, Color.yellow);
            DrawKnee(a, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, Color.cyan);
        }

        private static void DrawKnee(Animator a, HumanBodyBones thighBone, HumanBodyBones kneeBone, HumanBodyBones footBone, Color color)
        {
            Transform thigh = a.GetBoneTransform(thighBone), knee = a.GetBoneTransform(kneeBone), foot = a.GetBoneTransform(footBone);
            if (thigh == null || knee == null || foot == null) return;
            Vector3 axis = foot.position - thigh.position;
            Vector3 bend = Vector3.ProjectOnPlane(knee.position - thigh.position, axis.normalized);
            Gizmos.color = color;
            Gizmos.DrawLine(thigh.position, knee.position);
            Gizmos.DrawLine(knee.position, foot.position);
            if (bend.sqrMagnitude > 1e-6f) Gizmos.DrawLine(knee.position, knee.position + bend.normalized * 0.25f);
        }
    }
}
