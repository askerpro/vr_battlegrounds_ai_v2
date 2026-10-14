using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Interaction
{
    public class AnchoredItemCollisionIgnoreTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private HashSet<int> _reportedScenes;
        private int[] _previousReportedScenes;

        [SetUp]
        public void SetUp()
        {
            // Дедупликация ошибок живёт весь runtime. Сохраняем её для соседних fixtures,
            // чтобы порядок тестов не менял ожидание первой ошибки на scene handle.
            FieldInfo reported = typeof(AnchoredItemCollisionIgnore).GetField("ReportedScenes",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(reported);
            _reportedScenes = (HashSet<int>)reported.GetValue(null);
            _previousReportedScenes = new int[_reportedScenes.Count];
            _reportedScenes.CopyTo(_previousReportedScenes);
            _reportedScenes.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
            if (_reportedScenes != null && _previousReportedScenes != null)
            {
                _reportedScenes.Clear();
                _reportedScenes.UnionWith(_previousReportedScenes);
            }
        }

        private GameObject Create(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            _created.Add(go);
            return go;
        }

        private static GameObject LoadWeaponAsset(string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Weapons/{name}/{name}.prefab");
            Assert.IsNotNull(asset);
            Assert.IsTrue(EditorUtility.IsPersistent(asset), "Нужен asset, а не сценовый экземпляр.");
            Assert.IsFalse(asset.scene.IsValid(), "Предусловие: prefab asset вне действительной сцены.");
            return asset;
        }

        private static void ExpectOutOfSceneError()
        {
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[AnchoredItemCollisionIgnore\] предмет вне действительной сцены:.*пары столкновений пропущены"));
        }

        [Test]
        public void Вставленный_предмет_не_сталкивается_с_корпусом_после_выхода_пара_восстанавливается()
        {
            GameObject weapon = Create("Корпус");
            weapon.AddComponent<Rigidbody>().isKinematic = true;
            Collider bodyCollider = weapon.AddComponent<BoxCollider>();
            GameObject anchor = Create("Якорь", weapon.transform);
            anchor.AddComponent<UxrGrabbableObjectAnchor>();
            GameObject item = Create("Вставленный магазин", anchor.transform);
            item.AddComponent<Rigidbody>().isKinematic = true;
            Collider itemCollider = item.AddComponent<BoxCollider>();
            var ignore = weapon.AddComponent<AnchoredItemCollisionIgnore>();

            Assert.IsFalse(Physics.GetIgnoreCollision(bodyCollider, itemCollider));
            ignore.SyncIgnoredCollisions();
            Assert.IsTrue(Physics.GetIgnoreCollision(bodyCollider, itemCollider));
            ignore.SyncIgnoredCollisions();
            Assert.IsTrue(Physics.GetIgnoreCollision(bodyCollider, itemCollider));

            item.transform.SetParent(null, true);
            ignore.SyncIgnoredCollisions();
            Assert.IsFalse(Physics.GetIgnoreCollision(bodyCollider, itemCollider),
                "Вынутый магазин снова должен сталкиваться с оружием.");
        }

        [Test]
        public void Prefab_вне_сцены_пропускается_с_одной_ошибкой_на_handle()
        {
            GameObject firstAsset = LoadWeaponAsset("AR15");
            GameObject secondAsset = LoadWeaponAsset("PPK");
            var first = firstAsset.GetComponent<AnchoredItemCollisionIgnore>();
            var second = secondAsset.GetComponent<AnchoredItemCollisionIgnore>();
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreEqual(firstAsset.scene.handle, secondAsset.scene.handle,
                "Предусловие: два разных объекта принадлежат одному недействительному handle.");

            ExpectOutOfSceneError();
            Assert.DoesNotThrow(() => first.SyncIgnoredCollisions());
            Assert.DoesNotThrow(() => first.SyncIgnoredCollisions());
            Assert.DoesNotThrow(() => second.SyncIgnoredCollisions());
            // Ошибка Physics.IgnoreCollision также была бы неожиданным логом и провалила тест.
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Коллайдер_prefab_вне_сцены_не_передаётся_физике()
        {
            GameObject weapon = Create("Сценовый корпус");
            weapon.AddComponent<Rigidbody>().isKinematic = true;
            weapon.AddComponent<BoxCollider>();
            var ignore = weapon.AddComponent<AnchoredItemCollisionIgnore>();
            ignore.SyncIgnoredCollisions();
            GameObject asset = LoadWeaponAsset("AR15");
            Rigidbody assetBody = asset.GetComponent<Rigidbody>();
            Assert.IsNotNull(assetBody);
            Assert.IsNotEmpty(asset.GetComponentsInChildren<Collider>(true));

            // Unity запрещает родительство между сценой и prefab asset. Напрямую проверяем
            // защитную границу пары, которую публичный Sync получает при утечке разборки.
            MethodInfo setIgnored = typeof(AnchoredItemCollisionIgnore).GetMethod("SetIgnored",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(setIgnored);
            ExpectOutOfSceneError();
            Assert.DoesNotThrow(() => setIgnored.Invoke(ignore, new object[] { assetBody, true }));
            Assert.DoesNotThrow(() => setIgnored.Invoke(ignore, new object[] { assetBody, false }));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
