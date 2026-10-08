using System;
using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Extensions.System;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Манифест UniqueId сгенерированных слотов (генератор арсенала, задача 2b).
    ///
    /// Что доказывает. ID роли выводится только из общего входа — ключа запуска, станции, логического слота
    /// и роли, — поэтому одинаков на сервере, клиенте и у позднего клиента. Golden-значения приняты пробой
    /// в Unity (24/24): изменение кодека, схемы или полей seed молча развело бы машины — тест это ловит.
    /// </summary>
    public class ArsenalGeneratedIdentityManifestTests
    {
        private static readonly MapRunKey Run = new MapRunKey(new Guid("00112233-4455-6677-8899-aabbccddeeff"), 1);

        [Test]
        public void Golden_значения_кодека_не_меняются()
        {
            Assert.AreEqual("0d0126cc-9349-8463-bb20-0aa11beffafd", ArsenalGeneratedIdentityManifest.RoleBaseUid("item-anchor").ToString());
            Assert.AreEqual("c08ba3eb-e78b-8836-9af1-5e014a5a32eb", ArsenalGeneratedIdentityManifest.RoleBaseUid("magazine-anchor").ToString());
            Assert.AreEqual("5badfe30-c0ef-8945-9c04-a67e1c9ce487",
                ArsenalGeneratedIdentityManifest.SemanticSeed(Run, "station-A", "MP5K", "item-anchor").ToString());
            Assert.AreEqual("fdbfafef-49c2-bee4-3fdc-3d1fcf87a078",
                ArsenalGeneratedIdentityManifest.ExpectedUniqueId(Run, "station-A", "MP5K", "item-anchor").ToString());
        }

        [Test]
        public void Ожидаемый_ID_это_Combine_базы_роли_и_seed()
        {
            Guid expected = GuidExt.Combine(ArsenalGeneratedIdentityManifest.RoleBaseUid("item-anchor"),
                ArsenalGeneratedIdentityManifest.SemanticSeed(Run, "station-A", "MP5K", "item-anchor"));
            Assert.AreEqual(expected, ArsenalGeneratedIdentityManifest.ExpectedUniqueId(Run, "station-A", "MP5K", "item-anchor"));
        }

        [Test]
        public void Каждое_поле_входа_разделяет_ID()
        {
            Guid id = ArsenalGeneratedIdentityManifest.ExpectedUniqueId(Run, "station-A", "MP5K", "item-anchor");
            Assert.AreNotEqual(id, ArsenalGeneratedIdentityManifest.ExpectedUniqueId(new MapRunKey(Run.SessionEpoch, 2), "station-A", "MP5K", "item-anchor"), "новый запуск");
            Assert.AreNotEqual(id, ArsenalGeneratedIdentityManifest.ExpectedUniqueId(Run, "station-B", "MP5K", "item-anchor"), "другая станция");
            Assert.AreNotEqual(id, ArsenalGeneratedIdentityManifest.ExpectedUniqueId(Run, "station-A", "MKR9", "item-anchor"), "другое оружие");
            Assert.AreNotEqual(id, ArsenalGeneratedIdentityManifest.ExpectedUniqueId(Run, "station-A", "MP5K", "magazine-anchor"), "другая роль");
            Assert.AreNotEqual(id, ArsenalGeneratedIdentityManifest.ExpectedUniqueId(Run, "station-A", "mp5k", "item-anchor"), "регистр не нормализуется");
        }

        [Test]
        public void Сто_логических_слотов_дают_сто_разных_ID()
        {
            var ids = new HashSet<Guid>();
            for (int i = 0; i < 100; i++)
                ids.Add(ArsenalGeneratedIdentityManifest.ExpectedUniqueId(Run, "station-A", "logical-" + i, "item-anchor"));
            Assert.AreEqual(100, ids.Count);
        }

        [Test]
        public void Неверный_вход_именованный_отказ()
        {
            Assert.Throws<ArgumentException>(() => ArsenalGeneratedIdentityManifest.SemanticSeed(default, "s", "w", "item-anchor"));
            Assert.Throws<ArgumentException>(() => ArsenalGeneratedIdentityManifest.SemanticSeed(Run, " ", "w", "item-anchor"));
            Assert.Throws<ArgumentException>(() => ArsenalGeneratedIdentityManifest.RoleBaseUid("proxy-unknown"));
        }

        [Test]
        public void Таблица_ролей_шаблона_проверяется_целиком()
        {
            var item = new ArsenalTemplateRole("item-anchor", "89c0d0ea-3fd5-4fc0-b1c5-423d2e2293bd", "", "");
            var mag = new ArsenalTemplateRole("magazine-anchor", "97037b98-5d60-49d9-95a6-fdac2fcc4c5a", "", "");
            string error;
            Assert.IsTrue(ArsenalGeneratedIdentityManifest.TryValidateTemplateRoles(new[] { item, mag }, out error), error);
            Assert.IsFalse(ArsenalGeneratedIdentityManifest.TryValidateTemplateRoles(new[] { item }, out error));
            Assert.AreEqual("ArsenalIdentity.MissingRole:magazine-anchor", error);
            Assert.IsFalse(ArsenalGeneratedIdentityManifest.TryValidateTemplateRoles(new[] { item, item }, out error));
            StringAssert.StartsWith("ArsenalIdentity.DuplicateRole", error);
            Assert.IsFalse(ArsenalGeneratedIdentityManifest.TryValidateTemplateRoles(
                new[] { item, new ArsenalTemplateRole("magazine-anchor", item.SourceUniqueId, "", "") }, out error));
            StringAssert.StartsWith("ArsenalIdentity.DuplicateSourceUniqueId", error);
            Assert.IsFalse(ArsenalGeneratedIdentityManifest.TryValidateTemplateRoles(
                new[] { item, mag, new ArsenalTemplateRole("proxy", "58b94516-d22a-4f54-a20b-940a68dfbcb0", "", "") }, out error));
            StringAssert.StartsWith("ArsenalIdentity.UnknownRole", error);
        }
    }
}
