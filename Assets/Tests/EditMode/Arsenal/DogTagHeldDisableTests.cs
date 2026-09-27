using System.Reflection;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Закрытая стена запрещает брать жетон, не ломая захват того, кто его уже держит.
    ///
    /// <para>
    /// <b>Дефект.</b> Время закупки истекло, пока игрок держал жетон: фаза ушла в
    /// <c>Countdown</c>, стена закрылась и позвала <see cref="DogTagController.Disable" />, а тот
    /// выключал компонент <c>UxrGrabbableObject</c>. UltimateXR на выключение молча стирает
    /// запись о захвате (<c>UxrGrabManager.GrabbableObject_Disabled</c>), и при отпускании
    /// клиент писал «RuntimeManipulationInfo not found for object DogTag».
    /// </para>
    ///
    /// <para>
    /// <b>Правило.</b> Компонент не выключается никогда; запрет — флаг <c>IsGrabbable</c>,
    /// который SDK читает только при поиске нового захвата
    /// (<c>UxrGrabManager.Querying</c>), а текущий не трогает.
    /// </para>
    /// </summary>
    public class DogTagHeldDisableTests
    {
        private GameObject _root;
        private DogTagController _controller;
        private UxrGrabbableObject _tag;

        [SetUp]
        public void Build()
        {
            _root = new GameObject("DogTagPanel");
            _controller = _root.AddComponent<DogTagController>();

            var anchorGo = new GameObject("DogTagAnchor");
            anchorGo.transform.SetParent(_root.transform, false);
            var anchor = anchorGo.AddComponent<UxrGrabbableObjectAnchor>();

            var tagGo = new GameObject("DogTag");
            tagGo.transform.SetParent(_root.transform, false);
            _tag = tagGo.AddComponent<UxrGrabbableObject>();

            // Awake в EditMode не зовётся — ссылки ставим сами, как их ставит префаб.
            SetField("_tagAnchor", anchor);
            SetField("_tagObject", _tag);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(DogTagController).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"В DogTagController нет поля {name}.");
            field.SetValue(_controller, value);
        }

        [Test]
        public void Закрытие_не_выключает_компонент_жетона()
        {
            _controller.Disable();

            Assert.IsTrue(_tag.enabled,
                "Стена выключила компонент жетона. Если жетон в этот момент в руке, UltimateXR " +
                "стирает запись о захвате, и отпускание падает с «RuntimeManipulationInfo not found».");
        }

        [Test]
        public void Закрытие_запрещает_новый_захват()
        {
            _controller.Disable();

            Assert.IsFalse(_tag.IsGrabbable,
                "Стена закрыта, а жетон со стойки по-прежнему можно взять.");
        }

        [Test]
        public void Новая_закупка_снова_разрешает_захват()
        {
            _controller.Disable();
            _controller.ResetTag();

            Assert.IsTrue(_tag.IsGrabbable, "Закупка началась, а жетон взять нельзя.");
            Assert.IsTrue(_tag.enabled, "Контроль: компонент жетона включён.");
        }
    }
}
