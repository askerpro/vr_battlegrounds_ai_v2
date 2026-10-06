using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// Карман выбирает предмет для оружия в другой руке по <b>статической</b> совместимости — тегу гнезда,
    /// а не по валидаторам размещения. Валидаторы отвечают на «можно положить сейчас» (у
    /// <c>CartridgeIntake</c> — «патрон последним держал этот игрок», «в трубке есть место»), и у патрона,
    /// только что выданного в карман, ответ всегда «нет»: карман не отдавал патрон, пока дробовик в руке.
    /// </summary>
    public class MagazinePocketSelectionTests
    {
        private GameObject _weapon;
        private GameObject _item;

        [TearDown]
        public void TearDown()
        {
            if (_weapon != null) Object.DestroyImmediate(_weapon);
            if (_item != null) Object.DestroyImmediate(_item);
        }

        private UxrGrabbableObjectAnchor CreateWeaponWithSlot(string compatibleTag)
        {
            _weapon = new GameObject("Weapon");
            _weapon.AddComponent<UxrGrabbableObject>();
            var slot = new GameObject("Slot");
            slot.transform.SetParent(_weapon.transform, false);
            var anchor = slot.AddComponent<UxrGrabbableObjectAnchor>();
            var tags = new SerializedObject(anchor).FindProperty("_compatibleTags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).stringValue = compatibleTag;
            tags.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            return anchor;
        }

        private UxrGrabbableObject CreateItem(string tag)
        {
            _item = new GameObject("Item");
            var grabbable = _item.AddComponent<UxrGrabbableObject>();
            var settings = new SerializedObject(grabbable);
            settings.FindProperty("_tag").stringValue = tag;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return grabbable;
        }

        [Test]
        public void Патрон_подходит_оружию_даже_когда_гнездо_сейчас_не_принимает()
        {
            var anchor = CreateWeaponWithSlot("Cartridge:Herrington");
            anchor.AddPlacingValidator(_ => false);
            var cartridge = CreateItem("Cartridge:Herrington");

            Assert.IsFalse(anchor.IsCompatibleObject(cartridge), "Предусловие: валидатор размещения отказывает.");
            Assert.IsTrue(UxrMagazinePocket.Fits(cartridge, _weapon.GetComponent<UxrGrabbableObject>()),
                "Карман должен считать патрон подходящим по тегу гнезда, не по разрешению «положить сейчас».");
        }

        [Test]
        public void Чужой_патрон_оружию_не_подходит()
        {
            CreateWeaponWithSlot("Cartridge:Herrington");
            var cartridge = CreateItem("Cartridge:FabarmSDASS");

            Assert.IsFalse(UxrMagazinePocket.Fits(cartridge, _weapon.GetComponent<UxrGrabbableObject>()));
        }

        [Test]
        public void С_оружием_в_руке_карман_отдаёт_подходящий_иначе_ничего()
        {
            Assert.AreEqual(1, UxrMagazinePocket.ChooseMagazine(2, i => i == 1, holdingWeapon: true));
            Assert.AreEqual(-1, UxrMagazinePocket.ChooseMagazine(2, _ => false, holdingWeapon: true));
            Assert.AreEqual(1, UxrMagazinePocket.ChooseMagazine(2, _ => false, holdingWeapon: false),
                "Без оружия в другой руке — последний положенный.");
        }
    }
}
