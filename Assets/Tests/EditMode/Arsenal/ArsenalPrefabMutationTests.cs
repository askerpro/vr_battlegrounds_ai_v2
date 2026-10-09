using System.Reflection;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Network;
using VrBattlegrounds.Tests.Arsenal;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Пополнение арсенала не трогает префаб оружия — находка VR-04, задача T-22.
    ///
    /// Что доказывает. Чтобы <c>UxrGrabbableObject.Awake()</c> не успел создать лишний
    /// «Auto Anchor», пополнение выключало объект перед <c>Instantiate</c>. Выключало —
    /// сам префаб, то есть ассет на диске: <c>prefab.SetActive(false)</c>. В редакторе это
    /// метит ассет грязным, и правка утекает в репозиторий; исключение между выключением
    /// и обратным включением оставило бы префаб выключенным насовсем.
    ///
    /// Тест работает с настоящим префаб-ассетом, потому что «грязность» — свойство ассета:
    /// на обычном GameObject-е в памяти отличить старый способ от нового нечем. Флаг
    /// <c>EditorUtility.IsDirty</c> после <c>SetActive</c> уже не гаснет, даже если состояние
    /// вернули обратно, — именно поэтому проверка «префаб активен» ничего не ловила.
    ///
    /// Остальные проверки страхуют сам приём отложенного <c>Awake</c>: оружие обязано
    /// оказаться в слоте, а выключенный контейнер — остаться пустым.
    /// </summary>
    public class ArsenalPrefabMutationTests : MirrorTestHarness
    {
        private const string ProbeFolder = "Assets/__ArsenalPrefabProbe";
        private const string ProbePath = ProbeFolder + "/ProbeWeapon.prefab";

        private GameObject _weaponAsset;

        [SetUp]
        public void CreateWeaponAsset()
        {
            SilenceMirrorNoise();

            if (!AssetDatabase.IsValidFolder(ProbeFolder))
                AssetDatabase.CreateFolder("Assets", "__ArsenalPrefabProbe");

            GameObject source = new GameObject("ProbeWeapon");
            source.AddComponent<UxrGrabbableObject>();

            // NetworkIdentity зонду намеренно не даём, и Error Mirror «has no NetworkIdentity»
            // из NetworkServer.Spawn здесь ожидаем. С идентити спавн клона в EditMode падает
            // NRE: Unity не зовёт Awake у инстанса, массив NetworkBehaviours пуст, и
            // NetworkIdentity.OnStartServer валится — вместе с TearDown харнесса. Вклиниться
            // между Instantiate и Spawn тест не может: оба внутри ReplenishWeaponsNetwork.
            // Сам спавн проверяется только в PlayMode (см. SessionRecoveryTests, Docs/testing.md).

            _weaponAsset = PrefabUtility.SaveAsPrefabAsset(source, ProbePath);
            Object.DestroyImmediate(source);

            Assert.IsNotNull(_weaponAsset, "Не удалось создать временный префаб оружия.");
            Assert.IsFalse(EditorUtility.IsDirty(_weaponAsset),
                "Контроль: только что сохранённый ассет обязан быть чистым.");
        }

        [TearDown]
        public void DeleteWeaponAsset()
        {
            _weaponAsset = null;
            AssetDatabase.DeleteAsset(ProbeFolder);
        }

        /// <summary>Собранная стена с одним слотом, настроенным на временный префаб оружия.</summary>
        private ArsenalWallController CreateWall(out ArsenalSlotController slot)
        {
            GameObject wallObject = CreateNetworkObject("ArsenalWall");

            FirearmSlotController firearm = ArsenalTestStation.AddSlot(wallObject, "Slot");
            ArsenalWallController wall = ArsenalTestStation.Prepare(wallObject, new[] { firearm }, new[] { CreateWeaponInfo() });
            slot = firearm;
            EnableNetworking(wallObject);

            // В EditMode Unity не зовёт Awake, а без него у стены пуст список слотов.
            InvokeLifecycleMethod(wall, "Awake");

            return wall;
        }

        private WeaponInfo CreateWeaponInfo()
        {
            WeaponInfo info = ScriptableObject.CreateInstance<WeaponInfo>();
            SetPrivateField(info, "_displayName", "ProbeWeapon");
            SetPrivateField(info, "_weaponId", "probe_weapon");
            SetPrivateField(info, "_weaponPrefab", _weaponAsset);
            // Магазин станция требует от любого ствола; выдачу оружия он не трогает.
            SetPrivateField(info, "_magazinePrefab", _weaponAsset);
            return info;
        }

        /// <summary>Пополняет стену — тот же вызов, что делает сервер в OnStartServer.</summary>
        private void Replenish(ArsenalWallController wall)
        {
            InvokePrivateMethod(wall, "ReplenishWeaponsNetwork", true);
        }

        // ── Сама находка ────────────────────────────────────────────────────

        [Test]
        public void Пополнение_не_метит_префаб_оружия_грязным()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot;
            ArsenalWallController wall = CreateWall(out slot);

            Replenish(wall);

            Assert.IsFalse(EditorUtility.IsDirty(_weaponAsset),
                "Пополнение арсенала пометило префаб оружия изменённым. Значит оно выключает " +
                "и включает сам ассет, а не инстанс, — и это состояние утечёт в репозиторий.");

            Assert.IsTrue(_weaponAsset.activeSelf,
                "Префаб остался выключенным после пополнения.");
        }

        // ── Приём с отложенным Awake не должен ломать выдачу ────────────────

        [Test]
        public void Пополнение_кладёт_оружие_в_слот()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot;
            ArsenalWallController wall = CreateWall(out slot);

            Replenish(wall);

            Assert.IsTrue(slot.IsItemPresent,
                "После пополнения слот пуст — оружие до него не доехало.");
            Assert.IsTrue(slot.CurrentItem.activeInHierarchy,
                "Оружие в слоте осталось неактивным: его либо не включили, либо не вынули " +
                "из выключенного контейнера, под которым оно рождается.");
        }

        [Test]
        public void Выключенный_контейнер_не_держит_выданное_оружие()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot;
            ArsenalWallController wall = CreateWall(out slot);

            Replenish(wall);

            // Контейнер переехал из стены в сетевой слой: отложенный Awake нужен не только
            // арсеналу, но и любому сетевому спавну объектов UltimateXR (NET-16).
            FieldInfo field = typeof(NetworkUxrIdentity).GetField(
                "_dormitory", BindingFlags.Static | BindingFlags.NonPublic);

            Transform root = field != null ? (Transform)field.GetValue(null) : null;
            if (root == null) Assert.Pass("Отложенный Awake сделан без выключенного контейнера — проверять нечего.");

            Assert.AreEqual(0, root.childCount,
                "Оружие осталось висеть в выключенном контейнере — на стене его не будет.");
        }

        [Test]
        public void Выданное_оружие_рождается_без_авто_якоря()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot;
            ArsenalWallController wall = CreateWall(out slot);

            Replenish(wall);

            UxrGrabbableObject grabbable = slot.CurrentItem.GetComponent<UxrGrabbableObject>();
            Assert.IsNotNull(grabbable, "Контроль: у выданного оружия обязан быть UxrGrabbableObject.");

            FieldInfo autoAnchor = typeof(UxrGrabbableObject).GetField(
                "_autoCreateStartAnchor", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(autoAnchor, "UltimateXR: поле _autoCreateStartAnchor исчезло — приём больше не работает.");

            Assert.IsFalse((bool)autoAnchor.GetValue(grabbable),
                "У выданного оружия включён авто-якорь. При живом Awake UXR создаст «Auto Anchor» " +
                "и вытащит оружие из слота — ради этого выключение перед Instantiate и делается.");
        }
    }
}
