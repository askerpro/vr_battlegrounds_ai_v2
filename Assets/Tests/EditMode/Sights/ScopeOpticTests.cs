using System.Linq;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Editor.Weapons.Sights;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Tests.Sights
{
    /// <summary>
    /// Увеличивающая оптика (arsenal-combat-lineup, принято пользователем в шлеме 2026-10-23): комплект с камерой
    /// кратности, картинка и сетка на стекле окуляра, допуск ракурса из общих настроек, AX-50 Auto с призмой ×2.
    /// </summary>
    public sealed class ScopeOpticTests
    {
        private const string SettingsPath = "Assets/Data/Weapons/SightCalibration/WeaponSightCalibrationSettings.asset";
        private const string AutoInfoPath = "Assets/Data/Weapons/SniperRifleAuto_Weapon.asset";

        private static readonly string[] ScopeKits =
            SightKitBuilder.Optics.Where(o => o.ScopeMagnification > 0f).Select(o => o.Name).ToArray();

        private static SightKitBuilder.OpticKitSpec Spec(string name) => SightKitBuilder.Optics.Single(o => o.Name == name);

        [Test]
        public void Есть_увеличивающий_комплект_Sight08_x2()
        {
            CollectionAssert.Contains(ScopeKits, "SightKit_Sight08");
            Assert.AreEqual(2f, Spec("SightKit_Sight08").ScopeMagnification);
        }

        [TestCaseSource(nameof(ScopeKits))]
        public void Комплект_с_кратностью_связан_с_линзой(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Spec(name).PrefabPath);
            Assert.IsNotNull(prefab, "Нет префаба " + Spec(name).PrefabPath);
            var scopes = prefab.GetComponentsInChildren<WeaponScopeCamera>(true);
            var views = prefab.GetComponentsInChildren<WeaponOpticView>(true);
            Assert.AreEqual(1, scopes.Length, "камера кратности — ровно одна");
            Assert.AreEqual(1, views.Length, "линза — ровно одна");
            Assert.AreEqual(Spec(name).ScopeMagnification, scopes[0].Magnification);
            Assert.AreSame(views[0], new SerializedObject(scopes[0]).FindProperty("_view").objectReferenceValue, "камера читает линзу комплекта");
            Assert.AreSame(scopes[0], new SerializedObject(views[0]).FindProperty("_scope").objectReferenceValue, "линза знает свою камеру");
            Assert.IsTrue(views[0].TryGetLens(out _, out float radius), "у линзы нет submesh стекла на шейдере сетки");
            Assert.That(radius, Is.InRange(0.01f, 0.05f), "радиус стекла окуляра, м");
        }

        /// <summary>Комплекты стенда (решение пользователя 2026-10-23): ×2, сетка как у Sight08, Sight03 — окуляр уже, на стволах их нет.</summary>
        [Test]
        public void Комплекты_стенда_Sight03_и_Sight09_x2_не_на_стволах()
        {
            foreach (string name in new[] { "SightKit_Sight03", "SightKit_Sight09" })
            {
                CollectionAssert.Contains(ScopeKits, name);
                Assert.AreEqual(2f, Spec(name).ScopeMagnification, name);
                var reticle = AssetDatabase.LoadAssetAtPath<Material>(Spec(name).ReticleMaterialPath);
                Assert.AreEqual(1f, reticle.GetFloat("_ReticleStyle"), name + ": снайперская сетка");
                Assert.AreEqual(1f, reticle.GetColor("_GlassTint").a, name + ": стекло непрозрачное");
            }
            float Radius(string name)
            {
                var view = AssetDatabase.LoadAssetAtPath<GameObject>(Spec(name).PrefabPath).GetComponentInChildren<WeaponOpticView>(true);
                Assert.IsTrue(view.TryGetLens(out _, out float radius), name);
                return radius;
            }
            Assert.Less(Radius("SightKit_Sight03"), Radius("SightKit_Sight08"), "Sight03 — прицел поуже диаметром");

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Sights/")) continue;
                foreach (Transform t in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Transform>(true))
                    Assert.IsFalse(t.name == "SightKit_Sight03" || t.name == "SightKit_Sight09", $"{path}: комплект стенда на стволе");
            }
        }

        [Test]
        public void Коллиматор_XPS2_без_камеры_кратности()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Spec("SightKit_XPS2").PrefabPath);
            Assert.IsEmpty(prefab.GetComponentsInChildren<WeaponScopeCamera>(true));
            Assert.IsNull(new SerializedObject(prefab.GetComponentInChildren<WeaponOpticView>(true)).FindProperty("_scope").objectReferenceValue);
        }

        [Test]
        public void Сетка_Sight08_снайперская_и_стекло_непрозрачное()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PackOpticBuilder.Sight08.ReticleMaterialPath);
            Assert.IsNotNull(material);
            Assert.AreEqual(WeaponOpticView.ShaderName, material.shader.name);
            Assert.AreEqual(1f, material.GetFloat("_ReticleStyle"), "перекрестие, не точка коллиматора");
            Assert.AreEqual(1f, material.GetColor("_GlassTint").a, "стекло вне увеличения непрозрачное — корпус не просвечивает");
            Assert.Greater(material.GetColor("_DotColor").a, 0f, "своя (красная) точка в центре");
        }

        [Test]
        public void Допуск_ракурса_принятый_5_мм()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
            Assert.IsNotNull(settings);
            Assert.AreEqual(0.005f, settings.ScopeEyeTolerance, 1e-6f);
            Assert.AreEqual(WeaponSightCalibrationSettings.DefaultScopeEyeTolerance, settings.ScopeEyeTolerance, 1e-6f);
        }

        [Test]
        public void AX50_Auto_автомат_с_призмой_и_коротким_выстрелом()
        {
            var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AutoInfoPath);
            Assert.IsNotNull(info);
            Assert.IsTrue(info.FullAuto);
            var weapon = info.WeaponPrefab.GetComponent<UxrFirearmWeapon>();
            var trigger = new SerializedObject(weapon).FindProperty("_triggers").GetArrayElementAtIndex(0);
            Assert.AreEqual((int)UxrShotCycle.FullyAutomatic, trigger.FindPropertyRelative("_cycleType").intValue);
            var clip = trigger.FindPropertyRelative("_shotAudio._clip").objectReferenceValue as AudioClip;
            Assert.IsNotNull(clip, "нет звука выстрела");
            // Файл выстрела снайперского набора пака несёт цикл болтовки (1,88 с): у автомата он звучал как перезарядка.
            Assert.LessOrEqual(clip.length, 0.6f, "в выстреле остался звук болтовки");

            var view = info.WeaponPrefab.GetComponentInChildren<WeaponOpticView>(true);
            Assert.IsNotNull(view, "у AX-50 Auto нет оптики");
            Assert.IsNotNull(view.GetComponent<WeaponScopeCamera>(), "оптика без кратности");
            Assert.IsTrue(view.TryGetZero(out _, out _, out float distance), "прицел не сведён");
            Assert.AreEqual(15f, distance);
        }
    }
}
