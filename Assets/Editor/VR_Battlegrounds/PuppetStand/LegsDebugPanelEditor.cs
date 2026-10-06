using UltimateXR.Animation.IK;
using UltimateXR.Avatar.Controllers;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DevTools.LegsCompare;

namespace VrBattlegrounds.Editor.PuppetStand
{
    /// <summary>
    /// Инспектор <see cref="LegsDebugPanel"/>: по аватару — цепочка решения ног (как подпись), график «доля роста →
    /// Legs_Crouch» по той же формуле, что у ног (<see cref="UxrAnimatedLegs.CrouchFromRatio"/>), с текущей точкой, ручной
    /// Legs_Crouch и пороги приседа (поля <see cref="UxrLegsSettings"/> этого аватара; правка в Play не сохраняется).
    /// Меню <c>Tools/VR Battlegrounds/Debug/Legs Debug Panel</c> ставит панель в открытую сцену.
    /// </summary>
    [CustomEditor(typeof(LegsDebugPanel))]
    public sealed class LegsDebugPanelEditor : UnityEditor.Editor
    {
        private const float GraphHeight = 120f;
        private const float MinRatio = 0.3f, MaxRatio = 1.05f;

        [MenuItem("Tools/VR Battlegrounds/Debug/Legs Debug Panel")]
        private static void AddToScene()
        {
            var panel = Object.FindAnyObjectByType<LegsDebugPanel>();
            if (panel == null)
            {
                var go = new GameObject("LegsDebugPanel (отладка ног)");
                if (Application.isPlaying) go.hideFlags = HideFlags.DontSave;
                else Undo.RegisterCreatedObjectUndo(go, "Legs Debug Panel");
                panel = go.AddComponent<LegsDebugPanel>();
            }

            Selection.activeObject = panel.gameObject;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var panel = (LegsDebugPanel)target;
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Работает в Play: подписи над аватарами с ногами UltimateXR, здесь — график, ручной Legs_Crouch и пороги.", MessageType.Info);
                return;
            }

            foreach (UxrStandardAvatarController c in panel.Controllers)
            {
                if (c == null || c.AnimatedLegs == null) continue;
                EditorGUILayout.Space();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(LegsDebugPanel.Describe(c), EditorStyles.wordWrappedMiniLabel);
                DrawGraph(c.AnimatedLegs.CrouchDebug, c.Legs);
                DrawOverride(c.AnimatedLegs);
                DrawSettings(c.Legs);
                EditorGUILayout.EndVertical();
            }

            Repaint();
        }

        private static void DrawOverride(UxrAnimatedLegs legs)
        {
            bool manual = legs.CrouchOverride.HasValue;
            bool newManual = EditorGUILayout.ToggleLeft("Legs_Crouch вручную (вместо высоты головы)", manual);
            if (newManual != manual) legs.CrouchOverride = newManual ? legs.Crouch : (float?)null;
            if (newManual) legs.CrouchOverride = EditorGUILayout.Slider("цель Legs_Crouch", legs.CrouchOverride ?? 0f, 0f, 2f);
        }

        private static void DrawSettings(UxrLegsSettings s)
        {
            EditorGUILayout.LabelField("Пороги (Play, не сохраняются — значения записать в префаб/сборщик):", EditorStyles.miniBoldLabel);
            s.crouchDeadZone = EditorGUILayout.Slider("мёртвая зона, доля роста", s.crouchDeadZone, 0f, 0.4f);
            s.crouchSmoothTime = EditorGUILayout.Slider("сглаживание, с", s.crouchSmoothTime, 0.01f, 1f);
            s.clipLegsStart = EditorGUILayout.Slider("поза клипа в ногах с", s.clipLegsStart, 0f, 1f);
            s.locomotionCrouchLimit = EditorGUILayout.Slider("шаги до Legs_Crouch", s.locomotionCrouchLimit, 0f, 2f);
        }

        /// <summary>
        /// X — доля роста (камера / рост стоя), Y — Legs_Crouch 0…2. Линия — цель по формуле ног; вертикали — пороги стоя,
        /// колена, сидения; красная вертикаль — голова сейчас; точки — цель (белая) и сглаженный Legs_Crouch (жёлтая).
        /// Зелёная полоса — где ноги ниже таза из позы клипа, серая — где шагов нет.
        /// </summary>
        private static void DrawGraph(UxrAnimatedLegs.CrouchDebugState d, UxrLegsSettings s)
        {
            Rect r = GUILayoutUtility.GetRect(10f, GraphHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint || !d.Valid) return;

            EditorGUI.DrawRect(r, new Color(0.13f, 0.13f, 0.13f));
            Vector2 P(float ratio, float crouch) =>
                new Vector2(Mathf.Lerp(r.xMin, r.xMax, Mathf.InverseLerp(MinRatio, MaxRatio, ratio)), Mathf.Lerp(r.yMax - 4f, r.yMin + 4f, crouch / 2f));

            // Полосы по Legs_Crouch: поза клипа в ногах (≥ clipLegsStart) и без шагов (> locomotionCrouchLimit).
            float yClip = P(0f, s.clipLegsStart).y, ySteps = P(0f, s.locomotionCrouchLimit).y;
            EditorGUI.DrawRect(Rect.MinMaxRect(r.xMin, r.yMin, r.xMin + 6f, yClip), new Color(0.2f, 0.6f, 0.2f, 0.8f));
            EditorGUI.DrawRect(Rect.MinMaxRect(r.xMin + 6f, r.yMin, r.xMin + 12f, ySteps), new Color(0.5f, 0.5f, 0.5f, 0.8f));

            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            for (int level = 0; level <= 2; level++) Handles.DrawLine(P(MinRatio, level), P(MaxRatio, level));

            VLine(r, P(d.DeadZoneRatio, 0f).x, new Color(0.6f, 0.6f, 1f, 0.6f));
            VLine(r, P(d.KneelRatio, 0f).x, new Color(0.6f, 1f, 0.6f, 0.6f));
            if (d.HasSit) VLine(r, P(d.SitRatio, 0f).x, new Color(1f, 0.8f, 0.4f, 0.6f));

            const int steps = 120;
            var line = new Vector3[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float ratio = Mathf.Lerp(MinRatio, MaxRatio, i / (float)steps);
                line[i] = P(ratio, UxrAnimatedLegs.CrouchFromRatio(ratio, 1f - d.DeadZoneRatio, d.KneelRatio, d.SitRatio, d.HasSit));
            }

            Handles.color = Color.white;
            Handles.DrawAAPolyLine(2f, line);

            VLine(r, P(Mathf.Clamp(d.Ratio, MinRatio, MaxRatio), 0f).x, Color.red);
            Dot(P(Mathf.Clamp(d.Ratio, MinRatio, MaxRatio), d.Target), Color.white);
            Dot(P(Mathf.Clamp(d.Ratio, MinRatio, MaxRatio), d.Crouch), Color.yellow);

            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.gray } };
            GUI.Label(new Rect(r.xMin + 14f, r.yMin, 200f, 14f), "Legs_Crouch 2 — сидя", style);
            GUI.Label(new Rect(r.xMin + 14f, P(0f, 1f).y - 14f, 200f, 14f), "1 — колено", style);
            GUI.Label(new Rect(r.xMax - 150f, r.yMax - 14f, 150f, 14f), "доля роста →  1.0 стоя", style);
        }

        private static void VLine(Rect r, float x, Color color)
        {
            Handles.color = color;
            Handles.DrawLine(new Vector3(x, r.yMin), new Vector3(x, r.yMax));
        }

        private static void Dot(Vector2 p, Color color)
        {
            EditorGUI.DrawRect(new Rect(p.x - 3f, p.y - 3f, 6f, 6f), color);
        }
    }
}
