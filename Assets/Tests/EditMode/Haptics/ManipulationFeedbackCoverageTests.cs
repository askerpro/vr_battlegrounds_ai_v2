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
    /// Ни один якорь и ни один хватаемый предмет игры не остаётся без отклика незаметно. Для каждого якоря и корневого
    /// хватаемого предмета в префабах игры вычисляется клип по тому же порядку, что и в игре: <see cref="HapticOverride" />
    /// объекта → роль из <see cref="HapticRoles" />. Объект без отклика допустим только в списке
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
        private const string WeaponSocket = "гнездо магазина/патрона: роль World пустая — вибрация готовности гнезда решается " +
                                            "на этапе grabber-readiness (подсветку гнезда даёт WeaponMagazineAnchorHighlight, область weapon-system)";
        private const string ArsenalSlot = "слот арсенала/жетона: роль World пустая — вибрация готовности слота решается на этапе grabber-readiness";
        private const string ItemNoGrab = "хват предмета: роль ItemGrab пустая — готовность взять (ItemInReach) — этап grabber-readiness, " +
                                          "щелчок хвата SDK (UxrManipulationHapticFeedback) — этап item-clicks";

        private static readonly Dictionary<string, string> NoFeedbackYet = new Dictionary<string, string>
        {
            ["Assets/Prefabs/Arsenal/DogTag/DogTagPanel.prefab | DogTagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_AK105/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_AK105/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_M16/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_M16/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_Machinegun/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_Machinegun/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_SRM12_SightReview/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_SRM12_SightReview/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_Scar/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_Scar/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_Shotgun/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_Shotgun/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_SniperRifle/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_SniperRifle/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_SniperRifle_SightReview/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_SniperRifle_SightReview/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_TR15_SightReview/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/RiflesSlotsContainer/PegboardSlot_TR15_SightReview/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_MKR9_SightReview/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_MKR9_SightReview/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_MP5K/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_MP5K/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_PPK/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_PPK/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_R08/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_R08/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_SDKGun/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_SDKGun/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_Viper_SightReview/MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab | PresentationRoot/ShelfRoot/ShelfSlotsContainer/ShelfSlot_Viper_SightReview/WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/Slots/FireArmSlotPrefab.prefab | MagAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/Slots/FireArmSlotPrefab.prefab | WeaponAnchor | готовность якоря"] = ArsenalSlot,
            ["Assets/Prefabs/Arsenal/DogTag/DogTagPanel.prefab | DogTag | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/UI/Menu/Tablet/Tablet_Base.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/AK105/AK105.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/AR15/AR15.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/AR15/AR15_Magazine.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower_Magazine.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS_Ammo.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/Herrington/Herrington.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/MKR9/MKR9.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/MP5K/MP5K.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/Mk14/Mk14.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/PPK/PPK.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/R08/R08.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/Revolver/Revolver.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/SRM12/SRM12.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/Scar/Scar.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/SniperRifle/SniperRifle.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/TR15/TR15.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/Uzi/Uzi.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/Viper/Viper.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/Grenade.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/Gun.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/Machinegun.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/MagGun.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/MagMachinegun.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/MagShotgun.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/Shotgun.prefab | (корень) | хват предмета"] = ItemNoGrab,
            ["Assets/Prefabs/Weapons/AK105/AK105.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/AR15/AR15.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab | CartridgeIntake | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/Herrington/Herrington.prefab | CartridgeIntake | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/Herrington/Herrington.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/MKR9/MKR9.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/MP5K/MP5K.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/Mk14/Mk14.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/PPK/PPK.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/R08/R08.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/Revolver/Revolver.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/SRM12/SRM12.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/Scar/Scar.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/SniperRifle/SniperRifle.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/TR15/TR15.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/Uzi/Uzi.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/Prefabs/Weapons/Viper/Viper.prefab | MeshContainer/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/Gun.prefab | GunGeo/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/Machinegun.prefab | MachinegunGeo/MagAnchor | готовность якоря"] = WeaponSocket,
            ["Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/Shotgun.prefab | ShotGunGeo/MagAnchor | готовность якоря"] = WeaponSocket,
        };

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
            var roles = Resources.Load<HapticRoles>(nameof(HapticRoles));
            Assert.IsNotNull(roles, "Нет Resources/HapticRoles.asset.");

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
                    var own = anchor.GetComponent<HapticOverride>();
                    AnchorRoleKind role = AnchorRole.Get(anchor);
                    UxrHapticClip clip = HapticOverride.Pick(own != null ? own.AnchorReady : null, roles.AnchorReady(role));
                    string source = own != null && own.AnchorReady.HasWaveform ? "HapticOverride" : $"роль {role}";
                    Add($"{Origin(anchor)} | готовность якоря", source, clip != null && clip.HasWaveform);
                }

                foreach (UxrGrabbableObject item in prefab.GetComponentsInChildren<UxrGrabbableObject>(true))
                {
                    // Прокси кармана — вход в якорь, его отклик — готовность якоря. Детали предмета (затвор, помпа, чека) —
                    // часть предмета, не отдельный предмет.
                    if (proxies.Contains(item)) continue;
                    if (item.transform.parent != null && item.transform.parent.GetComponentInParent<UxrGrabbableObject>(true) != null) continue;
                    var own = item.GetComponent<HapticOverride>();
                    UxrHapticClip clip = HapticOverride.Pick(own != null ? own.Grab : null, roles.ItemGrab);
                    string source = own != null && own.Grab.HasWaveform ? "HapticOverride" : "роль ItemGrab";
                    Add($"{Origin(item)} | хват предмета", source, clip != null && clip.HasWaveform);
                }
            }
            return entries.Values.ToList();
        }

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
            Assert.IsEmpty(missing, $"Без отклика ({missing.Length}) — задать роль/HapticOverride или внести в NoFeedbackYet с " +
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
