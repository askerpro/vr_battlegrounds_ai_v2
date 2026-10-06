using NUnit.Framework;
using UltimateXR.Extensions.System;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// <c>SerializeStateValue</c> с именем кэширует значение через <c>ObjectExt.DeepCopy</c>. Класс без
    /// <c>ICloneable</c> уходит в <c>BinaryFormatter</c>, и поле-компонент даёт <c>SerializationException</c> на
    /// каждом сохранении состояния. Снимок встроенного запаса ручного заряжания падал именно так.
    /// </summary>
    public class StateSaveValueCopyTests
    {
        private GameObject _store;

        [TearDown]
        public void TearDown()
        {
            if (_store != null) Object.DestroyImmediate(_store);
        }

        [Test]
        public void Снимок_встроенного_запаса_копируется_без_сериализатора()
        {
            _store = new GameObject("FixedStore");
            var mag = _store.AddComponent<UxrFirearmMag>();
            var snapshots = new[] { new UxrFixedAmmoSnapshot { TriggerIndex = 0, Rounds = 5, Store = mag } };

            UxrFixedAmmoSnapshot[] copy = null;
            Assert.DoesNotThrow(() => copy = snapshots.DeepCopy());
            Assert.AreNotSame(snapshots[0], copy[0], "Кэш должен хранить копию, а не тот же объект.");
            Assert.AreSame(mag, copy[0].Store, "Ссылка на компонент копируется как есть.");
            Assert.AreEqual(5, copy[0].Rounds);
        }
    }
}
