using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Mirror;
using TMPro;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;
using Debug = UnityEngine.Debug;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Размечает статическую геометрию карт флагами occlusion и запекает occlusion culling
    /// для каждой сцены карты и лобби из Build Settings.
    ///
    /// <para>
    /// Зачем: на Quest 3 кадр упирается в GPU, как только в поле зрения аватары (~100k
    /// треугольников и десятки draw call на каждого). Без запечённых данных frustum culling
    /// рисует аватар за стеной целиком. Аватары динамические (Dynamic Occlusion включён),
    /// им нужны только запечённые окклюдеры — стены и укрытия.
    /// </para>
    /// <para>
    /// Что статично: <see cref="MeshRenderer" />, у которого ни сам объект, ни предки не несут
    /// ничего подвижного (<see cref="Rigidbody" />, <see cref="NetworkIdentity" />,
    /// <see cref="UxrGrabbableObject" />, <see cref="Animator" />/<see cref="Animation" />, стена
    /// арсенала с анимированными панелями, зоны спавна, мишени, UI, частицы). Прозрачное,
    /// вырезанное по альфе и мелкое (&lt; <see cref="SmallPropSize" /> м) — только Occludee:
    /// сквозь него видно или оно ничего не закрывает. С подвижных объектов флаги occlusion
    /// снимаются — окклюдер, который уехал, оставляет в запечённых данных «призрачную» стену.
    /// Выключенные объекты не трогаются вовсе: Umbra их не запекает.
    /// </para>
    /// <para>
    /// Инструмент управляет только битами <c>OccluderStatic</c>/<c>OccludeeStatic</c>, прочие
    /// static-флаги (batching, GI, navigation) остаются как их выставил дизайнер. Повторный
    /// запуск идемпотентен. После любой правки геометрии карты — перезапечь.
    /// </para>
    /// </summary>
    public static class OcclusionBakeTool
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Gameplay/Bake Occlusion (all maps)";

        /// <summary>Сцены с игровой геометрией: лобби и всё в папке карт.</summary>
        public const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        public const string MapsFolder     = "Assets/Scenes/Maps/";

        // ── Параметры запекания ─────────────────────────────────────────────
        // Арена ~15×18 м, укрытия 1.0–2.5 м (Docs/level-design.md). Запекание при таких
        // размерах стоит секунды, поэтому точность не экономим.

        /// <summary>
        /// Размер самого мелкого окклюдера, м. Самое низкое укрытие — 1.0 м, столбы
        /// LD_PillarBox — ~0.5 м в сечении. 0.5 м ловит их все, а мельче воксель лишь
        /// раздувает данные: предметы меньше полуметра аватар не закрывают.
        /// </summary>
        public const float SmallestOccluder = 0.5f;

        /// <summary>
        /// Самая узкая щель, сквозь которую должно быть видно, м. Щель уже этого Umbra
        /// считает сплошной и отсекает то, что за ней. В VR стреляют сквозь щели между
        /// блоками укрытий, поэтому берём 0.1 м: ложное исчезновение противника в щели
        /// хуже, чем лишняя отрисовка.
        /// </summary>
        public const float SmallestHole = 0.1f;

        /// <summary>
        /// 100 — не отбрасывать ячейки по обратным граням. Меньшее значение режет «изнанку»
        /// геометрии, но у блокаута карт много одностороннего меша (плоскости пола, стенки
        /// без торцов), и камера игрока, заглянувшая за укрытие, увидела бы дыры.
        /// </summary>
        public const float BackfaceThreshold = 100f;

        /// <summary>Объекты меньше этого (наибольшее ребро bounds, м) — только Occludee.</summary>
        public const float SmallPropSize = 0.3f;

        private const StaticEditorFlags OcclusionFlags =
            StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

        /// <summary>
        /// Компоненты, после которых объект и всё под ним считаются подвижными.
        /// </summary>
        private static readonly Type[] DynamicMarkers =
        {
            typeof(Rigidbody),
            typeof(NetworkIdentity),
            typeof(UxrGrabbableObject),
            typeof(Animator),
            typeof(Animation),
            typeof(ArsenalWallController),
            typeof(ArsenalAnimator),
            typeof(ArsenalSlotController),
            typeof(DogTagController),
            typeof(TeamSpawnZone),
            typeof(ShootingTarget),
            typeof(Canvas),
            typeof(ParticleSystem),
        };

        public sealed class SceneReport
        {
            public string Path;
            public int    Occluders;
            public int    OccludeeOnly;
            public int    Dynamic;
            public int    Inactive;
            public int    ChangedObjects;
            public double BakeSeconds;
            public bool   HasData;
            public string Skipped;

            public override string ToString() => Skipped != null
                ? $"{Path}: пропущена — {Skipped}"
                : $"{Path}: окклюдеров {Occluders}, только occludee {OccludeeOnly}, динамических {Dynamic}, выключенных (не тронуты) {Inactive}, " +
                  $"флаги изменены у {ChangedObjects}, запекание {BakeSeconds:0.00} с, данные {(HasData ? "есть" : "НЕТ")}";
        }

        [MenuItem(MenuPath)]
        private static void BakeFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[OcclusionBakeTool]\n" + Run());
        }

        /// <summary>
        /// Точка входа для MCP/CI. Открытые сцены с несохранёнными правками не трогаются —
        /// вызов вернёт отказ, чтобы не потерять чужую работу.
        /// </summary>
        public static string Run()
        {
            if (Application.isPlaying) return "Отказ: редактор в Play Mode.";

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                    return $"Отказ: в открытой сцене {open.path} несохранённые правки. Сохрани или отмени их.";
            }

            SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
            var reports = new List<SceneReport>();
            try
            {
                foreach (string path in TargetScenes())
                    reports.Add(BakeScene(path));
            }
            finally
            {
                if (previous.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previous);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Параметры: smallestOccluder {SmallestOccluder} м, smallestHole {SmallestHole} м, backfaceThreshold {BackfaceThreshold}");
            foreach (SceneReport r in reports) sb.AppendLine(r.ToString());
            return sb.ToString();
        }

        /// <summary>Лобби и карты из Build Settings (включённые).</summary>
        public static List<string> TargetScenes()
        {
            var result = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.enabled && IsTargetScene(s.path))
                    result.Add(s.path);
            return result;
        }

        public static bool IsTargetScene(string path) =>
            path == LobbyScenePath || path.StartsWith(MapsFolder, StringComparison.Ordinal);

        private static SceneReport BakeScene(string path)
        {
            var report = new SceneReport { Path = path };

            // Umbra запекает всё загруженное разом, поэтому сцена открывается одна.
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                Classify(renderer, report);

            if (report.ChangedObjects > 0) EditorSceneManager.MarkSceneDirty(scene);

            StaticOcclusionCulling.smallestOccluder  = SmallestOccluder;
            StaticOcclusionCulling.smallestHole      = SmallestHole;
            StaticOcclusionCulling.backfaceThreshold = BackfaceThreshold;

            var sw = Stopwatch.StartNew();
            bool ok = StaticOcclusionCulling.Compute();
            sw.Stop();
            report.BakeSeconds = sw.Elapsed.TotalSeconds;

            if (!ok)
            {
                report.Skipped = "StaticOcclusionCulling.Compute вернул false";
                return report;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.HasData = SceneReferencesOcclusionData(path);
            return report;
        }

        /// <summary>Сохранённая сцена ссылается на ассет OcclusionCullingData.</summary>
        public static bool SceneReferencesOcclusionData(string scenePath)
        {
            foreach (string line in System.IO.File.ReadLines(scenePath))
                if (line.Contains("m_OcclusionCullingData:"))
                    return !line.Contains("{fileID: 0}");
            return false;
        }

        private static void Classify(Renderer renderer, SceneReport report)
        {
            GameObject go = renderer.gameObject;

            // Выключенное Umbra не запекает, а его флаги — чужая разметка (например, образцы
            // UltimateXR под выключенным корнем). Не трогаем: иначе сотни пустых overrides.
            if (!go.activeInHierarchy || !renderer.enabled)
            {
                report.Inactive++;
                return;
            }

            StaticEditorFlags current = GameObjectUtility.GetStaticEditorFlags(go);
            StaticEditorFlags desired = current & ~OcclusionFlags;

            if (!(renderer is MeshRenderer) || IsDynamic(renderer))
            {
                report.Dynamic++;
            }
            else if (IsSeeThrough(renderer) || IsSmall(renderer) || !CastsVisibleSurface(renderer))
            {
                desired |= StaticEditorFlags.OccludeeStatic;
                report.OccludeeOnly++;
            }
            else
            {
                desired |= OcclusionFlags;
                report.Occluders++;
            }

            if (desired == current) return;

            // Флаги ставятся на объект целиком; под одним объектом может быть и другой рендерер.
            Undo.RecordObject(go, "Occlusion static flags");
            GameObjectUtility.SetStaticEditorFlags(go, desired);
            report.ChangedObjects++;
        }

        private static bool IsDynamic(Renderer renderer)
        {
            GameObject go = renderer.gameObject;
            if (go.CompareTag("EditorOnly")) return true;
            if (go.GetComponent<TMP_Text>() != null) return true;

            foreach (Type marker in DynamicMarkers)
                if (go.GetComponentInParent(marker, true) != null) return true;

            // Меш-объём триггера (визуализация зоны) — не стена.
            Collider[] colliders = go.GetComponents<Collider>();
            if (colliders.Length > 0)
            {
                bool solid = false;
                foreach (Collider c in colliders) solid |= !c.isTrigger;
                if (!solid) return true;
            }
            return false;
        }

        /// <summary>Прозрачное или вырезанное по альфе: за ним видно.</summary>
        private static bool IsSeeThrough(Renderer renderer)
        {
            foreach (Material m in renderer.sharedMaterials)
            {
                if (m == null) continue;
                if (m.renderQueue >= (int)RenderQueue.AlphaTest) return true;
                if (m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || m.IsKeywordEnabled("_ALPHATEST_ON")) return true;
            }
            return false;
        }

        private static bool IsSmall(Renderer renderer)
        {
            Vector3 size = renderer.bounds.size;
            return Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < SmallPropSize;
        }

        private static bool CastsVisibleSurface(Renderer renderer) =>
            renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
    }
}
