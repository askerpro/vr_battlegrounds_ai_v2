using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Зона оружия (стена арсенала, место у стены, точка спавна) без пробы отражений, когда отражение окружения
    /// намного темнее рассеянного света в этой точке. Металл почти не рассеивает свет: его цвет — отражение, и без пробы
    /// он отражает небо сцены. Рассеянный свет берётся из light probes (в них запечены лампы), отражение окружения —
    /// из ambient probe сцены × Reflection Intensity. На время проверки сцена делается активной: RenderSettings читаются
    /// только у активной сцены.
    /// </summary>
    public sealed class DarkReflectionZoneRule : AuditRule, ISceneAuditCheck
    {
        private static readonly Vector3[] Directions =
            { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        [AuditThreshold("Во сколько раз рассеянный свет в зоне ярче отражения окружения, чтобы металл выглядел чёрным")]
        public float MinDiffuseToReflectionRatio = 2f;

        [AuditThreshold("Высота точки спавна и места у стены арсенала, на которой держат оружие (м)")]
        public float HandHeight = 1.2f;

        public override string Id => "lighting.dark-reflection-zone";
        public override string Title => "Зона оружия без пробы отражений";
        public override string Category => "lighting";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Цвет металла (Metallic ≈ 1) — почти целиком отражение. Без Reflection Probe объект отражает небо сцены: " +
            "в помещении ночью металл оружия становится чёрным, хотя руки и матовые детали рядом освещены лампами. " +
            "Запечённая проба стоит на Quest ≈ 0: Lit и так читает кубмапу, меняется только её содержимое.";

        public override string Evidence => AuditEvidence.LobbyWeaponReflection;

        public override string FixHint =>
            "Запечённая Reflection Probe (Box Projection) на помещение/зону; при открытом помещении — повысить её Intensity " +
            "или задать тёплый Custom-источник отражений окружения. Смешивание проб не включать.";

        public void CheckScene(Scene scene, string scenePath, AuditContext context)
        {
            var zones = new List<KeyValuePair<string, Vector3>>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ArsenalStationAnchor anchor in root.GetComponentsInChildren<ArsenalStationAnchor>(false))
                {
                    zones.Add(new KeyValuePair<string, Vector3>(PathOf(anchor.transform) + " (стена)", anchor.RaisedBoundsWorld.center));
                    zones.Add(new KeyValuePair<string, Vector3>(PathOf(anchor.transform) + " (место у стены)",
                        anchor.StandingPosition + Vector3.up * HandHeight));
                }
                foreach (TeamSpawnZone zone in root.GetComponentsInChildren<TeamSpawnZone>(false))
                    zones.Add(new KeyValuePair<string, Vector3>(PathOf(zone.transform) + " (спавн)", zone.SpawnPoint.position + Vector3.up * HandHeight));
            }
            if (zones.Count == 0)
                return;

            var probes = new List<ReflectionProbe>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ReflectionProbe probe in root.GetComponentsInChildren<ReflectionProbe>(false))
                {
                    if (probe.enabled && HasTexture(probe))
                        probes.Add(probe);
                }
            }

            Scene previous = SceneManager.GetActiveScene();
            bool switched = previous != scene && SceneManager.SetActiveScene(scene);
            try
            {
                if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom && RenderSettings.customReflectionTexture != null)
                    return; // Свой источник отражений задан намеренно; яркость кубмапы здесь не оценить.
                float reflection = Luminance(RenderSettings.ambientProbe) * RenderSettings.reflectionIntensity;
                foreach (KeyValuePair<string, Vector3> zone in zones)
                {
                    if (Covered(probes, zone.Value))
                        continue;
                    LightProbes.GetInterpolatedProbe(zone.Value, null, out SphericalHarmonicsL2 sh);
                    float diffuse = Luminance(sh);
                    float ratio = diffuse / Mathf.Max(reflection, 1e-4f);
                    if (ratio < MinDiffuseToReflectionRatio)
                        continue;
                    // Одна находка на сцену: зоны — в списке, у каждой своё отношение.
                    context.Report(this, scenePath,
                            "зоны оружия без пробы отражений: отражение окружения в " + MinDiffuseToReflectionRatio.ToString("0.#") +
                            "+ раза темнее рассеянного света — металл выглядит чёрным")
                        .WithGroup("refl:" + scenePath)
                        .WithItem(zone.Key + " — в " + ratio.ToString("0.#") + " раза")
                        .WithMetric("ratio", ratio)
                        .WithMetric("diffuse", diffuse)
                        .WithMetric("environmentReflection", reflection)
                        .WithMetric("point", zone.Value.ToString("F1"));
                }
            }
            finally
            {
                if (switched && previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
            }
        }

        private static bool HasTexture(ReflectionProbe probe) =>
            probe.mode == ReflectionProbeMode.Realtime
            || (probe.mode == ReflectionProbeMode.Baked && probe.bakedTexture != null)
            || (probe.mode == ReflectionProbeMode.Custom && probe.customBakedTexture != null);

        private static bool Covered(List<ReflectionProbe> probes, Vector3 point)
        {
            foreach (ReflectionProbe probe in probes)
            {
                if (probe.bounds.Contains(point))
                    return true;
            }
            return false;
        }

        /// <summary>Средняя по шести направлениям яркость освещения из SH (как видит его матовая поверхность).</summary>
        private static float Luminance(SphericalHarmonicsL2 sh)
        {
            var colors = new Color[Directions.Length];
            sh.Evaluate(Directions, colors);
            float sum = 0f;
            foreach (Color color in colors)
                sum += Mathf.Max(0f, color.grayscale);
            return sum / colors.Length;
        }

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
    }
}
