using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    ///     Метки в Scene View для всех <see cref="UxrGrabbableObjectAnchor" />: карманов аватара,
    ///     слотов магазинов в оружии, слотов арсенала. У самого якоря SDK гизмо нет — пустой
    ///     объект без геометрии в сцене не найти.
    ///
    ///     <para>
    ///     Рисуется через <see cref="DrawGizmo" /> на тип SDK, а не компонентом на префабах:
    ///     префабы и исходники UltimateXR не меняются, в билд ничего не попадает, новые якоря
    ///     получают метку сами. Выключается штатно — меню Gizmos сцены, строка
    ///     <c>UxrGrabbableObjectAnchor</c>.
    ///     </para>
    ///
    ///     Цвет — роль якоря: зелёный — карман магазинов, оранжевый — основное оружие
    ///     (<c>Anchor_Back</c>), голубой — второе (<c>Anchor_Hip_R</c>), жёлтый — прочие якоря
    ///     аватара, серый — якоря вне аватара. Оси показывают, как ляжет предмет (синяя — вперёд).
    ///     Пунктир ведёт к прокси вслепую-захвата, если он лежит не внутри якоря.
    /// </summary>
    internal static class GrabbableAnchorGizmos
    {
        private const float BoxSize   = 0.07f;
        private const float AxisSize  = 0.09f;
        private const float ProxySize = 0.05f;

        // Иконки — белые силуэты, тонируются цветом роли. Лежат в Assets/Gizmos (папка только
        // редактора). Источник и лицензия — Assets/Gizmos/VR Battlegrounds/ATTRIBUTION.md.
        // Рисуются через GUI, а не Gizmos.DrawIcon: у того размер мелкий и задаётся общим
        // ползунком Unity, тонкий силуэт автомата в нём теряется.
        private const string IconFolder    = "Assets/Gizmos/VR Battlegrounds/";
        private const string IconMagazine  = "anchor_magazine";
        private const string IconRifle     = "anchor_rifle";
        private const string IconPistol    = "anchor_pistol";
        private const string IconGrabProxy = "anchor_grab_proxy";
        private const float  IconPixels    = 48f;
        private const float  ShadowPixels  = 2f;

        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);

        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> Icons =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawAnchor(UxrGrabbableObjectAnchor anchor, GizmoType gizmoType)
        {
            Transform t        = anchor.transform;
            bool      selected = (gizmoType & GizmoType.Selected) != 0;
            bool      onAvatar = AnchorRole.IsAvatarPocket(anchor);
            Color     color    = GetColor(anchor, onAvatar);

            // Куб в осях якоря: и метка, и область для клика мышью.
            Gizmos.matrix = t.localToWorldMatrix;
            Gizmos.color  = new Color(color.r, color.g, color.b, selected ? 0.45f : 0.25f);
            Gizmos.DrawCube(Vector3.zero, Vector3.one * BoxSize);
            Gizmos.color = color;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one * BoxSize);
            Gizmos.matrix = Matrix4x4.identity;

            DrawAxes(t);
            DrawProxy(anchor, color);

            // Зоны досягаемости — только у выделенного якоря или его прокси: у всех сразу
            // они сливаются в кашу.
            UxrGrabbableObject proxy = anchor.GrabProxy;
            if (selected || (proxy != null && Selection.Contains(proxy.gameObject)))
            {
                DrawReachZones(anchor, color);
            }

            if (onAvatar)
            {
                DrawIcon(t.position, GetIcon(anchor), color);
            }

            // Подписи — только у якорей аватара и у выделенного: на стене арсенала их десятки.
            if (onAvatar || selected)
            {
                string label = selected ? $"{anchor.name}\n{GetTagsLine(anchor)}" : anchor.name;
                Handles.Label(t.position + Vector3.up * (BoxSize * 0.9f), label, LabelStyle(color));
            }
        }

        private static Color GetColor(UxrGrabbableObjectAnchor anchor, bool onAvatar)
        {
            return AnchorRole.GetColor(AnchorRole.Get(anchor));
        }

        // Иконка — только у карманов аватара: у оружия и стены арсенала якорей десятки.
        private static string GetIcon(UxrGrabbableObjectAnchor anchor)
        {
            switch (AnchorRole.Get(anchor))
            {
                case AnchorRoleKind.Magazine:  return IconMagazine;
                case AnchorRoleKind.Primary:   return IconRifle;
                case AnchorRoleKind.Secondary: return IconPistol;
                default:                       return null;
            }
        }

        private static void DrawAxes(Transform t)
        {
            Vector3 p = t.position;
            Handles.color = Color.red;
            Handles.DrawLine(p, p + t.right * AxisSize);
            Handles.color = Color.green;
            Handles.DrawLine(p, p + t.up * AxisSize);
            Handles.color = Color.blue;
            Handles.DrawLine(p, p + t.forward * AxisSize, 2f);
        }

        private static void DrawProxy(UxrGrabbableObjectAnchor anchor, Color color)
        {
            UxrGrabbableObject proxy = anchor.GrabProxy;
            if (proxy == null || proxy.transform.IsChildOf(anchor.transform)) return;

            Vector3 p = proxy.transform.position;
            Handles.color = color;
            Handles.DrawDottedLine(anchor.transform.position, p, 3f);
            Gizmos.color = color;
            Gizmos.DrawWireSphere(p, ProxySize);
            DrawIcon(p, IconGrabProxy, color);
        }

        /// <summary>
        ///     Где предмет встанет в якорь (сплошной контур) и где рука возьмёт его через прокси
        ///     (пунктирный). Считает <see cref="AnchorReachZones" /> — то же, что проверяет SDK.
        /// </summary>
        private static void DrawReachZones(UxrGrabbableObjectAnchor anchor, Color color)
        {
            ReachZone place = AnchorReachZones.GetPlaceZone(anchor);
            DrawZone(place, color, false);
            Handles.Label(place.Center + Vector3.down * (place.Radius + 0.02f), $"укладка ≤ {place.Radius:0.00} м", LabelStyle(color));

            UxrGrabbableObject proxy = anchor.GrabProxy;
            if (proxy == null) return;

            UxrAvatar avatar = anchor.GetComponentInParent<UxrAvatar>(true);
            Color     grabColor = Color.Lerp(color, Color.white, 0.5f);

            foreach (ReachZone zone in AnchorReachZones.GetGrabZones(proxy, avatar, UxrHandSide.Right))
            {
                DrawZone(zone, grabColor, true);
                string size = zone.Shape == ReachZoneShape.Sphere
                    ? $"хват ≤ {zone.Radius:0.00} м"
                    : $"хват в коробке {zone.Box.size.x:0.00}×{zone.Box.size.y:0.00}×{zone.Box.size.z:0.00}";
                Handles.Label(zone.Center + Vector3.up * 0.03f, size, LabelStyle(grabColor));
            }
        }

        private static void DrawZone(ReachZone zone, Color color, bool dashed)
        {
            if (zone.Shape == ReachZoneShape.Box)
            {
                Gizmos.matrix = zone.Box.transform.localToWorldMatrix;
                Gizmos.color  = new Color(color.r, color.g, color.b, 0.08f);
                Gizmos.DrawCube(zone.Box.center, zone.Box.size);
                Gizmos.color = color;
                Gizmos.DrawWireCube(zone.Box.center, zone.Box.size);
                Gizmos.matrix = Matrix4x4.identity;
                return;
            }

            Gizmos.color = new Color(color.r, color.g, color.b, 0.07f);
            Gizmos.DrawSphere(zone.Center, zone.Radius);

            // Три окружности вместо проволочной сферы Gizmos: читаются при любом ракурсе.
            Handles.color = color;
            if (dashed)
            {
                DrawDashedCircle(zone.Center, Vector3.up, zone.Radius);
                DrawDashedCircle(zone.Center, Vector3.right, zone.Radius);
                DrawDashedCircle(zone.Center, Vector3.forward, zone.Radius);
            }
            else
            {
                Handles.DrawWireDisc(zone.Center, Vector3.up, zone.Radius, 2f);
                Handles.DrawWireDisc(zone.Center, Vector3.right, zone.Radius, 2f);
                Handles.DrawWireDisc(zone.Center, Vector3.forward, zone.Radius, 2f);
            }
        }

        private static void DrawDashedCircle(Vector3 center, Vector3 normal, float radius)
        {
            const int segments = 48;
            Vector3 from = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized * radius;

            for (int i = 0; i < segments; i += 2)
            {
                Vector3 a = Quaternion.AngleAxis(360f * i / segments, normal) * from;
                Vector3 b = Quaternion.AngleAxis(360f * (i + 1) / segments, normal) * from;
                Handles.DrawLine(center + a, center + b);
            }
        }

        /// <summary>Иконка постоянного экранного размера поверх геометрии, с тонировкой.</summary>
        private static void DrawIcon(Vector3 worldPosition, string iconName, Color tint)
        {
            if (iconName == null) return;

            Texture2D texture = LoadIcon(iconName);
            Camera    camera  = Camera.current;
            if (texture == null || camera == null) return;

            // За камерой WorldToGUIPoint отражает точку — не рисуем.
            if (camera.WorldToViewportPoint(worldPosition).z <= 0f) return;

            Vector2 center = HandleUtility.WorldToGUIPoint(worldPosition);
            Rect    rect   = new Rect(center.x - IconPixels * 0.5f, center.y - IconPixels * 0.5f, IconPixels, IconPixels);

            Handles.BeginGUI();
            Color previous = GUI.color;

            // Тёмная тень вокруг силуэта — иначе светлая иконка теряется на бежевой форме.
            GUI.color = ShadowColor;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                GUI.DrawTexture(new Rect(rect.x + dx * ShadowPixels, rect.y + dy * ShadowPixels, rect.width, rect.height),
                                texture, ScaleMode.ScaleToFit, true);
            }

            GUI.color = tint;
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
            Handles.EndGUI();
        }

        private static Texture2D LoadIcon(string iconName)
        {
            if (!Icons.TryGetValue(iconName, out Texture2D texture) || texture == null)
            {
                texture          = AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + iconName + ".png");
                Icons[iconName] = texture;
            }

            return texture;
        }

        private static string GetTagsLine(UxrGrabbableObjectAnchor anchor)
        {
            SerializedProperty tags = new SerializedObject(anchor).FindProperty("_compatibleTags");
            if (tags == null || tags.arraySize == 0) return "(любые предметы)";

            string[] names = new string[tags.arraySize];
            for (int i = 0; i < names.Length; i++) names[i] = tags.GetArrayElementAtIndex(i).stringValue;
            return string.Join(", ", names);
        }

        private static GUIStyle LabelStyle(Color color)
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel);
            style.normal.textColor = color;
            return style;
        }
    }
}
