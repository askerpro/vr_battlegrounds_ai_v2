using NUnit.Framework;
using UltimateXR.Core.Unique;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Идентичность объектов UltimateXR, заспавненных в рантайме — находка NET-16.
    ///
    /// <para>
    /// Что доказывает. Канал состояния UltimateXR адресует компоненты по <c>UniqueId</c>.
    /// У объекта, созданного в рантайме, этот идентификатор на каждой машине свой, и
    /// событие захвата приезжает на другую машину неразрешимым:
    /// <c>UxrComponentNotFoundException</c>. Замер на выделенном сервере: у одного и того
    /// же предмета <c>netId=77</c> сервер видел <c>638f948e-…</c>, клиент — <c>a4c824e5-…</c>.
    /// Через тот же канал идут все манипуляции — захват, выстрел, вставка магазина, —
    /// то есть не работало вообще ничего.
    /// </para>
    ///
    /// <para>
    /// Второй процесс для проверки не нужен. Свойство, которое лечит находку, локальное:
    /// «один и тот же префаб, выровненный по одному и тому же <c>netId</c>, обязан получить
    /// один и тот же <c>UniqueId</c>». Если оно держится в пределах процесса, то держится
    /// и между процессами: исходные идентификаторы приезжают из общего ассета, а
    /// <c>netId</c> раздаёт сервер.
    /// </para>
    ///
    /// <para>
    /// Все инстансы делаются из одного источника: у двух независимо созданных объектов
    /// идентификаторы разные с самого начала, и сравнивать их бессмысленно — на разных
    /// машинах инстансы приходят из общего ассета.
    /// </para>
    /// </summary>
    public class NetworkUxrIdentityTests : MirrorTestHarness
    {
        private const uint ProbeNetId = 4242;

        private GameObject _source;

        [SetUp]
        public void CreateSource()
        {
            SilenceMirrorNoise();

            _source = CreateObject("SourceProbe");
            _source.AddComponent<UxrGrabbableObject>();

            GameObject child = new GameObject("Attachment");
            child.transform.SetParent(_source.transform);
            child.AddComponent<UxrGrabbableObjectAnchor>();

            // Источник в выравнивании не участвует: он играет роль префаба на диске.
            _source.SetActive(false);
        }

        /// <summary>Инстанс «префаба» — то, что на боевой машине создаёт спавн.</summary>
        private GameObject Clone()
        {
            return Object.Instantiate(_source);
        }

        /// <summary>
        /// Убирает инстанс так, как это происходит в игре: сначала компоненты снимают
        /// свои идентификаторы с учёта, потом объект уничтожается.
        ///
        /// В рантайме снятие с учёта делает <c>UxrComponent.OnDestroy</c>, но в EditMode
        /// Unity не зовёт ни <c>Awake</c>, ни <c>OnDestroy</c> у объекта, который ни разу
        /// не был активен, — и уничтоженный двойник продолжал бы занимать идентификатор.
        /// </summary>
        private static void Remove(GameObject instance)
        {
            foreach (IUxrUniqueId unique in instance.GetComponentsInChildren<IUxrUniqueId>(true))
                unique.Unregister();

            Object.DestroyImmediate(instance);
        }

        // ── Сама находка ────────────────────────────────────────────────────

        [Test]
        public void Выравнивание_по_одному_netId_даёт_одинаковый_UniqueId()
        {
            SilenceMirrorNoise();

            GameObject first = Clone();
            NetworkUxrIdentity.Align(first, ProbeNetId);
            System.Guid firstId = first.GetComponent<UxrGrabbableObject>().UniqueId;

            // Уничтожаем до второго инстанса: живой двойник дал бы столкновение
            // идентификаторов, а машины друг о друге ничего не знают и столкновений
            // между собой не видят.
            Remove(first);

            GameObject second = Clone();
            NetworkUxrIdentity.Align(second, ProbeNetId);
            System.Guid secondId = second.GetComponent<UxrGrabbableObject>().UniqueId;

            Remove(second);

            Assert.AreEqual(firstId, secondId,
                "Один префаб, один netId — а UniqueId разные. Значит на сервере и на клиенте " +
                "они тоже разойдутся, и ни одно событие манипуляции не будет применено (NET-16).");
        }

        [Test]
        public void Выравнивание_разводит_разные_netId()
        {
            SilenceMirrorNoise();

            GameObject first = Clone();
            NetworkUxrIdentity.Align(first, ProbeNetId);
            System.Guid firstId = first.GetComponent<UxrGrabbableObject>().UniqueId;

            GameObject second = Clone();
            NetworkUxrIdentity.Align(second, ProbeNetId + 1);
            System.Guid secondId = second.GetComponent<UxrGrabbableObject>().UniqueId;

            Remove(first);
            Remove(second);

            Assert.AreNotEqual(firstId, secondId,
                "Два разных предмета получили один UniqueId — адресация канала состояния " +
                "перестанет их различать.");
        }

        [Test]
        public void Выравнивание_доходит_до_вложенных_компонентов()
        {
            SilenceMirrorNoise();

            GameObject probe = Clone();
            UxrGrabbableObjectAnchor nested = probe.GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
            System.Guid before = nested.UniqueId;

            NetworkUxrIdentity.Align(probe, ProbeNetId);
            System.Guid after = nested.UniqueId;

            Remove(probe);

            Assert.AreNotEqual(before, after,
                "Вложенный компонент остался с идентификатором префаба. У оружия так устроен " +
                "якорь магазина: без выравнивания вставка магазина не доедет до другой машины.");
        }

        [Test]
        public void Повторное_выравнивание_ничего_не_сдвигает()
        {
            SilenceMirrorNoise();

            GameObject probe = Clone();

            NetworkUxrIdentity.Align(probe, ProbeNetId);
            System.Guid once = probe.GetComponent<UxrGrabbableObject>().UniqueId;

            NetworkUxrIdentity.Align(probe, ProbeNetId);
            System.Guid twice = probe.GetComponent<UxrGrabbableObject>().UniqueId;

            Remove(probe);

            Assert.AreEqual(once, twice,
                "Комбинация не идемпотентна сама по себе, поэтому выравнивание обязано " +
                "распознавать уже выровненный объект. Иначе хост (сервер плюс клиент " +
                "в одном процессе) сдвинул бы идентификаторы дважды.");
        }

        // ── Расхождение по «Auto Anchor» ────────────────────────────────────

        [Test]
        public void Созданный_инстанс_рождается_без_авто_якоря_и_выключенным()
        {
            SilenceMirrorNoise();

            _source.GetComponent<UxrGrabbableObject>().AutoCreateStartAnchor = true;

            GameObject instance = NetworkUxrIdentity.CreateInstance(_source);

            Assert.IsNotNull(instance, "Инстанс не создан.");
            Assert.IsFalse(instance.activeSelf,
                "Инстанс вернулся включённым: Awake отработает раньше, чем его успеют настроить.");
            Assert.IsFalse(instance.GetComponent<UxrGrabbableObject>().AutoCreateStartAnchor,
                "У инстанса остался включённым авто-якорь. UXR создаст объекту лишнего родителя, " +
                "которого на другой машине нет, и событие захвата станет неразрешимым (NET-16).");
            Assert.IsNull(instance.transform.parent,
                "Инстанс остался в выключенном контейнере — Mirror пришлёт ему локальные координаты, " +
                "и предмет окажется не там, где на сервере.");

            Remove(instance);
        }
    }
}
