using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Общее для правил запечённого света: LightmapSettings, Lightmapping и RenderSettings читаются только у активной
    /// сцены, поэтому проверка делает проверяемую сцену активной и возвращает прежнюю. Цели ищутся по компонентам и флагам,
    /// без путей конкретной карты. Объект сцены адресуется <see cref="GlobalObjectId"/> — имена светильников повторяются.
    /// </summary>
    internal static class BakedLighting
    {
        public static void WithActive(Scene scene, Action action)
        {
            Scene previous = SceneManager.GetActiveScene();
            bool switched = previous != scene && SceneManager.SetActiveScene(scene);
            try { action(); }
            finally
            {
                if (switched && previous.IsValid())
                    SceneManager.SetActiveScene(previous);
            }
        }

        public static IEnumerable<T> All<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(false));

        public static bool ContributesGi(GameObject gameObject) =>
            (GameObjectUtility.GetStaticEditorFlags(gameObject) & StaticEditorFlags.ContributeGI) != 0;

        /// <summary>Сцена настроена на запечённый свет: есть ассет LightingSettings и в нём Baked Global Illumination.</summary>
        public static bool UsesBakedGi(out LightingSettings settings) =>
            Lightmapping.TryGetLightingSettings(out settings) && settings != null && settings.bakedGI;

        public static string FixTarget(UnityEngine.Object target) => GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();

        public static string PathOf(Transform transform) => AuditAssetUtil.HierarchyPath(transform);
    }

    /// <summary>
    /// QPM-LIGHT-001: реалтайм/смешанный источник в статичной сцене (любой карты, запечённой или нет). Источник
    /// считается каждый кадр (тени, освещение по пикселям) вместо готовой лайтмапы.
    /// </summary>
    public sealed class RealtimeLightInBakedSceneRule : AuditRule, ISceneAuditCheck
    {
        public override string Id => "lighting.realtime-light";
        public override string Title => "Реалтайм-свет в сцене";
        public override string Category => "lighting";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Realtime/Mixed источник освещает каждый пиксель каждый кадр (а с тенями — ещё и проход теней); на тайловом GPU " +
            "Quest это прямые миллисекунды. В запечённой сцене тот же свет уже лежит в лайтмапе и пробах почти бесплатно.";

        public override string Evidence =>
            "QPM-LIGHT-001 (Docs/quest-performance-manifest.md); Lobby: весь свет переведён в Baked при запекании ночи " +
            "(задача lobby-decoration 2026-10-13, scene-detail 2026-10-20). " + AuditEvidence.PerformanceExpertTricks;

        public override string FixHint => "Режим источника Baked и перезапечь свет (фиксер меняет только режим).";

        public void CheckScene(Scene scene, string scenePath, AuditContext context)
        {
            foreach (Light light in BakedLighting.All<Light>(scene))
            {
                if (!light.enabled || light.lightmapBakeType == LightmapBakeType.Baked)
                    continue;
                context.Report(this, scenePath, "источник " + light.lightmapBakeType + " в статичной сцене")
                    .WithGroup("realtime:" + scenePath + "|" + light.lightmapBakeType)
                    .WithItem(BakedLighting.PathOf(light.transform) + " (" + light.type + ")")
                    .WithFixTarget(BakedLighting.FixTarget(light));
            }
        }
    }

    /// <summary>
    /// Сцена со светом (любой источник) или запекаемым содержимым (ContributeGI), у которой нет запечённого света:
    /// не назначен LightingSettings, выключен Baked GI, нет LightingDataAsset или лайтмап.
    /// </summary>
    public sealed class SceneNotBakedRule : AuditRule, ISceneAuditCheck
    {
        public override string Id => "lighting.scene-not-baked";
        public override string Title => "Свет сцены не запечён";
        public override string Category => "lighting";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Без запекания статичные объекты освещает только ambient и реалтайм-источники: либо сцена тёмная и плоская, " +
            "либо свет считается каждый кадр. Запечённые лайтмапы и пробы — основной дешёвый свет на Quest.";

        public override string Evidence => "QPM-LIGHT-001 (Docs/quest-performance-manifest.md). " + AuditEvidence.PerformanceExpertTricks;

        public override string FixHint =>
            "Назначить сцене ассет LightingSettings (Baked GI), перевести источники в Baked и запечь (Window → Rendering → Lighting → Generate Lighting).";

        public void CheckScene(Scene scene, string scenePath, AuditContext context)
        {
            BakedLighting.WithActive(scene, () =>
            {
                int lights = BakedLighting.All<Light>(scene).Count(l => l.enabled);
                List<MeshRenderer> gi = BakedLighting.All<MeshRenderer>(scene)
                    .Where(r => r.enabled && BakedLighting.ContributesGi(r.gameObject)).ToList();
                if (lights == 0 && gi.Count == 0)
                    return;

                var reasons = new List<string>();
                if (!Lightmapping.TryGetLightingSettings(out LightingSettings settings) || settings == null)
                    reasons.Add("не назначен ассет LightingSettings");
                else if (!settings.bakedGI)
                    reasons.Add("в LightingSettings выключен Baked Global Illumination");
                if (Lightmapping.lightingDataAsset == null)
                    reasons.Add("нет запечённых данных (LightingDataAsset)");
                bool wantsLightmaps = gi.Any(r => r.receiveGI == ReceiveGI.Lightmaps);
                if (wantsLightmaps && LightmapSettings.lightmaps.Length == 0)
                    reasons.Add("рендереры ждут лайтмапы, а лайтмап нет");
                if (reasons.Count == 0)
                    return;

                context.Report(this, scenePath, "свет не запечён: " + string.Join("; ", reasons))
                    .WithMetric("contributeGiRenderers", gi.Count)
                    .WithMetric("lights", lights);
            });
        }
    }

    /// <summary>
    /// QPM-LIGHT-001: ContributeGI у подвижного объекта (под Rigidbody, Animator или NetworkIdentity). Его запечённая тень
    /// и отражённый свет остаются на месте, когда объект уезжает, а сам он освещён лайтмапой прежнего положения.
    /// </summary>
    public sealed class DynamicObjectInGiRule : AuditRule, ISceneAuditCheck
    {
        [AuditThreshold("Компоненты, наличие которых на объекте или его родителе делает объект подвижным (имена типов)")]
        public string[] MovingComponents = { "Rigidbody", "Animator", "NetworkIdentity" };

        public override string Id => "lighting.dynamic-in-gi";
        public override string Title => "Подвижный объект в запечённом свете";
        public override string Category => "lighting";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Запекание считает объект неподвижным: его тень и отражённый свет запекаются в окружение и остаются на старом " +
            "месте, а сам объект после движения освещён чужой лайтмапой. Подвижное освещают пробы — это бесплатно.";

        public override string Evidence => "QPM-LIGHT-001 (Docs/quest-performance-manifest.md): ContributeGI у подвижных.";

        public override string FixHint => "Снять Contribute GI (фиксер снимает только этот флаг); объект берёт свет из проб.";

        public void CheckScene(Scene scene, string scenePath, AuditContext context)
        {
            foreach (MeshRenderer renderer in BakedLighting.All<MeshRenderer>(scene))
            {
                if (!BakedLighting.ContributesGi(renderer.gameObject))
                    continue;
                string mover = MovingAncestor(renderer.transform);
                if (mover == null)
                    continue;
                context.Report(this, scenePath, "ContributeGI под " + mover)
                    .WithGroup("dynamic-gi:" + scenePath + "|" + mover)
                    .WithItem(BakedLighting.PathOf(renderer.transform))
                    .WithFixTarget(BakedLighting.FixTarget(renderer.gameObject));
            }
        }

        private string MovingAncestor(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                foreach (Component component in current.GetComponents<Component>())
                {
                    if (component != null && Array.IndexOf(MovingComponents, component.GetType().Name) >= 0)
                        return component.GetType().Name;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Рендерер ждёт лайтмапу (ContributeGI, Receive GI: Lightmaps), а у меша нет UV2: лайтмаппер берёт UV0, где острова
    /// перекрываются и повторяются, — пятна и швы, либо объект вовсе не попадает в лайтмапу.
    /// </summary>
    public sealed class LightmappedMeshWithoutUv2Rule : AuditRule, ISceneAuditCheck
    {
        public override string Id => "mesh.lightmapped-without-uv2";
        public override string Title => "Меш в лайтмапе без UV2";
        public override string Category => "lighting";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Без UV2 запечённый свет ложится по UV0: перекрытые и повторённые острова дают пятна и швы. Исправлять " +
            "приходится реалтайм-светом или дорогим шейдером, а UV2 в импорте бесплатна.";

        public override string Evidence =>
            "Lobby 2026-10-20 (scene-detail): пол шатра без UV2 освещался одной пробой; после меша с UV2 — лайтмапа. " +
            AuditEvidence.LobbyQuest3PerDraw;

        public override string FixHint =>
            "Меш модели — своя копия с UV2 для этой сцены (QPM-LIGHT-003: общий FBX не трогать); сгенерированный .asset-меш — " +
            "развёртка UV2 на месте. Фиксер делает оба варианта, затем перезапечь свет.";

        public void CheckScene(Scene scene, string scenePath, AuditContext context)
        {
            foreach (MeshRenderer renderer in BakedLighting.All<MeshRenderer>(scene))
            {
                if (!renderer.enabled || renderer.receiveGI != ReceiveGI.Lightmaps || !BakedLighting.ContributesGi(renderer.gameObject))
                    continue;
                Mesh mesh = AuditAssetUtil.GetMesh(renderer);
                if (mesh == null || mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                    continue;
                string meshPath = AssetDatabase.GetAssetPath(mesh);
                string label = AuditAssetUtil.AssetLabel(mesh);
                context.Report(this, string.IsNullOrEmpty(meshPath) ? label : meshPath, "меш без UV2 ждёт лайтмапу")
                    .WithGroup("uv2:" + label)
                    .WithItem(scenePath + ":" + BakedLighting.PathOf(renderer.transform))
                    .WithFixTarget(BakedLighting.FixTarget(renderer.gameObject));
            }
        }
    }

    /// <summary>
    /// Игровая зона далеко от light probes: подвижные объекты (игрок, оружие, боты) освещаются ближайшей пробой,
    /// и в дыре сетки они темнеют или берут свет соседнего помещения. Точки — пол арены <see cref="PhysicalArenaDefinition"/>
    /// с шагом сетки, места у стен <see cref="ArsenalStationAnchor"/> и точки <see cref="TeamSpawnZone"/> на высоте рук.
    /// </summary>
    public sealed class GameplayZoneWithoutProbesRule : AuditRule, ISceneAuditCheck
    {
        [AuditThreshold("Шаг сетки точек по полу арены (м)")]
        public float FloorGridStep = 2f;

        [AuditThreshold("Высота проверки над полом, точкой спавна и местом у стены — руки и оружие (м)")]
        public float HandHeight = 1.2f;

        [AuditThreshold("Предельное расстояние до ближайшей пробы (м)")]
        public float MaxProbeDistance = 3f;

        public override string Id => "lighting.zone-without-probes";
        public override string Title => "Игровая зона без light probes";
        public override string Category => "lighting";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Подвижное в запечённой сцене освещают только пробы. Без проб рядом руки, оружие и аватары берут свет далёкой " +
            "пробы — темнеют или светятся чужим светом; лечить приходится реалтайм-светом, который на Quest дорог.";

        public override string Evidence =>
            "Lobby: сетка NightLightProbes по полу арены и местам у стен (lobby-decoration 2026-10-13); металл оружия без проб " +
            "и отражений — " + AuditEvidence.LobbyWeaponReflection;

        public override string FixHint =>
            "Light Probe Group над полом арены (шаг ≤ порога) и у стен арсенала/спавнов на высоте рук, затем перезапечь свет.";

        public void CheckScene(Scene scene, string scenePath, AuditContext context)
        {
            var points = new List<KeyValuePair<string, Vector3>>();
            foreach (PhysicalArenaDefinition arena in BakedLighting.All<PhysicalArenaDefinition>(scene))
            {
                if (!arena.TryFloorBounds(out Bounds floor) || FloorGridStep <= 0f)
                    continue;
                string name = BakedLighting.PathOf(arena.transform) + " (пол)";
                for (float x = floor.min.x + FloorGridStep * 0.5f; x < floor.max.x; x += FloorGridStep)
                    for (float z = floor.min.z + FloorGridStep * 0.5f; z < floor.max.z; z += FloorGridStep)
                        points.Add(new KeyValuePair<string, Vector3>(name, new Vector3(x, floor.max.y + HandHeight, z)));
            }
            foreach (ArsenalStationAnchor anchor in BakedLighting.All<ArsenalStationAnchor>(scene))
                points.Add(new KeyValuePair<string, Vector3>(BakedLighting.PathOf(anchor.transform) + " (место у стены)",
                    anchor.StandingPosition + Vector3.up * HandHeight));
            foreach (TeamSpawnZone zone in BakedLighting.All<TeamSpawnZone>(scene))
                points.Add(new KeyValuePair<string, Vector3>(BakedLighting.PathOf(zone.transform) + " (спавн)",
                    zone.SpawnPoint.position + Vector3.up * HandHeight));
            if (points.Count == 0)
                return;

            BakedLighting.WithActive(scene, () =>
            {
                if (!BakedLighting.UsesBakedGi(out _))
                    return;
                LightProbes probes = LightmapSettings.lightProbes;
                Vector3[] positions = probes != null ? probes.positions : null;
                if (positions == null || positions.Length == 0)
                {
                    context.Report(this, scenePath, "в запечённой сцене нет light probes, а игровых точек " + points.Count)
                        .WithMetric("points", points.Count);
                    return;
                }
                float limit = MaxProbeDistance * MaxProbeDistance;
                foreach (IGrouping<string, KeyValuePair<string, Vector3>> zone in points.GroupBy(p => p.Key))
                {
                    int uncovered = 0;
                    float worst = 0f;
                    foreach (KeyValuePair<string, Vector3> point in zone)
                    {
                        float best = positions.Min(p => (p - point.Value).sqrMagnitude);
                        if (best <= limit)
                            continue;
                        uncovered++;
                        worst = Mathf.Max(worst, Mathf.Sqrt(best));
                    }
                    if (uncovered == 0)
                        continue;
                    context.Report(this, scenePath, zone.Key + ": точек без пробы ближе " + MaxProbeDistance + " м — " +
                                                    uncovered + " из " + zone.Count())
                        .WithGroup("probes:" + scenePath + "|" + zone.Key)
                        .WithItem(zone.Key)
                        .WithMetric("uncovered", uncovered)
                        .WithMetric("worstDistanceM", worst);
                }
            });
        }
    }
}
