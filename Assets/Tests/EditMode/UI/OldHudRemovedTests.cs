using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Старый HUD перед глазами снят (T-46): всё, что он показывал, — на наручных часах
    /// (<c>WristDisplay</c>), сообщения — нотификации часов (<c>WatchNotifications</c>).
    ///
    /// <para>
    /// Проверка по тексту ассетов, а не по типам: классов старого HUD больше нет, и забытая ссылка
    /// на них в префабе — это «Missing script», который по типу не найти. GUID удалённых скриптов
    /// и префаба живут в ассетах ровно столько, сколько ссылка. Без Unity API — тест идёт и вне редактора.
    /// </para>
    /// </summary>
    public class OldHudRemovedTests
    {
        /// <summary>GUID скриптов и префаба старого HUD (из их удалённых .meta).</summary>
        private static readonly Dictionary<string, string> OldHudGuids = new Dictionary<string, string>
        {
            ["606334f1f02c0444ebe2f6abd707a3d9"] = "PlayerHUDManager",
            ["be770f8820f39fa4abfc43a9a6bcf045"] = "HUDWidget_GameNotification",
            ["bdf47cd5fa1a6c841968605f239c08fe"] = "HUDWidget_Health",
            ["95c43dad7734b014ead9dce3cb22df64"] = "HUDWidget_MapTimer",
            ["e0adb4a6b0c76894e91ddf040648f0e3"] = "HUDWidget_RoundTimer",
            ["1034cf8fc4db8134fad37f039ab56985"] = "HUDWidget_TeamScore",
            ["6a46ee019375bb94bb3604225fc72821"] = "HUDWidget_EliminationMode_TeamRoundScore",
            ["8f79705957c52a64283e82250c72c8af"] = "EliminationHUD.prefab",
        };

        /// <summary>Имена полей и объектов, через которые HUD вставал в игру.</summary>
        private static readonly string[] OldHudMarkers =
        {
            "_hudContainer:",
            "hudPrefab:",
            "m_Name: HUDContainer",
        };

        private static readonly string[] ScannedRoots = { "Assets/Prefabs", "Assets/Scenes", "Assets/Data" };
        private static readonly string[] ScannedExtensions = { ".prefab", ".unity", ".asset" };

        private static IEnumerable<string> GameAssets() =>
            ScannedRoots.Where(Directory.Exists)
                        .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                        .Where(path => ScannedExtensions.Contains(Path.GetExtension(path)));

        [Test]
        public void Старого_HUD_нет_в_префабах_сценах_и_данных()
        {
            List<string> assets = GameAssets().ToList();
            Assert.That(assets.Count, Is.GreaterThan(10), "Сканер не нашёл ассетов — сломан поиск, а не правило");

            var problems = new List<string>();
            foreach (string path in assets)
            {
                string text = File.ReadAllText(path);
                foreach (KeyValuePair<string, string> old in OldHudGuids)
                    if (text.Contains("guid: " + old.Key))
                        problems.Add($"{path}: ссылка на {old.Value}");

                foreach (string marker in OldHudMarkers)
                    if (text.Contains(marker))
                        problems.Add($"{path}: «{marker}» старого HUD");
            }

            Assert.That(problems, Is.Empty,
                "Старый HUD ещё встроен в игру — его место на часах (T-46):\n" + string.Join("\n", problems));
        }

        [Test]
        public void Скрипты_и_префаб_старого_HUD_удалены()
        {
            var left = new[] { "Assets/Scripts", "Assets/Prefabs", "Assets/Editor" }
                                .SelectMany(root => Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
                                .Where(meta => OldHudGuids.Keys.Any(guid => File.ReadLines(meta).Take(3)
                                                                             .Any(line => line == "guid: " + guid)))
                                .ToList();

            Assert.That(left, Is.Empty, "Остался ассет старого HUD:\n" + string.Join("\n", left));
        }
    }
}
