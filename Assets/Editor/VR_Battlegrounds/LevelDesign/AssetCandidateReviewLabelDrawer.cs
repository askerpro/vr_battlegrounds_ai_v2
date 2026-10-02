using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DebugTools;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Подписи постоянного экранного размера с разведением пересекающихся карточек.</summary>
    [InitializeOnLoad]
    public static class AssetCandidateReviewLabelDrawer
    {
        public static Rect[] LastRects { get; private set; } = new Rect[0];
        public struct LabelInfo { public string Caption; public Color Color; public Vector3 Position; }

        static AssetCandidateReviewLabelDrawer()
        {
            SceneView.duringSceneGui += Draw;
        }

        private static void Draw(SceneView view)
        {
            if (!view.drawGizmos || view.camera == null)
            {
                LastRects = new Rect[0];
                return;
            }
            var labels = Object.FindObjectsByType<AssetCandidateReviewLabel>(FindObjectsSortMode.None)
                .Where(l => l.isActiveAndEnabled)
                .Select(l => new LabelInfo { Caption = l.Caption, Color = l.Color, Position = l.transform.position })
                .Concat(CatalogMarkingGizmos.Labels())
                .OrderBy(l => Vector3.SqrMagnitude(l.Position - view.camera.transform.position));
            var occupied = new List<Rect>();
            Handles.BeginGUI();
            Color previous = GUI.color;
            Color previousHandles = Handles.color;
            try
            {
                foreach (var label in labels)
                {
                    Vector3 viewport = view.camera.WorldToViewportPoint(label.Position);
                    if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) continue;
                    Vector2 anchor = HandleUtility.WorldToGUIPoint(label.Position);
                    var style = new GUIStyle(EditorStyles.helpBox)
                    {
                        fontSize = 12,
                        wordWrap = true,
                        alignment = TextAnchor.UpperLeft,
                        padding = new RectOffset(7, 7, 5, 5)
                    };
                    style.normal.textColor = label.Color;
                    var content = new GUIContent(label.Caption);
                    float width = Mathf.Min(360, style.CalcSize(content).x + 8);
                    float height = style.CalcHeight(content, width);
                    float screenWidth = view.position.width;
                    var rect = new Rect(Mathf.Clamp(anchor.x - width * 0.5f, 4, Mathf.Max(4, screenWidth - width - 4)),
                        anchor.y - height - 12, width, height);
                    bool fits = false;
                    // Когда места мало, дальние карточки скрываем вместо наложения на ближние.
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        if (rect.y < 30) break;
                        if (!occupied.Any(r => r.Overlaps(rect))) { fits = true; break; }
                        rect.y -= height + 6;
                    }
                    if (!fits) continue;
                    occupied.Add(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6));
                    Handles.color = label.Color;
                    Handles.DrawLine(new Vector3(anchor.x, anchor.y, 0), new Vector3(rect.center.x, rect.yMax, 0));
                    GUI.color = Color.white;
                    GUI.Box(rect, content, style);
                }
            }
            finally
            {
                GUI.color = previous;
                Handles.color = previousHandles;
                Handles.EndGUI();
                LastRects = occupied.Select(r => new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6)).ToArray();
            }
        }
    }
}
