using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Core.Components;
using UltimateXR.Core.Unique;
using UltimateXR.Exceptions;
using UltimateXR.Extensions.System.IO;
using UltimateXR.Manipulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Ссылка на UXR-компонент в сетевом событии называет предмет, а не только его id
    /// (патч 8 в <c>Docs/UltimateXR/sdk-patches.md</c>).
    ///
    /// <para>
    /// <b>Зачем.</b> <c>UxrComponentNotFoundException</c> сообщал только id: «Id is ae118e67-…».
    /// Что это за предмет, принимающая сторона знать не может — компонента у неё нет, — и
    /// расследование сводилось к перебору <c>Combine(id префаба, netId)</c> по всем префабам.
    /// Теперь отправитель кладёт рядом с id путь и тип компонента, и ошибка называет предмет сама.
    /// </para>
    ///
    /// <para>
    /// Ненайденный компонент моделируется честно: id выставляется прямо в сериализованное поле
    /// и в реестр <c>UxrUniqueIdImplementer</c> не попадает — ровно как предмет, существующий
    /// только на машине отправителя.
    /// </para>
    /// </summary>
    public class UniqueComponentDebugInfoTests
    {
        private GameObject _root;

        [TearDown]
        public void Cleanup()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            UxrUniqueIdDebugInfo.IncludeInSerialization = UxrUniqueIdDebugInfo.DefaultIncludeInSerialization;
        }

        /// <summary>Якорь с id, которого нет в реестре: для читателя он «чужой».</summary>
        private UxrGrabbableObjectAnchor CreateUnregisteredComponent(out Guid id)
        {
            _root = new GameObject("DebugInfoWall");
            var child = new GameObject("DogTagAnchor");
            child.transform.SetParent(_root.transform, false);
            var anchor = child.AddComponent<UxrGrabbableObjectAnchor>();

            id = Guid.NewGuid();
            FieldInfo field = typeof(UxrComponent).GetField("_uxrUniqueId", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "В UxrComponent нет поля _uxrUniqueId — SDK обновился, тест надо поправить.");
            field.SetValue(anchor, id.ToString());
            FieldInfo cached = typeof(UxrComponent).GetField("_cachedGuid", BindingFlags.NonPublic | BindingFlags.Instance);
            cached?.SetValue(anchor, Guid.Empty);

            Assert.AreEqual(id, anchor.UniqueId, "Контроль: компонент отдаёт выставленный id.");
            return anchor;
        }

        private static byte[] Write(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                write(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static BinaryReader Reader(byte[] bytes) => new BinaryReader(new MemoryStream(bytes));

        [Test]
        public void Ненайденный_компонент_называет_предмет_отправителя()
        {
            UxrGrabbableObjectAnchor anchor = CreateUnregisteredComponent(out Guid id);
            byte[] bytes = Write(w => w.WriteUniqueComponent(anchor));

            var ex = Assert.Throws<UxrComponentNotFoundException>(
                () => Reader(bytes).ReadUniqueComponent(UxrConstants.Serialization.CurrentBinaryVersion));

            Assert.AreEqual(id, ex.UniqueId, "Исключение потеряло id.");
            StringAssert.Contains("DebugInfoWall/DogTagAnchor", ex.Message,
                "Сообщение не называет предмет — по одному id его на принимающей стороне не опознать.\n" +
                "Фактически: " + ex.Message);
            StringAssert.Contains(nameof(UxrGrabbableObjectAnchor), ex.Message,
                "Сообщение не называет тип компонента.\nФактически: " + ex.Message);
        }

        [Test]
        public void Без_отладочной_строки_формат_прежний_и_читается()
        {
            UxrUniqueIdDebugInfo.IncludeInSerialization = false;
            UxrGrabbableObjectAnchor anchor = CreateUnregisteredComponent(out Guid id);
            byte[] bytes = Write(w => w.WriteUniqueComponent(anchor));

            // Прежний формат: флаг (1 байт) + Guid (16 байт) и ничего больше.
            Assert.AreEqual(17, bytes.Length,
                "С выключенной отладкой ссылка обязана занимать прежние 17 байт — это release-трафик.");

            var ex = Assert.Throws<UxrComponentNotFoundException>(
                () => Reader(bytes).ReadUniqueComponent(UxrConstants.Serialization.CurrentBinaryVersion));
            Assert.AreEqual(id, ex.UniqueId);
        }

        [Test]
        public void Null_ссылка_читается_как_null_и_не_ломает_поток()
        {
            byte[] bytes = Write(w =>
            {
                w.WriteUniqueComponent(null);
                w.Write(42);
            });

            using (BinaryReader reader = Reader(bytes))
            {
                Assert.IsNull(reader.ReadUniqueComponent(UxrConstants.Serialization.CurrentBinaryVersion));
                Assert.AreEqual(42, reader.ReadInt32(), "После null-ссылки поток съехал.");
            }
        }

        [Test]
        public void Отладочная_строка_не_сдвигает_следующие_данные()
        {
            UxrGrabbableObjectAnchor anchor = CreateUnregisteredComponent(out _);
            byte[] bytes = Write(w =>
            {
                w.WriteUniqueComponent(anchor);
                w.Write(42);
            });

            using (BinaryReader reader = Reader(bytes))
            {
                Assert.Throws<UxrComponentNotFoundException>(
                    () => reader.ReadUniqueComponent(UxrConstants.Serialization.CurrentBinaryVersion));
                Assert.AreEqual(42, reader.ReadInt32(),
                    "Читатель не дочитал отладочную строку до конца, и следующее поле события съехало.");
            }
        }
    }
}
