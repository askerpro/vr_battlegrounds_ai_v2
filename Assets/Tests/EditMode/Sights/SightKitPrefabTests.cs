using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Weapons.Sights;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Tests.Sights
{
    /// <summary>
    /// Данные инструмента автосведения: префабы комплектов SightKit_* и импорт их FBX. Механика прицелов в игре
    /// пользователем ещё не принята — здесь только структура комплектов и точки R/F (этап sight-iron-kits).
    /// </summary>
    public sealed class SightKitPrefabTests
    {
        private static readonly string[] KitNames = SightKitBuilder.Kits.Select(k => k.Name).ToArray();

        private static SightKitBuilder.KitSpec Spec(string name) => SightKitBuilder.Kits.Single(k => k.Name == name);

        private static GameObject Load(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Spec(name).PrefabPath);
            Assert.IsNotNull(prefab, "Нет префаба " + Spec(name).PrefabPath);
            return prefab;
        }

        [TestCaseSource(nameof(KitNames))]
        public void Kit_HasRearAndFront_OneMoving(string name)
        {
            var root = Load(name);
            var parts = root.GetComponentsInChildren<SightAdjustablePart>(true);
            Assert.AreEqual(2, parts.Length);
            var rear = parts.Single(p => p.Role == SightPartRole.Rear);
            var front = parts.Single(p => p.Role == SightPartRole.Front);
            Assert.AreEqual("Rear", rear.transform.parent.name);
            Assert.AreEqual("Front", front.transform.parent.name);
            Assert.AreSame(root.transform, rear.transform.parent.parent);
            Assert.AreSame(root.transform, front.transform.parent.parent);
            Assert.AreEqual(1, parts.Count(p => p.IsAdjustable), "подвижна ровно одна деталь");
            Assert.IsTrue(front.IsAdjustable, "подвижный — столбик мушки");
            Assert.AreEqual(0f, rear.TravelDown); Assert.AreEqual(0f, rear.TravelUp);
            Assert.Greater(front.TravelDown, 0f); Assert.Greater(front.TravelUp, 0f);
            Assert.AreEqual(Spec(name).Front.TravelDown, front.TravelDown, 1e-7f, "ход вниз — из спецификации SightKitBuilder");
            Assert.AreEqual(Spec(name).Front.TravelUp, front.TravelUp, 1e-7f, "ход вверх — из спецификации SightKitBuilder");
        }

        /// <summary>Решение 2026-10-12 «Ход TR15»: столбик TR15 вниз 8 мм, вверх 3 мм (AR-15 на 15 м требует −5,6 мм).</summary>
        [Test]
        public void Tr15Post_Travel_Down8_Up3()
        {
            var front = Load("SightKit_TR15").GetComponentsInChildren<SightAdjustablePart>(true).Single(p => p.Role == SightPartRole.Front);
            Assert.AreEqual(.008f, front.TravelDown, 1e-7f);
            Assert.AreEqual(.003f, front.TravelUp, 1e-7f);
        }

        [TestCaseSource(nameof(KitNames))]
        public void Kit_PartPivots_StandOnAimingPoints(string name)
        {
            var spec = Spec(name);
            var root = Load(name);
            foreach (var part in root.GetComponentsInChildren<SightAdjustablePart>(true))
            {
                var half = part.Role == SightPartRole.Rear ? spec.Rear : spec.Front;
                Assert.AreEqual(half.PartName, part.name);
                Assert.Less((part.transform.localPosition - half.ExpectedPivot).magnitude, SightKitBuilder.PivotTolerance,
                            part.name + ": pivot не на точке " + (part.Role == SightPartRole.Rear ? "R" : "F"));
                Assert.AreEqual(part.NeutralLocalPosition, part.transform.localPosition, "в префабе комплекта сдвиг 0");
                Assert.Less(Quaternion.Angle(part.transform.localRotation, Quaternion.identity), .01f, "ход только по локальной Y");
                Assert.AreEqual(Vector3.one, part.transform.localScale);
            }
        }

        [TestCaseSource(nameof(KitNames))]
        public void Kit_IsStaticVisualOnly(string name)
        {
            var root = Load(name);
            Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true), "без коллайдеров");
            Assert.IsEmpty(root.GetComponentsInChildren<Rigidbody>(true));
            Assert.IsEmpty(root.GetComponentsInChildren<Mirror.NetworkIdentity>(true), "без NetworkIdentity");
            Assert.IsEmpty(root.GetComponentsInChildren<SkinnedMeshRenderer>(true), "статические меши");
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.IsNotNull(renderer.GetComponent<MeshFilter>().sharedMesh, renderer.name);
                Assert.IsTrue(renderer.sharedMaterials.All(m => m != null), renderer.name + ": пустой материал");
                Assert.AreEqual(0, renderer.gameObject.layer, renderer.name + ": слой деталей оружия — Default");
                Assert.AreEqual("Untagged", renderer.tag);
            }
            Assert.AreEqual(4, root.GetComponentsInChildren<MeshRenderer>(true).Length, "два основания и две детали");
        }

        [Test]
        public void KitFbx_OverlaysPackSource()
        {
            // Все детали, кроме намеренно переставленных (барабан диоптра TR15), совпадают с исходником пака.
            string report = SightKitBuilder.VerifyOverlay(out float worstMatchedMm, out _);
            Assert.Less(worstMatchedMm, .05f, report);
        }

        [Test]
        public void SightTable_AssetExists()
        {
            Assert.IsNotNull(WeaponSightTable.Load(), "Нет " + WeaponSightTable.AssetPath);
        }
    }
}
