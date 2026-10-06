using System.Collections.Generic;
using System.Text;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar.Controllers;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.Animations;
#endif

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Отладочная панель ног UltimateXR (Play): над каждым аватаром с <see cref="UxrAnimatedLegs"/> — вся цепочка решения
    /// позы ног в этом кадре: высота головы → доля роста → цель и сглаженный <c>Legs_Crouch</c> → шаги вкл/выкл → доля позы
    /// клипа против решателя ноги → состояния Animator копии рига по слоям (фаза, переход) и клипы с весами → параметры.
    /// Подпись повёрнута к камере. Инспектор панели — график «доля роста → Legs_Crouch», ручной Legs_Crouch и пороги приседа.
    /// Ставится стендом-кукловодом или меню <c>Tools/VR Battlegrounds/Debug/Legs Debug Panel</c> (и в Play в любой сцене).
    /// </summary>
    public class LegsDebugPanel : MonoBehaviour
    {
        [Tooltip("Подписи над аватарами (Game и Scene view). Выключено — только инспектор панели.")]
        public bool labels = true;

        [Tooltip("Высота подписи над полом аватара, м.")]
        public float labelHeight = 2.2f;

        [Tooltip("Размер шрифта подписи.")]
        public float labelSize = 0.012f;

        private readonly Dictionary<UxrStandardAvatarController, TextMesh> _labels = new Dictionary<UxrStandardAvatarController, TextMesh>();
        private readonly List<UxrStandardAvatarController> _controllers = new List<UxrStandardAvatarController>();
        private static readonly Dictionary<RuntimeAnimatorController, Dictionary<int, string>> s_stateNames = new Dictionary<RuntimeAnimatorController, Dictionary<int, string>>();
        private float _nextScan;

        /// <summary>Аватары с ногами из клипов, найденные панелью.</summary>
        public IReadOnlyList<UxrStandardAvatarController> Controllers => _controllers;

        private void LateUpdate()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Scan();
            }

            Camera view = Camera.main;
            foreach (UxrStandardAvatarController c in _controllers)
            {
                if (!_labels.TryGetValue(c, out TextMesh label) || label == null) continue;
                label.gameObject.SetActive(labels);
                if (!labels) continue;
                Transform t = label.transform;
                t.position = c.transform.position + c.transform.up * labelHeight;
                if (view != null) t.rotation = Quaternion.LookRotation(t.position - view.transform.position, Vector3.up);
                label.characterSize = labelSize;
                label.text = Describe(c);
            }
        }

        private void OnDisable()
        {
            foreach (TextMesh label in _labels.Values)
            {
                if (label != null) Destroy(label.gameObject);
            }

            _labels.Clear();
            _controllers.Clear();
        }

        private void Scan()
        {
            _controllers.Clear();
            foreach (UxrStandardAvatarController c in FindObjectsByType<UxrStandardAvatarController>())
            {
                if (c.AnimatedLegs == null) continue;
                _controllers.Add(c);
                if (_labels.ContainsKey(c) && _labels[c] != null) continue;
                var go = new GameObject($"LegsDebugLabel ({c.name})") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                var tm = go.AddComponent<TextMesh>();
                tm.fontSize = 64;
                tm.anchor = TextAnchor.LowerCenter;
                tm.alignment = TextAlignment.Left;
                tm.color = Color.black;
                _labels[c] = tm;
            }
        }

        /// <summary>Цепочка решения ног аватара в этом кадре — текстом (подпись и инспектор).</summary>
        public static string Describe(UxrStandardAvatarController c)
        {
            var sb = new StringBuilder();
            UxrAnimatedLegs legs = c.AnimatedLegs;
            sb.Append(c.name).Append('\n');
            if (legs == null || !legs.IsReady)
            {
                sb.Append("ноги: копия рига не готова");
                return sb.ToString();
            }

            UxrAnimatedLegs.CrouchDebugState d = legs.CrouchDebug;
            UxrLegsSettings s = c.Legs;
            if (d.Valid)
            {
                sb.Append($"голова {d.CameraHeight:0.00} м / рост {d.StandRef:0.00} = {d.Ratio:0.00}\n");
                sb.Append($"  стоя ≥ {d.DeadZoneRatio:0.00} · колено {d.KneelRatio:0.00}").Append(d.HasSit ? $" · сидя {d.SitRatio:0.00}" : " · сидения нет").Append('\n');
                sb.Append($"Legs_Crouch цель {d.Target:0.00}{(d.Overridden ? " (вручную)" : "")} → {d.Crouch:0.00}\n");
                sb.Append(d.StepsOff ? $"шаги ВЫКЛ (> {s.locomotionCrouchLimit:0.00}), root motion выброшен\n" : $"шаги вкл (≤ {s.locomotionCrouchLimit:0.00}), идёт: {(legs.IsMoving ? "да" : "нет")}\n");
            }
            else
            {
                sb.Append("присед: нет уровней клипов или параметра Legs_Crouch\n");
            }

            float clip = d.ClipLegsWeight;
            sb.Append($"ноги ниже таза: решатель {(1f - clip) * s.legsWeight:P0} · поза клипа {clip:P0} (с {s.clipLegsStart:0.00}) · таз и корпус — BodyIK\n");
            sb.Append($"стойка {c.LegStance:0.#}\n");
            AppendAnimator(sb, legs.RigAnimator);
            return sb.ToString();
        }

        private static void AppendAnimator(StringBuilder sb, Animator a)
        {
            if (a == null || a.runtimeAnimatorController == null) return;
            for (int layer = 0; layer < a.layerCount; layer++)
            {
                AnimatorStateInfo state = a.GetCurrentAnimatorStateInfo(layer);
                float layerWeight = layer == 0 ? 1f : a.GetLayerWeight(layer);
                sb.Append($"[{layer}] {a.GetLayerName(layer)} ×{layerWeight:0.00}: {StateName(a, state.shortNameHash)} фаза {Mathf.Repeat(state.normalizedTime, 1f):0.00}");
                if (state.loop && state.normalizedTime >= 1f) sb.Append($" (цикл {Mathf.FloorToInt(state.normalizedTime) + 1})");
                sb.Append('\n');

                bool inTransition = a.IsInTransition(layer);
                float t = inTransition ? a.GetAnimatorTransitionInfo(layer).normalizedTime : 0f;
                AppendClips(sb, a.GetCurrentAnimatorClipInfo(layer), inTransition ? 1f - t : 1f);
                if (!inTransition) continue;

                AnimatorStateInfo next = a.GetNextAnimatorStateInfo(layer);
                sb.Append($"  → {StateName(a, next.shortNameHash)} {t:P0}, фаза {Mathf.Repeat(next.normalizedTime, 1f):0.00}\n");
                AppendClips(sb, a.GetNextAnimatorClipInfo(layer), t);
            }

            sb.Append("параметры:");
            foreach (AnimatorControllerParameter p in a.parameters)
            {
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float: sb.Append($" {Short(p.name)} {a.GetFloat(p.nameHash):0.00}"); break;
                    case AnimatorControllerParameterType.Int: sb.Append($" {Short(p.name)} {a.GetInteger(p.nameHash)}"); break;
                    case AnimatorControllerParameterType.Bool: sb.Append($" {Short(p.name)} {(a.GetBool(p.nameHash) ? "да" : "нет")}"); break;
                }
            }
        }

        /// <summary>Клипы состояния (листья blend tree) с весом в позе: вес в состоянии × доля состояния в переходе.</summary>
        private static void AppendClips(StringBuilder sb, AnimatorClipInfo[] clips, float stateShare)
        {
            foreach (AnimatorClipInfo info in clips)
            {
                float w = info.weight * stateShare;
                if (w < 0.005f || info.clip == null) continue;
                sb.Append($"    {info.clip.name}  {w:0.00}\n");
            }
        }

        private static string Short(string param) => param.StartsWith("Legs_") ? param.Substring(5) : param;

        private static string StateName(Animator a, int shortNameHash)
        {
#if UNITY_EDITOR
            RuntimeAnimatorController rc = a.runtimeAnimatorController;
            if (!s_stateNames.TryGetValue(rc, out Dictionary<int, string> names))
            {
                names = new Dictionary<int, string>();
                AnimatorController ac = rc as AnimatorController ?? (rc is AnimatorOverrideController o ? o.runtimeAnimatorController as AnimatorController : null);
                if (ac != null)
                {
                    foreach (AnimatorControllerLayer l in ac.layers) Collect(l.stateMachine, names);
                }

                s_stateNames[rc] = names;
            }

            if (names.TryGetValue(shortNameHash, out string name)) return name;
#endif
            return $"#{shortNameHash}";
        }

#if UNITY_EDITOR
        private static void Collect(AnimatorStateMachine sm, Dictionary<int, string> names)
        {
            foreach (ChildAnimatorState s in sm.states) names[s.state.nameHash] = s.state.name;
            foreach (ChildAnimatorStateMachine child in sm.stateMachines) Collect(child.stateMachine, names);
        }
#endif
    }
}
