using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// Уборка мусора с пола: часы «сколько пролежал» (<see cref="LooseItemClock"/>),
    /// признак «ничей» (<see cref="LooseItems.IsLoose"/>), слот стены, который принимает
    /// своё оружие обратно, и проводка правил — где что лежит.
    ///
    /// Сетевое удаление и возврат домой проверяются в Play mode: им нужен сервер
    /// и заспавненные предметы.
    /// </summary>
    public class LooseItemTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object o in _created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        // ── Часы ────────────────────────────────────────────────────────────

        [Test]
        public void Отсчёт_идёт_с_первого_замера_ничьим()
        {
            var clock = new LooseItemClock();

            Assert.AreEqual(0f, clock.Observe(1, true, 10f));
            Assert.AreEqual(25f, clock.Observe(1, true, 35f));
        }

        [Test]
        public void Поднятый_предмет_начинает_отсчёт_заново()
        {
            var clock = new LooseItemClock();
            clock.Observe(1, true, 0f);

            clock.Observe(1, false, 20f);

            Assert.AreEqual(0f, clock.Observe(1, true, 25f),
                "Магазин подняли и снова бросили — срок считается от нового броска, а не от первого.");
            Assert.AreEqual(10f, clock.Observe(1, true, 35f));
        }

        [Test]
        public void Пропавший_из_замера_предмет_забывается()
        {
            var clock = new LooseItemClock();

            clock.BeginPass();
            clock.Observe(1, true, 0f);
            clock.Observe(2, true, 0f);
            clock.EndPass();

            clock.BeginPass();
            clock.Observe(2, true, 1f);
            clock.EndPass();

            Assert.AreEqual(1, clock.Tracked, "Уничтоженный магазин не должен висеть в словаре вечно.");
        }

        // ── Что ничьё ───────────────────────────────────────────────────────

        private UxrGrabbableObject CreateItem(bool kinematic)
        {
            GameObject go = Track(new GameObject("Item"));
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.isKinematic = kinematic;

            UxrGrabbableObject grabbable = go.AddComponent<UxrGrabbableObject>();
            grabbable.RigidBodySource = body;
            return grabbable;
        }

        [Test]
        public void Брошенный_предмет_ничей()
        {
            Assert.IsTrue(LooseItems.IsLoose(CreateItem(kinematic: false)));
        }

        [Test]
        public void Спрятанный_в_карман_не_ничей()
        {
            UxrGrabbableObject item = CreateItem(kinematic: false);
            item.gameObject.SetActive(false);

            Assert.IsFalse(LooseItems.IsLoose(item),
                "Карман прячет магазины выключенными — уборка их не трогает.");
        }

        [Test]
        public void Декоративный_магазин_слота_не_ничей()
        {
            // Так FirearmSlotController оформляет магазин-витрину у слота.
            UxrGrabbableObject item = CreateItem(kinematic: true);
            item.enabled = false;

            Assert.IsFalse(LooseItems.IsLoose(item),
                "Магазин-витрину у слота стены никто не бросал — убирать его нельзя.");
        }

        [Test]
        public void Кинематический_предмет_не_ничей()
        {
            Assert.IsFalse(LooseItems.IsLoose(CreateItem(kinematic: true)));
        }

        // ── Слот принимает своё оружие обратно ──────────────────────────────

        private static WeaponInfo LoadWeapon(string file)
        {
            var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>($"Assets/Data/Weapons/{file}.asset");
            Assert.IsNotNull(info, $"Контроль: {file} найден.");
            Assert.IsNotNull(info.WeaponPrefab, $"Контроль: у {file} есть префаб оружия.");
            return info;
        }

        private ArsenalSlotController CreateSlot(WeaponInfo info)
        {
            GameObject slotObject = Track(new GameObject("Slot"));
            GameObject anchorObject = new GameObject("ItemAnchor");
            anchorObject.transform.SetParent(slotObject.transform);
            anchorObject.AddComponent<UxrGrabbableObjectAnchor>();

            slotObject.SetActive(false);
            ArsenalSlotController slot = slotObject.AddComponent<ArsenalSlotController>();
            var field = typeof(ArsenalSlotController).GetField("_weaponInfo",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field.SetValue(slot, info);

            // Awake слота настраивает якорь; в EditMode его зовём сами.
            typeof(ArsenalSlotController).GetMethod("Awake",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(slot, null);
            return slot;
        }

        private UxrGrabbableObject CreateIssuedWeapon(WeaponInfo info)
        {
            GameObject weapon = Track((GameObject)PrefabUtility.InstantiatePrefab(info.WeaponPrefab));
            // Не «??»: у Unity-объекта «нет компонента» — это не C#-null.
            WeaponComponent component = weapon.GetComponent<WeaponComponent>();
            if (component == null) component = weapon.AddComponent<WeaponComponent>();
            component.Init(info);
            return weapon.GetComponent<UxrGrabbableObject>();
        }

        [Test]
        public void Слот_принимает_своё_оружие_обратно()
        {
            WeaponInfo m16 = LoadWeapon("M16_Weapon");
            ArsenalSlotController slot = CreateSlot(m16);

            Assert.IsTrue(slot.ItemAnchor.IsCompatibleObject(CreateIssuedWeapon(m16)),
                "Слот не принимает оружие, которое сам выдал. У якорей стены пустой Compatible Tags, " +
                "а UltimateXR понимает пустой список как «только предметы без тега» — повесить ствол " +
                "обратно было нельзя.");
        }

        [Test]
        public void Слот_не_принимает_чужой_тип_оружия()
        {
            // У Gun_real тег M16_Rifle — один тег слот винтовки от пистолета не отличит.
            ArsenalSlotController slot = CreateSlot(LoadWeapon("M16_Weapon"));

            Assert.IsFalse(slot.ItemAnchor.IsCompatibleObject(CreateIssuedWeapon(LoadWeapon("Gun_Weapon"))),
                "Пистолет встал бы на слот винтовки.");
        }

        [Test]
        public void Закрытый_слот_ничего_не_принимает()
        {
            WeaponInfo m16 = LoadWeapon("M16_Weapon");
            ArsenalSlotController slot = CreateSlot(m16);
            slot.Lock();

            Assert.IsFalse(slot.ItemAnchor.IsCompatibleObject(CreateIssuedWeapon(m16)),
                "На закрытую стену оружие не вешается.");
        }

        // ── Проводка правил ─────────────────────────────────────────────────

        private static string SweeperGuid =>
            AssetDatabase.AssetPathToGUID("Assets/Scripts/Interaction/LooseItemSweeper.cs");

        /// <summary>
        /// Уборщик лобби переехал со сцены на префаб разминки (<c>WarmupMode.prefab</c>, бывший LobbyMode):
        /// лобби стало режимом, и его правила живут там же, где правила Elimination, —
        /// на префабе режима. Раньше тест искал компонент в <c>Lobby.unity</c>.
        /// </summary>
        [Test]
        public void В_лобби_оружие_возвращается_домой()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameModes/WarmupMode.prefab");
            Assert.IsNotNull(prefab, "Нет префаба разминки.");

            var sweeper = prefab.GetComponent<LooseItemSweeper>();
            Assert.IsNotNull(sweeper, "На разминке нет LooseItemSweeper — мусор в лобби копится без предела.");

            var action = (LooseWeaponAction)typeof(LooseItemSweeper)
                .GetField("_weaponAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(sweeper);
            Assert.AreEqual(LooseWeaponAction.ReturnHome, action, "В лобби оружие с пола обязано возвращаться в свой слот.");

            Assert.IsFalse(File.ReadAllText("Assets/Scenes/Lobby.unity").Contains(SweeperGuid),
                "Уборщик остался и в сцене лобби — два уборщика спорили бы за один пол.");
        }

        [Test]
        public void В_раунде_оружие_лежит_а_пол_чистится_к_новому_раунду()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameModes/EliminationMode.prefab");

            Assert.IsNotNull(prefab.GetComponent<RoundCleanup>(),
                "Нет RoundCleanup — мусор прошлого раунда переходит в следующий.");

            var sweeper = prefab.GetComponent<LooseItemSweeper>();
            Assert.IsNotNull(sweeper, "Нет LooseItemSweeper — магазины на полу лежат весь раунд.");

            var action = (LooseWeaponAction)typeof(LooseItemSweeper)
                .GetField("_weaponAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(sweeper);
            Assert.AreEqual(LooseWeaponAction.Keep, action, "В раунде ствол убитого можно подобрать — не трогать до конца раунда.");
        }
    }
}
