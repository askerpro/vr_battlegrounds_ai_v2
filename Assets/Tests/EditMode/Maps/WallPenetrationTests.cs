using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Прострел стен по формуле Counter-Strike (<see cref="WallPenetration"/>, патч SDK 32): расчёт на примитивах и
    /// путь настоящей пули через <c>UxrWeaponManager.UpdateProjectiles</c> — стена между стрелком и целью, урон цели.
    ///
    /// <para>
    /// Ожидаемые числа посчитаны вручную по формуле CS (не вызовом проверяемого кода):
    /// потеря = (1/pm)·t²/24 + урон·0.16 + (3.75/пробитие)·3·(1/pm), t — толщина в юнитах (дюймах).
    /// 10 см = 3.937 юнита, t² = 15.50; 5 см = 1.969, t² = 3.875; 50 см = 19.685, t² = 387.5.
    /// </para>
    ///
    /// <para>
    /// Всё строится далеко от начала координат: открытая в редакторе сцена не должна попасть под луч.
    /// </para>
    /// </summary>
    public class WallPenetrationTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const float Deagle = 2f;   // пробитие Desert Eagle, винтовок
        private const float Glock = 1f;    // пробитие пистолетов и SMG
        private const float Wood = 3f;     // pm дерева/картона насквозь (правило одного материала CS)
        private const float Damage = 50f;

        private static readonly Vector3 Origin = new Vector3(5000f, 5000f, 5000f);

        private readonly List<Object> _created = new List<Object>();
        private UxrWeaponManager _createdManager;
        private IList _projectiles;
        private int _start;

        [SetUp]
        public void SetUp()
        {
            if (!UxrWeaponManager.HasInstance)
            {
                _createdManager = new GameObject("WeaponManager").AddComponent<UxrWeaponManager>();
                Call(_createdManager, "Awake");
            }

            _projectiles = (IList)typeof(UxrWeaponManager).GetField("_projectiles", Any).GetValue(UxrWeaponManager.Instance);
            _start = _projectiles.Count;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _projectiles.Count - 1; i >= _start; i--)
            {
                var go = _projectiles[i].GetType().GetProperty("Projectile").GetValue(_projectiles[i]) as GameObject;
                _projectiles.RemoveAt(i);
                if (go != null) Object.DestroyImmediate(go);
            }

            foreach (Object o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();

            if (_createdManager != null)
            {
                Call(_createdManager, "OnDestroy");
                Object.DestroyImmediate(_createdManager.gameObject);
                _createdManager = null;
            }
        }

        // ── Формула CS ──────────────────────────────────────────────────────

        [Test]
        public void Потеря_по_формуле_CS()
        {
            // Дерево 10 см, Deagle: 0.3333·15.50/24 + 50·0.16 + 1.875·3·0.3333 = 0.2153 + 8 + 1.875 = 10.090
            Assert.AreEqual(10.090f, WallPenetration.Loss(Damage, 0.10f, Wood, Deagle), 0.01f);
            // То же, Glock: вход дороже вдвое — 3.75 вместо 1.875.
            Assert.AreEqual(11.965f, WallPenetration.Loss(Damage, 0.10f, Wood, Glock), 0.01f);
            // Материал pm 1, 10 см, Deagle: 0.6458 + 8 + 5.625 = 14.271
            Assert.AreEqual(14.271f, WallPenetration.Loss(Damage, 0.10f, 1f, Deagle), 0.01f);
            // pm 1, 50 см, Deagle: 16.146 + 8 + 5.625 = 29.771 — толщина растёт квадратом.
            Assert.AreEqual(29.771f, WallPenetration.Loss(Damage, 0.50f, 1f, Deagle), 0.01f);
        }

        [Test]
        public void Нулевое_пробитие_и_непробиваемый_материал_не_пропускают()
        {
            Assert.IsTrue(float.IsPositiveInfinity(WallPenetration.Loss(Damage, 0.1f, Wood, 0f)), "Пробитие 0 — не пробивает.");
            Assert.IsTrue(float.IsPositiveInfinity(WallPenetration.Loss(Damage, 0.1f, 0.05f, Deagle)), "pm < 0.1 — в CS пуля не проходит.");
        }

        [Test]
        public void Неразмеченная_стена_останавливает()
        {
            GameObject wall = Wall("Wall", 0.05f, null);
            Assert.AreEqual(UxrPenetrationKind.Stop, EvaluateAt(wall).Kind, "Без разметки стена — Hard.");
        }

        [Test]
        public void Hard_останавливает()
        {
            GameObject wall = Wall("Wall_Hard", 0.05f, CoverClass.Hard);
            Assert.AreEqual(UxrPenetrationKind.Stop, EvaluateAt(wall).Kind);
        }

        [Test]
        public void Soft_пробивается_с_потерей_CS()
        {
            GameObject wall = Wall("Wall_Soft", 0.10f, CoverClass.Soft);
            UxrPenetrationResult r = EvaluateAt(wall);

            Assert.AreEqual(UxrPenetrationKind.Penetrate, r.Kind);
            Assert.AreEqual(39.910f / Damage, r.DamageMultiplier, 0.001f, "Deagle сквозь 10 см дерева: 50 − 10.09 = 39.91.");
            Assert.AreEqual(Origin.z + 0.05f + WallPenetration.ExitOffset, r.ExitPoint.z, 0.002f, "Пуля выходит с дальней грани.");
            Assert.AreEqual(wall.GetComponent<Collider>(), r.ExitHit.collider, "Выходная грань — та же стена (декаль на выходе).");
            Assert.Greater(Vector3.Dot(r.ExitHit.normal, Vector3.forward), 0.9f, "Нормаль выхода смотрит по полёту пули.");
        }

        [Test]
        public void Soft_на_MeshCollider_пробивается_так_же()
        {
            GameObject wall = Wall("Wall_Soft", 0.10f, CoverClass.Soft);
            Object.DestroyImmediate(wall.GetComponent<BoxCollider>());
            wall.AddComponent<MeshCollider>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Physics.SyncTransforms();

            UxrPenetrationResult r = EvaluateAt(wall);
            Assert.AreEqual(UxrPenetrationKind.Penetrate, r.Kind);
            Assert.AreEqual(39.910f / Damage, r.DamageMultiplier, 0.001f);
        }

        [Test]
        public void Множитель_учитывает_уже_потерянный_урон()
        {
            // Пуля уже потеряла 20 % (множитель 0.8), текущий урон 40: 0.2153 + 6.4 + 1.875 = 8.490 → 31.51 из 50.
            GameObject wall = Wall("Wall_Soft", 0.10f, CoverClass.Soft);
            UxrPenetrationResult r = EvaluateAt(wall, damage: 40f, damageMultiplier: 0.8f);
            Assert.AreEqual(31.510f / Damage, r.DamageMultiplier, 0.001f);
        }

        [Test]
        public void Soft_толще_поиска_выхода_не_пробивается()
        {
            GameObject wall = Wall("Wall_Soft", WallPenetration.MaxThickness + 0.05f, CoverClass.Soft);
            wall.GetComponent<CoverSurface>().PenetrationModifier = 1000f;
            Assert.AreEqual(UxrPenetrationKind.Stop, EvaluateAt(wall, power: 1000f).Kind);
        }

        [Test]
        public void Пуля_застревает_когда_потеря_больше_урона()
        {
            // pm 1, 1 м (39.37 юнита): 64.58 + 8 + 5.625 > 50.
            GameObject wall = Wall("Wall_Soft", 1.0f, CoverClass.Soft);
            wall.GetComponent<CoverSurface>().PenetrationModifier = 1f;
            Assert.AreEqual(UxrPenetrationKind.Stop, EvaluateAt(wall).Kind);
        }

        [Test]
        public void Пуля_застревает_когда_остаток_меньше_единицы()
        {
            // Урон 3: 0.2153 + 0.48 + 1.875 = 2.570 → остаток 0.43 < 1.
            GameObject wall = Wall("Wall_Soft", 0.10f, CoverClass.Soft);
            Assert.AreEqual(UxrPenetrationKind.Stop, EvaluateAt(wall, damage: 3f).Kind);
            Assert.AreEqual(UxrPenetrationKind.Stop, EvaluateAt(wall, power: 0f).Kind, "Пробитие 0 — не пробивает.");
        }

        [Test]
        public void Лимит_пробитий_как_в_CS()
        {
            Assert.AreEqual(4, WallPenetration.MaxPenetrations, "В CS пуля пробивает до 4 препятствий.");
            GameObject wall = Wall("Wall_Soft", 0.02f, CoverClass.Soft);
            Assert.AreEqual(UxrPenetrationKind.Penetrate, EvaluateAt(wall, penetrations: WallPenetration.MaxPenetrations - 1).Kind);
            Assert.AreEqual(UxrPenetrationKind.Stop, EvaluateAt(wall, penetrations: WallPenetration.MaxPenetrations).Kind);
        }

        [Test]
        public void Visual_пропускает_без_потерь()
        {
            GameObject bush = Wall("Bush_Visual", 1f, CoverClass.Visual);
            UxrPenetrationResult r = EvaluateAt(bush, damageMultiplier: 0.7f);
            Assert.AreEqual(UxrPenetrationKind.PassThrough, r.Kind);
            Assert.AreEqual(0.7f, r.DamageMultiplier, 1e-4f);
        }

        [Test]
        public void Разметка_родителя_действует_на_дочерний_коллайдер()
        {
            var fence = new GameObject("Fence_Soft");
            _created.Add(fence);
            fence.AddComponent<CoverSurface>().Class = CoverClass.Soft;
            GameObject plank = Wall("Plank", 0.05f, null);
            plank.transform.SetParent(fence.transform, true);

            Assert.AreEqual(UxrPenetrationKind.Penetrate, EvaluateAt(plank).Kind);
        }

        // ── Настоящая пуля через UxrWeaponManager ───────────────────────────

        [Test]
        public void Пуля_сквозь_Soft_ранит_цель_за_стеной_по_CS()
        {
            Wall("Wall_Soft", 0.10f, CoverClass.Soft);
            Assert.AreEqual(39.910f, Reached(FireAtTargetBehind(Deagle)), 0.02f, "Deagle сквозь 10 см дерева.");
        }

        [Test]
        public void Пистолет_теряет_больше_чем_Deagle()
        {
            Wall("Wall_Soft", 0.10f, CoverClass.Soft);
            Assert.AreEqual(38.035f, Reached(FireAtTargetBehind(Glock)), 0.02f, "Glock сквозь 10 см дерева: 50 − 11.965.");
        }

        [Test]
        public void Пуля_в_неразмеченную_стену_не_ранит_цель()
        {
            Wall("Wall", 0.10f, null);
            Assert.IsNull(FireAtTargetBehind(Deagle), "Урон прошёл сквозь Hard-стену.");
        }

        [Test]
        public void Пуля_сквозь_Visual_бьёт_в_полную_силу()
        {
            Wall("Bush_Visual", 0.5f, CoverClass.Visual);
            Assert.AreEqual(Damage, Reached(FireAtTargetBehind(Deagle)), 0.01f);
        }

        [Test]
        public void Две_Soft_стены_теряют_последовательно()
        {
            // 5 см дерева: 0.0538 + 0.16·урон + 1.875. Первая: 50 → 40.071; вторая: 40.071 → 31.731.
            Wall("WallA_Soft", 0.05f, CoverClass.Soft, z: 0f);
            Wall("WallB_Soft", 0.05f, CoverClass.Soft, z: 1f);
            Assert.AreEqual(31.731f, Reached(FireAtTargetBehind(Deagle)), 0.02f);
        }

        [Test]
        public void Без_хука_пуля_останавливается_как_в_SDK()
        {
            UxrProjectilePenetrationHandler installed = UxrWeaponManager.ProjectilePenetration;
            UxrWeaponManager.ProjectilePenetration = null;
            try
            {
                Wall("Wall_Soft", 0.10f, CoverClass.Soft);
                Assert.IsNull(FireAtTargetBehind(Deagle));
            }
            finally
            {
                UxrWeaponManager.ProjectilePenetration = installed;
            }
        }

        [Test]
        public void Хук_установлен_при_загрузке()
        {
            Assert.IsNotNull(UxrWeaponManager.ProjectilePenetration, "WallPenetration.Install не отработал — прострела в игре не будет.");
        }

        // ── Вспомогательное ─────────────────────────────────────────────────

        /// <summary>Стена поперёк оси Z: центр в <see cref="Origin"/> + z, толщина по Z. Soft — дерево (pm 3).</summary>
        private GameObject Wall(string name, float thickness, CoverClass? coverClass, float z = 0f)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = Origin + new Vector3(0f, 0f, z);
            wall.transform.localScale = new Vector3(2f, 2f, thickness);
            if (coverClass.HasValue)
            {
                var surface = wall.AddComponent<CoverSurface>();
                surface.Class = coverClass.Value;
                surface.PenetrationModifier = Wood;
            }
            _created.Add(wall);
            Physics.SyncTransforms();
            return wall;
        }

        private static UxrPenetrationResult EvaluateAt(GameObject wall, float power = Deagle, int penetrations = 0,
                                                       float damage = Damage, float damageMultiplier = 1f)
        {
            Physics.SyncTransforms();
            Vector3 from = new Vector3(Origin.x, Origin.y, wall.transform.position.z - 3f);
            Assert.IsTrue(wall.GetComponent<Collider>().Raycast(new Ray(from, Vector3.forward), out RaycastHit hit, 10f), "Луч не попал в стену.");
            return WallPenetration.Evaluate(hit, Vector3.forward, power, penetrations, damage, damageMultiplier);
        }

        /// <summary>Выстрел по оси Z из-за стен в цель за ними; урон, дошедший до цели, или null.</summary>
        private float? FireAtTargetBehind(float power)
        {
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "Target";
            target.transform.position = Origin + new Vector3(0f, 0f, 3f);
            _created.Add(target);
            var actor = target.AddComponent<UxrActor>();
            var actorSo = new SerializedObject(actor);
            actorSo.FindProperty("_life").floatValue = 1000f;
            actorSo.ApplyModifiedPropertiesWithoutUndo();

            float? received = null;
            actor.DamageReceiving += (_, e) =>
            {
                received = e.Damage;
                e.Cancel(); // смерть и эффекты урона в edit mode не нужны — важно только число
            };

            var gun = new GameObject("Gun");
            _created.Add(gun);
            var source = gun.AddComponent<UxrProjectileSource>();

            var bullet = new GameObject("Bullet");
            _created.Add(bullet);

            var shot = new UxrShotDescriptor();
            SetField(shot, "_projectilePrefab", bullet);
            SetField(shot, "_projectileSpeed", 0f);           // пуля не двигается сама — луч кадра покрывает всё поле
            SetField(shot, "_projectileLength", 20f);
            SetField(shot, "_projectileDamageNear", Damage);
            SetField(shot, "_projectileDamageFar", Damage);
            SetField(shot, "_penetrationPower", power);

            Physics.SyncTransforms();
            UxrWeaponManager.Instance.RegisterNewProjectileShot(source, shot, Origin + new Vector3(0f, 0f, -3f), Quaternion.LookRotation(Vector3.forward));

            object info = _projectiles[_projectiles.Count - 1];
            _created.Add((GameObject)info.GetType().GetProperty("Projectile").GetValue(info));

            // SDK убирает пулю через Destroy — вне Play Mode это Error «Destroy may not be called from edit mode»,
            // шум харнесса, а не отказ логики (как в RemoteShotReplayTests).
            LogAssert.ignoreFailingMessages = true;
            for (int frame = 0; frame < 8 && _projectiles.Contains(info); frame++)
            {
                typeof(UxrWeaponManager).GetMethod("UpdateProjectiles", Any).Invoke(UxrWeaponManager.Instance, null);
            }

            Assert.IsFalse(_projectiles.Contains(info), "Пуля не остановилась ни на стене, ни на цели.");
            return received;
        }

        private static float Reached(float? damage)
        {
            Assert.IsNotNull(damage, "Пуля не дошла до цели за стеной.");
            return damage.Value;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, Any) ?? throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }

        private static void Call(object target, string method)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                MethodInfo info = type.GetMethod(method, Any | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (info != null)
                {
                    info.Invoke(target, null);
                    return;
                }
            }
        }
    }
}
