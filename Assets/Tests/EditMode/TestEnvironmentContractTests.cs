using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Tests
{
    public sealed class TestEnvironmentProbe : MonoBehaviour { }
    public sealed class TestSingletonProbe : UltimateXR.Core.Components.Singleton.UxrSingleton<TestSingletonProbe> { }

    public class TestEnvironmentContractTests
    {
        private GameObject _first;
        private GameObject _second;

        [Test]
        public void Мёртвый_кеш_singleton_сбрасывается_а_живой_сохраняется()
        {
            var field = typeof(UltimateXR.Core.Components.Singleton.UxrAbstractSingleton<TestSingletonProbe>)
                .GetField("s_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            _first = new GameObject("SingletonProbe");
            var probe = _first.AddComponent<TestSingletonProbe>();
            try
            {
                field.SetValue(null, probe);
                TestEnvironmentContract.ResetDestroyedSingleton<TestSingletonProbe>();
                Assert.That(field.GetValue(null), Is.SameAs(probe), "Живой singleton нельзя сбрасывать.");
                Object.DestroyImmediate(_first);
                field.SetValue(null, probe); // воспроизводим пропущенный SDK OnDestroy
                TestEnvironmentContract.ResetDestroyedSingleton<TestSingletonProbe>();
                Assert.That(field.GetValue(null), Is.Null);
            }
            finally
            {
                field.SetValue(null, null);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_first != null) Object.DestroyImmediate(_first);
            if (_second != null) Object.DestroyImmediate(_second);
        }

        [Test]
        public void Отсутствующий_обязательный_компонент_отклоняется()
        {
            Assert.Throws<AssertionException>(() => TestEnvironmentContract.ExactlyOneInScene<TestEnvironmentProbe>());
        }

        [Test]
        public void Единственный_объект_сцены_принимается()
        {
            _first = new GameObject("ContractProbe");
            var probe = _first.AddComponent<TestEnvironmentProbe>();
            Assert.That(TestEnvironmentContract.ExactlyOneInScene<TestEnvironmentProbe>(), Is.SameAs(probe));
            TestEnvironmentContract.IsActive(probe);
        }

        [Test]
        public void Выключенный_дубликат_тоже_нарушает_единственность()
        {
            _first = new GameObject("ContractProbe1");
            _first.AddComponent<TestEnvironmentProbe>();
            _second = new GameObject("ContractProbe2");
            _second.AddComponent<TestEnvironmentProbe>();
            _second.SetActive(false);
            Assert.Throws<AssertionException>(() => TestEnvironmentContract.ExactlyOneInScene<TestEnvironmentProbe>());
        }

        [Test]
        public void Выключенный_или_уничтоженный_объект_не_готов_к_работе()
        {
            _first = new GameObject("ContractProbe");
            var probe = _first.AddComponent<TestEnvironmentProbe>();
            _first.SetActive(false);
            Assert.Throws<AssertionException>(() => TestEnvironmentContract.IsActive(probe));
            Object.DestroyImmediate(_first);
            Assert.Throws<AssertionException>(() => TestEnvironmentContract.IsActive(probe));
        }
    }
}
