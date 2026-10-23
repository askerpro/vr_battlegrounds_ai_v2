using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Economy;
using VrBattlegrounds.Tests.Arsenal;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Подсветка слотов арсенала (задача arsenal-slot-lighting, принято пользователем в Play 2026-10-23).
    /// <para>
    /// Слот: скрытая LED-лента только по бокам доски (тонкая тёплая полоска у бортиков, цвет «доступно» янтарный),
    /// два бортика её прячут, точки ламп по углам доски в 20 см перед ней; между досками ~4,5 см (зазор рядов
    /// станции Lobby). Пробу отражений станции с запечёнными бликами берут только оружие и магазины — доска,
    /// бортики, лента, подложка карточки и корпус станции без проб отражений, иначе полосы бликуют на них.
    /// Недоступный слот гаснет полностью: лента выключена, ствол и запасной магазин берут пробы в 2 м перед слотом
    /// (только общий свет шатра), цвет материала не меняется — окраска основы делала металл чёрным.
    /// </para>
    /// </summary>
    public class ArsenalSlotLightingTests : MirrorTestHarness
    {
        private const string WallSlotPath = "Assets/Prefabs/Arsenal/Slots/FireArmSlotPrefab.prefab";
        private const string ShelfSlotPath = "Assets/Prefabs/Arsenal/Slots/ShelfSlotPrefab Variant.prefab";
        private const string StationPath = "Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab";
        private const float Eps = 1e-3f;

        private static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, path);
            return go;
        }

        // ── Префаб слота ────────────────────────────────────────────────────

        [TestCase(WallSlotPath)]
        [TestCase(ShelfSlotPath)]
        public void Лента_по_бокам_доски_и_бортики_её_прячут(string path)
        {
            Transform board = Load(path).transform.Find("PegboardSection");
            Transform glow = board.Find("SlotGlow");
            Assert.IsNotNull(glow, "лента SlotGlow — квад на доске");
            Assert.AreEqual(Vector3.one, glow.localScale, "лента — квад во всю доску, свечение по бокам даёт текстура");
            Assert.IsNotNull(glow.GetComponent<ArsenalSlotGlow>());

            foreach (string name in new[] { "StripLip_L", "StripLip_R" })
            {
                Transform lip = board.Find(name);
                Assert.IsNotNull(lip, name);
                Assert.AreEqual(0.5f, Mathf.Abs(lip.localPosition.x), Eps, name + " — у бокового края доски");
                Assert.AreEqual(0.02f, lip.localScale.x * board.localScale.x, Eps, name + " — ширина 2 см");
                Assert.AreEqual(0.03f, lip.localScale.z * board.localScale.z, Eps, name + " — глубина 3 см");
            }
            Assert.IsNull(board.Find("StripLip_T"), "сверху и снизу ленты нет — только бока");
        }

        [TestCase(WallSlotPath)]
        [TestCase(ShelfSlotPath)]
        public void Лента_доступного_слота_янтарная(string path)
        {
            GameObject prefab = Load(path);
            var controller = prefab.GetComponent<ArsenalSlotController>();
            Assert.AreEqual(new Color(1f, 0.6f, 0.25f), controller.AvailableColor, "тёплый янтарный, не белый");
            Assert.AreEqual(1.2f, controller.Glow.Strength, Eps);
        }

        [TestCase(WallSlotPath)]
        [TestCase(ShelfSlotPath)]
        public void Точки_ламп_по_углам_доски_в_20_см_перед_ней(string path)
        {
            Transform root = Load(path).transform;
            Transform board = root.Find("PegboardSection");
            Transform points = root.Find("LampPoints");
            Assert.IsNotNull(points);
            Assert.AreEqual(4, points.childCount);

            Vector3 face = board.localRotation * Vector3.back;
            if (Vector3.Dot(face, Vector3.up + Vector3.back) < 0f) face = -face;
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = Quaternion.Inverse(board.localRotation) * (points.GetChild(i).localPosition - board.localPosition);
                Assert.AreEqual(board.localScale.x * 0.5f, Mathf.Abs(local.x), Eps, "лампа " + i + " — у бокового края");
                Assert.AreEqual(board.localScale.y * 0.5f, Mathf.Abs(local.y), Eps, "лампа " + i + " — у верхнего/нижнего края");
                Assert.AreEqual(0.2f, Vector3.Dot(points.GetChild(i).localPosition - board.localPosition, face), Eps,
                    "лампа " + i + " — 20 см перед лицом доски");
            }
        }

        [TestCase(WallSlotPath)]
        [TestCase(ShelfSlotPath)]
        public void Корпус_слота_не_берёт_пробу_станции(string path)
        {
            Transform root = Load(path).transform;
            foreach (string part in new[] { "PegboardSection", "PegboardSection/StripLip_L", "PegboardSection/StripLip_R",
                                            "PegboardSection/SlotGlow", "WeaponCard/CardBacking" })
                Assert.AreEqual(ReflectionProbeUsage.Off, root.Find(part).GetComponent<Renderer>().reflectionProbeUsage, part);
        }

        // ── Станция Lobby ────────────────────────────────────────────────────

        [Test]
        public void Ряды_станции_Lobby_с_просветом_между_досками()
        {
            var rows = Load(StationPath).GetComponentsInChildren<ArsenalSlotRow>(true);
            Assert.IsNotEmpty(rows);
            foreach (ArsenalSlotRow row in rows)
            {
                float expected = row.Zone == ArsenalPresentationZone.Shelf ? 0.075f : 0.095f;
                Assert.AreEqual(expected, row.Gap, Eps, row.name);
                float board = row.SlotPrefab.transform.Find("PegboardSection").localScale.x;
                float clear = row.SlotPrefab.SlotWidth + row.Gap - board;
                Assert.That(clear, Is.InRange(0.04f, 0.05f), row.name + ": между досками 4–5 см");
            }
        }

        [Test]
        public void Корпус_станции_Lobby_без_проб_отражений()
        {
            foreach (Renderer r in Load(StationPath).GetComponentsInChildren<Renderer>(true))
                Assert.AreEqual(ReflectionProbeUsage.Off, r.reflectionProbeUsage, r.name);
        }

        // ── Гашение недоступного слота ───────────────────────────────────────

        private FirearmSlotController CreateSlot(out Renderer weapon, out Renderer magazine)
        {
            SilenceMirrorNoise();
            GameObject station = CreateNetworkObject("Station");
            FirearmSlotController slot = ArsenalTestStation.AddSlot(station, "Slot");
            var info = ScriptableObject.CreateInstance<WeaponInfo>();
            SetPrivateField(info, "_displayName", "TestRifle");
            SetPrivateField(info, "_weaponId", "test_rifle");
            SetPrivateField(info, "_weaponPrefab", Grabbable("TestRiflePrefab"));
            SetPrivateField(info, "_magazinePrefab", Grabbable("TestRifleMagazine"));
            ArsenalTestStation.Prepare(station, new[] { slot }, new[] { info });

            GameObject item = Grabbable("Weapon");
            weapon = item.AddComponent<MeshRenderer>();
            slot.AssignNetworkItem(item);
            GameObject mag = CreateObject("Magazine");
            mag.transform.SetParent(slot.MagAnchor.transform, false);
            magazine = mag.AddComponent<MeshRenderer>();
            return slot;
        }

        private GameObject Grabbable(string name)
        {
            GameObject go = CreateObject(name);
            go.AddComponent<UxrGrabbableObject>();
            return go;
        }

        private static void AssertDimmed(ArsenalSlotController slot, Renderer r, string what)
        {
            Assert.IsNotNull(r.probeAnchor, what + ": пробы берутся не у слота");
            Vector3 expected = slot.transform.TransformPoint(Vector3.back * 2f + Vector3.up * 0.5f);
            Assert.Less(Vector3.Distance(expected, r.probeAnchor.position), Eps, what + ": точка проб — 2 м перед слотом");
            Assert.IsFalse(r.HasPropertyBlock(), what + ": цвет материала не меняется (окраска делала металл чёрным)");
        }

        [Test]
        public void Слот_не_по_карману_гасит_ствол_и_магазин()
        {
            ArsenalSlotController slot = CreateSlot(out Renderer weapon, out Renderer magazine);

            slot.ApplyOffer(SlotOffer.TooExpensive);
            AssertDimmed(slot, weapon, "ствол");
            slot.RefreshDimming();
            AssertDimmed(slot, magazine, "запасной магазин");

            slot.ApplyOffer(SlotOffer.Affordable);
            Assert.IsNull(weapon.probeAnchor, "доступный ствол снова берёт пробы станции");
            Assert.IsNull(magazine.probeAnchor, "доступный магазин снова берёт пробы станции");
        }

        [Test]
        public void Закрытый_слот_гасит_ствол()
        {
            ArsenalSlotController slot = CreateSlot(out Renderer weapon, out _);
            slot.Lock();
            AssertDimmed(slot, weapon, "ствол закрытого слота");
            slot.Unlock();
            Assert.IsNull(weapon.probeAnchor);
        }

        // ── Инструмент запекания ────────────────────────────────────────────

        private static Type Tool()
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VrBattlegrounds.Editor.Arsenal.ArsenalSlotLighting"))
                .FirstOrDefault(t => t != null);
            Assert.IsNotNull(type, "Editor assembly с ArsenalSlotLighting не загружена.");
            return type;
        }

        [Test]
        public void Лампы_слота_и_слой_пробы_станции()
        {
            GameObject rowObject = CreateObject("Row");
            var row = rowObject.AddComponent<ArsenalSlotRow>();
            var prefab = Load(WallSlotPath).GetComponent<ArsenalSlotController>();
            row.Configure("pegboard", ArsenalPresentationZone.Pegboard, prefab, Vector3.zero, Quaternion.identity, 0.095f,
                ArsenalRowAlignment.Center);

            object points = Tool().GetMethod("Points", BindingFlags.NonPublic | BindingFlags.Static)
                                  .Invoke(null, new object[] { row, Matrix4x4.identity });
            Type t = points.GetType();
            var lamps = (Vector3[])t.GetField("Lamps").GetValue(points);
            var aims = (Vector3[])t.GetField("LampAims").GetValue(points);
            var volume = (Bounds)t.GetField("Volume").GetValue(points);
            var aim = (Vector3)t.GetField("Aim").GetValue(points);

            Assert.AreEqual(4, lamps.Length, "4 лампы на слот — по точкам LampPoints префаба");
            Transform board = prefab.transform.Find("PegboardSection");
            for (int i = 0; i < 4; i++)
            {
                Vector3 lamp = Quaternion.Inverse(board.localRotation) * (lamps[i] - board.localPosition);
                Vector3 target = Quaternion.Inverse(board.localRotation) * (aims[i] - board.localPosition);
                Assert.AreEqual(Mathf.Sign(lamp.x), Mathf.Sign(target.x), "лампа " + i + " светит на свою половину доски");
                Assert.AreEqual(Mathf.Sign(lamp.y), Mathf.Sign(target.y), "лампа " + i + " светит на свою половину доски");
            }

            Assert.IsTrue(volume.Contains(aim), "оружие слота — в объёме пробы станции");
            Vector3 player = aim + Vector3.back * 1f;
            Assert.IsFalse(volume.Contains(player),
                "место игрока вне объёма пробы станции — иначе руки и оружие в руках отражают полосы «хромом»");
        }
    }
}
