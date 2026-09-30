using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Урон пули по зоне (T-38): множитель применяется до события урона — тогда и «смертельный ли» (<c>Dies</c>),
    /// и вычитаемая жизнь, и метка/журнал урона видят один и тот же урон на каждой машине.
    /// </summary>
    public class HitZoneDamageTests
    {
        [Test]
        public void Множители_зон()
        {
            Assert.AreEqual(3f, HitZoneDamage.Multiplier(HitZone.Head));
            Assert.AreEqual(1f, HitZoneDamage.Multiplier(HitZone.Torso));
            Assert.AreEqual(1f, HitZoneDamage.Multiplier(HitZone.Arm));
            Assert.AreEqual(0.75f, HitZoneDamage.Multiplier(HitZone.Leg));
        }

        [TestCase(HitZone.Head, 30f, 90f)]
        [TestCase(HitZone.Torso, 30f, 30f)]
        [TestCase(HitZone.Leg, 40f, 30f)]
        public void Пуля_бьёт_по_зоне(HitZone zone, float damage, float expected)
        {
            UxrDamageEventArgs args = Shoot(zone, damage, 100f, out UxrActor actor, out GameObject root);
            try
            {
                Assert.IsNotNull(args, "Попадание не дошло до DamageReceiving.");
                Assert.AreEqual(expected, args.Damage, 0.001f, $"Урон в {zone} без множителя зоны.");
                Assert.AreEqual(100f - expected, actor.Life, 0.001f, "Жизнь уменьшена не на урон с множителем.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Смертельность_после_множителя()
        {
            // Урон отменяется в обработчике: смерть в edit mode зовёт Destroy (шум харнесса), а Dies уже посчитан.
            UxrDamageEventArgs args = Shoot(HitZone.Head, 40f, 100f, out _, out GameObject root, cancel: true);
            try
            {
                Assert.IsTrue(args.Dies, "40 в голову (×3 = 120) при 100 жизни — не смертельно: множитель применён после Dies.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Не_хитбокс_без_множителя()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Physics.SyncTransforms();
                Assert.IsTrue(Physics.Raycast(new Vector3(0f, 0f, -5f), Vector3.forward, out RaycastHit hit, 10f));
                Assert.AreEqual(30f, HitZoneDamage.Apply(hit, 30f));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        private static UxrDamageEventArgs Shoot(HitZone zone, float damage, float life, out UxrActor actor, out GameObject root, bool cancel = false)
        {
            root = new GameObject("Victim");
            actor = root.AddComponent<UxrActor>();
            var actorSo = new SerializedObject(actor);
            actorSo.FindProperty("_life").floatValue = life;
            actorSo.ApplyModifiedPropertiesWithoutUndo();

            var part = new GameObject("Part", typeof(SphereCollider));
            part.transform.SetParent(root.transform, false);
            part.transform.position = new Vector3(100f, 0f, 0f); // вдали от чужих коллайдеров сцены
            var hitboxSo = new SerializedObject(part.AddComponent<Hitbox>());
            hitboxSo.FindProperty("_part").enumValueIndex = (int)zone;
            hitboxSo.ApplyModifiedPropertiesWithoutUndo();

            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(new Vector3(100f, 0f, -5f), Vector3.forward, out RaycastHit hit, 10f), "Луч не попал в хитбокс.");

            UxrDamageEventArgs received = null;
            actor.DamageReceiving += (s, e) =>
            {
                received = e;
                if (cancel) e.Cancel();
            };
            actor.ReceiveImpact(null, hit, damage);
            return received;
        }
    }
}
