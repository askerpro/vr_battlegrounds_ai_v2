using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Haptics;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Haptics
{
    /// <summary>
    /// Ни один якорь и ни один хватаемый предмет игры не остаётся без отклика готовности незаметно. Для каждого якоря и
    /// корневого хватаемого предмета в префабах игры префаб отклика выбирается так же, как в игре
    /// (<see cref="InteractionFeedback" />): <see cref="InteractionFeedbackOverride" /> объекта → <see cref="InteractionFeedbackConfig" />
    /// (роль якоря или «в досягаемости»). Объект без отклика допустим только в списке
    /// <see cref="NoFeedbackYet" /> с причиной — так новый предмет или якорь без отклика сразу виден, а текущие пробелы
    /// перечислены. Полный перечень — <c>Temp/HapticFeedbackCoverage.txt</c>. Обзорные и черновые префабы прицелов — не игра.
    /// </summary>
    public class ManipulationFeedbackCoverageTests
    {
        private const string ReportPath = "Temp/HapticFeedbackCoverage.txt";

        private static readonly string[] NotInGame = { "/SightReview/", "/SightCalibrationDrafts/" };

        /// <summary>
        /// Пока без отклика: «префаб | путь объекта | вид» → причина. Строка уходит, когда объект получил отклик (тест
        /// проверяет, что список не устарел).
        /// </summary>
        private static readonly Dictionary<string, string> NoFeedbackYet = new Dictionary<string, string>();

        private readonly struct Entry
        {
            public readonly string Key, Source;
            public readonly bool Covered;

            public Entry(string key, string source, bool covered)
            {
                Key = key;
                Source = source;
                Covered = covered;
            }
        }

        private static List<Entry> Scan()
        {
            var config = Resources.Load<InteractionFeedbackConfig>(nameof(InteractionFeedbackConfig));
            Assert.IsNotNull(config, "Нет Resources/InteractionFeedbackConfig.asset.");

            // Объект оценивается там, где он реально стоит (карман — внутри аватара), а в перечень попадает один раз — по
            // исходному префабу. Отклик засчитывается, если он есть хоть в одном месте использования.
            var entries = new Dictionary<string, Entry>();
            void Add(string key, string source, bool covered)
            {
                if (!entries.TryGetValue(key, out Entry old) || (!old.Covered && covered)) entries[key] = new Entry(key, source, covered);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (NotInGame.Any(path.Contains)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var proxies = new HashSet<UxrGrabbableObject>();
                foreach (UxrGrabbableObjectAnchor anchor in prefab.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
                {
                    if (anchor.GrabProxy != null) proxies.Add(anchor.GrabProxy);
                    var own = anchor.GetComponent<InteractionFeedbackOverride>();
                    AnchorRoleKind role = AnchorRole.Get(anchor);
                    GameObject feedback = own != null && own.Ready != null ? own.Ready : config.AnchorReady(role);
                    string source = own != null && own.Ready != null ? nameof(InteractionFeedbackOverride) : $"конфиг, роль {role}";
                    Add($"{Origin(anchor)} | готовность якоря", Describe(source, feedback), feedback != null);
                }

                foreach (UxrGrabbableObject item in prefab.GetComponentsInChildren<UxrGrabbableObject>(true))
                {
                    // Прокси кармана — вход в якорь, его отклик — готовность якоря. Детали предмета (затвор, помпа, чека) —
                    // часть предмета, не отдельный предмет.
                    if (proxies.Contains(item)) continue;
                    if (item.transform.parent != null && item.transform.parent.GetComponentInParent<UxrGrabbableObject>(true) != null) continue;
                    var own = item.GetComponent<InteractionFeedbackOverride>();
                    GameObject feedback = own != null && own.Ready != null ? own.Ready : config.ItemInReach;
                    string source = own != null && own.Ready != null ? nameof(InteractionFeedbackOverride) : "конфиг, в досягаемости";
                    Add($"{Origin(item)} | готовность взять", Describe(source, feedback), feedback != null);
                }
            }
            return entries.Values.ToList();
        }

        private static string Describe(string source, GameObject feedback) =>
            feedback != null ? $"{source} → {feedback.name}" : source;

        /// <summary>«Исходный префаб | путь объекта в нём» — один ключ для объекта, где бы префаб ни был вложен.</summary>
        private static string Origin(Component component)
        {
            Component source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(component) ?? component;
            GameObject root = source.transform.root.gameObject;
            return $"{AssetDatabase.GetAssetPath(source)} | {RelativePath(root, source.transform)}";
        }

        private static string RelativePath(GameObject root, Transform t)
        {
            var parts = new List<string>();
            for (Transform c = t; c != null && c != root.transform; c = c.parent) parts.Insert(0, c.name);
            return parts.Count == 0 ? "(корень)" : string.Join("/", parts);
        }

        [Test]
        public void У_каждого_якоря_и_предмета_игры_есть_отклик_или_явное_исключение()
        {
            List<Entry> entries = Scan();
            WriteReport(entries);

            string[] missing = entries.Where(e => !e.Covered && !NoFeedbackYet.ContainsKey(e.Key)).Select(e => e.Key).ToArray();
            Assert.IsEmpty(missing, $"Без отклика ({missing.Length}) — задать в конфиге/InteractionFeedbackOverride или внести в NoFeedbackYet с " +
                                    $"причиной. Полный перечень: {ReportPath}\n" + string.Join("\n", missing.Take(40)));
        }

        [Test]
        public void Список_исключений_не_устарел()
        {
            HashSet<string> uncovered = new HashSet<string>(Scan().Where(e => !e.Covered).Select(e => e.Key));
            string[] stale = NoFeedbackYet.Keys.Where(k => !uncovered.Contains(k)).ToArray();
            Assert.IsEmpty(stale, "Объект получил отклик или исчез — убрать из NoFeedbackYet:\n" + string.Join("\n", stale));
        }

        private static void WriteReport(List<Entry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Отклик манипуляций: {entries.Count(e => e.Covered)} с откликом, {entries.Count(e => !e.Covered)} без.");
            foreach (Entry e in entries.OrderBy(e => e.Covered).ThenBy(e => e.Key))
                sb.AppendLine($"{(e.Covered ? "есть " : "НЕТ  ")} [{e.Source}] {e.Key}");
            Directory.CreateDirectory("Temp");
            File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
            TestContext.WriteLine(sb.ToString());
        }
    }
}
