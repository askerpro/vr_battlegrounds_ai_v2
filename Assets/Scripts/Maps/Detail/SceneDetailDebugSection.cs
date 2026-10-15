using System;
using System.IO;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.Maps.Detail
{
    /// <summary>
    /// Секция «Детализация сцены» для планшета «Отладка» (задача scene-detail): правка живого профиля
    /// <see cref="SceneDetailApplier.Live" /> кнопками «−»/«+», изменения видны сразу. Только в
    /// Development-сборке; ассет профиля не меняется — «Сохранить» пишет JSON в persistentDataPath,
    /// редактор забирает его со шлема (<c>Tools/VR Battlegrounds/Detail/Забрать профиль со шлема</c>).
    /// Экран вызывает <see cref="Build" /> в своей вкладке и учитывает <see cref="Snapshot" /> при перестройке.
    /// </summary>
    public static class SceneDetailDebugSection
    {
        public static readonly float[] MidStartSteps = { 5f, 10f, 15f, 20f, 25f, 30f };
        public static readonly float[] FarStartSteps = { 10f, 15f, 20f, 25f, 30f, 40f, 50f };
        public static readonly float[] CullSteps = { 0f, .2f, .35f, .5f, SceneDetailProfile.MaxCullSize };
        private static readonly int[] LodSteps = { 0, 1, 2, SceneDetailProfile.WorstLod };

        private static string _status = "";

        /// <summary>Строка состояния для снимка перестройки экрана; пусто — секции нет.</summary>
        public static string Snapshot()
        {
            SceneDetailApplier applier = SceneDetailApplier.Current;
            if (applier == null || !Debug.isDebugBuild) return "";
            SceneDetailProfile p = applier.Live;
            return $"{p.midStart}|{p.farStart}|{Rule(p.mid)}|{Rule(p.far)}|{_status}";
        }

        public static void Build(RectTransform content, Action invalidate)
        {
            SceneDetailApplier applier = SceneDetailApplier.Current;
            if (applier == null || !Debug.isDebugBuild) return;
            SceneDetailProfile p = applier.Live;

            MenuKit.Section(content, "Детализация сцены");
            Row(content, $"Средний план с {p.midStart:0} м", d => p.midStart = Step(MidStartSteps, p.midStart, d), applier, invalidate);
            Row(content, $"Дальний план с {Mathf.Max(p.farStart, p.midStart):0} м", d => p.farStart = Step(FarStartSteps, p.farStart, d), applier, invalidate);
            PlaneRows(content, "Средний", () => p.mid, r => p.mid = r, applier, invalidate);
            PlaneRows(content, "Дальний", () => p.far, r => p.far = r, applier, invalidate);

            RectTransform save = MenuKit.Row(content);
            MenuKit.Button(save, "Сохранить профиль", () => { _status = Save(p, applier.gameObject.scene.name); invalidate(); });
            if (!string.IsNullOrEmpty(_status)) MenuKit.Label(content, _status, MenuTextRole.Caption, MenuColorRole.TextSecondary);
        }

        private static void PlaneRows(RectTransform content, string plane, Func<SceneDetailProfile.PlaneRule> get,
                                      Action<SceneDetailProfile.PlaneRule> set, SceneDetailApplier applier, Action invalidate)
        {
            SceneDetailProfile.PlaneRule rule = get();
            string lod = rule.lodSkip == 0 ? "как задано" : rule.lodSkip >= SceneDetailProfile.WorstLod ? "только худший" : $"без {rule.lodSkip} первых";
            Row(content, $"{plane}: LOD {lod}", d => { var r = get(); r.lodSkip = StepInt(LodSteps, r.lodSkip, d); set(r); }, applier, invalidate);
            Row(content, $"{plane}: текстуры /{1 << rule.textureBias}", d => { var r = get(); r.textureBias = Mathf.Clamp(r.textureBias + d, 0, SceneDetailProfile.MaxTextureBias); set(r); }, applier, invalidate);
            Row(content, $"{plane}: без объектов мельче {rule.cullSize * 100f:0} см", d => { var r = get(); r.cullSize = Step(CullSteps, r.cullSize, d); set(r); }, applier, invalidate);
        }

        private static void Row(RectTransform content, string label, Action<int> change, SceneDetailApplier applier, Action invalidate)
        {
            RectTransform row = MenuKit.Row(content);
            MenuKit.Label(row, label);
            MenuKit.Button(row, "−", () => { change(-1); applier.Apply(); invalidate(); });
            MenuKit.Button(row, "+", () => { change(+1); applier.Apply(); invalidate(); });
        }

        private static float Step(float[] steps, float current, int delta)
        {
            int index = 0;
            for (int i = 0; i < steps.Length; i++) if (Mathf.Abs(steps[i] - current) < Mathf.Abs(steps[index] - current)) index = i;
            return steps[Mathf.Clamp(index + delta, 0, steps.Length - 1)];
        }

        private static int StepInt(int[] steps, int current, int delta)
        {
            int index = 0;
            for (int i = 0; i < steps.Length; i++) if (Mathf.Abs(steps[i] - current) < Mathf.Abs(steps[index] - current)) index = i;
            return steps[Mathf.Clamp(index + delta, 0, steps.Length - 1)];
        }

        private static string Rule(SceneDetailProfile.PlaneRule r) => $"{r.lodSkip}/{r.textureBias}/{r.cullSize}";

        /// <summary>Путь сохранённого профиля сцены на устройстве (от persistentDataPath).</summary>
        public static string SavedPath(string scene) => Path.Combine("detail", scene + ".json");

        private static string Save(SceneDetailProfile profile, string scene)
        {
            string path = Path.Combine(Application.persistentDataPath, SavedPath(scene));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(profile, true));
                GameLog.Perf.Info("[SceneDetail] Профиль сохранён: " + path);
                return "Сохранено: " + path;
            }
            catch (Exception e)
            {
                GameLog.Error("[SceneDetail] Профиль не сохранён: " + e.Message);
                return "Не сохранено: " + e.Message;
            }
        }
    }
}
