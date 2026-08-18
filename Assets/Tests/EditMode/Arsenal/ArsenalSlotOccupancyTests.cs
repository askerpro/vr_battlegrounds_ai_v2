using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Занятость слота арсенала — находка NET-13, задача T-15.
    ///
    /// Что доказывает. <c>ArsenalSlotController.IsItemPresent</c> читал
    /// <c>UxrGrabbableObjectAnchor.CurrentPlacedObject</c>, а тот заполняется только
    /// изнутри <c>UxrGrabManager</c>. Сетевое пополнение (<c>AssignNetworkItem</c>)
    /// через менеджер захвата не проходит: оно перепарентит уже заспавненный Mirror'ом
    /// объект под якорь. Значит для всего, что появилось на стене по сети,
    /// <c>CurrentPlacedObject</c> оставался null, <c>IsItemPresent</c> — false,
    /// а <c>NeedsReplenishment()</c> — true навсегда.
    ///
    /// Последствие, ради которого тест и написан: <c>ReplenishWeaponsNetwork(false)</c>
    /// в фазе <c>Setup</c> спавнит оружие во все слоты заново, поверх уже лежащего.
    /// До T-09 и T-13 второй <c>Setup</c> был недостижим, и дефект прятался.
    ///
    /// Почему ярус A (<see cref="MirrorTestHarness"/>), хотя сам слот — не
    /// <c>NetworkBehaviour</c>: нужны уборка созданных объектов и запись в приватные
    /// сериализованные поля. Сеть здесь не участвует.
    /// </summary>
    public class ArsenalSlotOccupancyTests : MirrorTestHarness
    {
        /// <summary>
        /// Слот с якорем и настроенным <see cref="WeaponInfo"/> — ровно то, что видит
        /// <c>ReplenishWeaponsNetwork</c> на боевой стене.
        /// </summary>
        private ArsenalSlotController CreateSlot(string name, out UxrGrabbableObjectAnchor anchor)
        {
            GameObject slotObject = CreateObject(name);

            GameObject anchorObject = new GameObject("ItemAnchor");
            anchorObject.transform.SetParent(slotObject.transform);
            anchor = anchorObject.AddComponent<UxrGrabbableObjectAnchor>();

            ArsenalSlotController slot = slotObject.AddComponent<ArsenalSlotController>();
            SetPrivateField(slot, "_itemAnchor", anchor);
            SetPrivateField(slot, "_weaponInfo", CreateWeaponInfo());

            return slot;
        }

        private WeaponInfo CreateWeaponInfo()
        {
            WeaponInfo info = ScriptableObject.CreateInstance<WeaponInfo>();
            SetPrivateField(info, "_displayName", "TestRifle");
            SetPrivateField(info, "_weaponId", "test_rifle");
            return info;
        }

        /// <summary>Заготовка «оружие, заспавненное сервером» — то, что приходит в AssignNetworkItem.</summary>
        private GameObject CreateSpawnedWeapon(string name)
        {
            GameObject weapon = CreateObject(name);
            weapon.AddComponent<UxrGrabbableObject>();
            return weapon;
        }

        // ── Сама находка ────────────────────────────────────────────────────

        [Test]
        public void Слот_после_сетевой_выдачи_предмета_считается_занятым()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot = CreateSlot("Slot", out UxrGrabbableObjectAnchor anchor);

            Assert.IsFalse(slot.IsItemPresent,
                "Контроль: пустой слот не имеет права считаться занятым.");

            slot.AssignNetworkItem(CreateSpawnedWeapon("Weapon"));

            Assert.IsTrue(slot.IsItemPresent,
                "Слот получил предмет через AssignNetworkItem, но считает себя пустым. " +
                "Это NET-13: занятость читается из UxrGrabbableObjectAnchor.CurrentPlacedObject, " +
                "который сетевая выдача не заполняет — она идёт мимо UxrGrabManager.");

            Assert.IsNull(anchor.CurrentPlacedObject,
                "Контроль: сетевая выдача через UxrGrabManager не проходит, поэтому " +
                "CurrentPlacedObject обязан остаться пустым. Если он заполнился, тест " +
                "проверяет уже не тот путь.");
        }

        [Test]
        public void Занятый_слот_не_требует_пополнения()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot = CreateSlot("Slot", out _);
            slot.AssignNetworkItem(CreateSpawnedWeapon("Weapon"));

            Assert.IsFalse(slot.NeedsReplenishment(),
                "Слот с оружием просит пополнения. Значит ReplenishWeaponsNetwork(false) " +
                "в фазе Setup заспавнит второе оружие поверх лежащего — и так каждый раунд.");
        }

        // ── Обратная сторона: слот обязан пустеть ───────────────────────────

        [Test]
        public void Слот_без_предмета_требует_пополнения()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot = CreateSlot("Slot", out _);

            Assert.IsTrue(slot.NeedsReplenishment(),
                "Пустой слот обязан просить пополнения — иначе оружие на стене не появится.");
        }

        [Test]
        public void Слот_пустеет_когда_предмет_унесли()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot = CreateSlot("Slot", out _);
            GameObject weapon = CreateSpawnedWeapon("Weapon");
            slot.AssignNetworkItem(weapon);

            // Так выглядит захват: UxrGrabManager перепарентит предмет к аватару
            // грабера (UseParenting у UxrGrabbableObject включён по умолчанию).
            weapon.transform.SetParent(null);

            Assert.IsFalse(slot.IsItemPresent,
                "Предмет унесли со стены, а слот всё ещё считает себя занятым. " +
                "Тогда он не пополнится никогда — это отказ ровно в другую сторону от NET-13.");
            Assert.IsTrue(slot.NeedsReplenishment(),
                "Опустевший слот обязан просить пополнения к следующему раунду.");
        }

        [Test]
        public void Слот_пустеет_когда_предмет_уничтожили()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot = CreateSlot("Slot", out _);
            GameObject weapon = CreateSpawnedWeapon("Weapon");
            slot.AssignNetworkItem(weapon);

            Object.DestroyImmediate(weapon);

            Assert.IsTrue(slot.NeedsReplenishment(),
                "Предмет уничтожен (деспавн по сети), а слот считает себя занятым.");
        }

        // ── Блокировка слота ────────────────────────────────────────────────

        [Test]
        public void Блокировка_слота_выключает_захват_выданного_предмета()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot = CreateSlot("Slot", out _);
            GameObject weapon = CreateSpawnedWeapon("Weapon");
            slot.AssignNetworkItem(weapon);

            UxrGrabbableObject grabbable = weapon.GetComponent<UxrGrabbableObject>();
            Assert.IsTrue(grabbable.enabled, "Контроль: до блокировки предмет обязан быть хватаемым.");

            slot.Lock();

            Assert.IsFalse(grabbable.enabled,
                "Слот заблокирован, а оружие со стены по-прежнему можно взять. " +
                "Lock() ищет предмет в CurrentPlacedObject — том самом поле из NET-13.");

            slot.Unlock();

            Assert.IsTrue(grabbable.enabled,
                "Слот разблокирован, но предмет так и остался незахватываемым.");
        }

        [Test]
        public void Предмет_выданный_в_заблокированный_слот_не_хватается()
        {
            SilenceMirrorNoise();

            ArsenalSlotController slot = CreateSlot("Slot", out _);

            // Боевой порядок: стена стартует закрытой и блокирует слоты в Start(),
            // а оружие приезжает позже — из OnStartServer.
            slot.Lock();

            GameObject weapon = CreateSpawnedWeapon("Weapon");
            slot.AssignNetworkItem(weapon);

            Assert.IsFalse(weapon.GetComponent<UxrGrabbableObject>().enabled,
                "Оружие выдано в закрытую стену и его можно взять до открытия арсенала.");
        }
    }
}
