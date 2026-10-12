using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Weapons.Sights;
using VrBattlegrounds.Weapons.Sights;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Sights
{
    /// <summary>
    /// Инструментальный этап sight-iron-kits: комплект под масштабированным корнем оружия остаётся в реальном размере,
    /// ход и сдвиг решателя — метры мира. Механика прицелов пользователем ещё не принята.
    /// </summary>
    public sealed class SightKitScaleTests
    {
        private const float Z = 15f;

        [TestCase(.7f)]        // AR15
        [TestCase(4.5333433f)] // MP5K
        [TestCase(.2087f)]     // Uzi
        public void KitUnderScaledWeapon_StaysRealSize_AndSolvesInMeters(float weaponScale)
        {
            var kitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SightKitBuilder.Kits[0].PrefabPath);
            Assert.IsNotNull(kitPrefab);
            var root = new GameObject("Weapon");
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(1f, 1.2f, -2f), Quaternion.Euler(0f, 30f, 0f));
                root.transform.localScale = Vector3.one * weaponScale;
                var container = new GameObject("MeshContainer").transform;
                container.SetParent(root.transform, false);
                var muzzle = new GameObject("ShotSource").transform;
                muzzle.SetParent(root.transform, false);
                var kit = (GameObject)Object.Instantiate(kitPrefab, container, false);
                kit.transform.localPosition = new Vector3(0f, .05f, -.2f) / weaponScale;
                SightKitScale.Apply(kit.transform);

                Assert.IsNull(SightKitScale.Problem(kit.transform));
                Vector3 lossy = kit.transform.lossyScale;
                Assert.AreEqual(1f, lossy.x, 1e-5f); Assert.AreEqual(1f, lossy.y, 1e-5f); Assert.AreEqual(1f, lossy.z, 1e-5f);

                var parts = kit.GetComponentsInChildren<SightAdjustablePart>(true);
                Transform rear = parts.Single(p => p.Role == SightPartRole.Rear).transform;
                SightAdjustablePart moving = parts.Single(p => p.IsAdjustable);
                Vector3 axis = moving.transform.parent.TransformVector(Vector3.up);
                Assert.AreEqual(1f, axis.magnitude, 1e-5f, "единица хода детали = 1 м мира");

                // Точка T на 4 см выше продолжения линии R→F на 15 м вперёд от R: столбику нужно ≈ 0,8 мм.
                Vector3 forward = root.transform.forward, up = root.transform.up;
                Vector3 r = rear.position, f = moving.transform.position, dir = f - r;
                float along = Vector3.Dot(dir, forward);
                Vector3 target = r + dir * (Z / along) + up * .04f;
                muzzle.SetPositionAndRotation(target - forward * Z, root.transform.rotation);

                var result = SightAutoZeroSolver.Solve(new SightAutoZeroInput
                {
                    Rear = r, Front = f, MovingRole = moving.Role, MoveAxisPerUnit = axis, RearUp = rear.up, FrontUp = moving.transform.up,
                    MuzzlePosition = muzzle.position, MuzzleForward = muzzle.forward, MuzzleUp = muzzle.up, ZeroDistance = Z,
                    CurrentOffset = moving.Offset, TravelDown = moving.TravelDown, TravelUp = moving.TravelUp
                });
                Assert.IsTrue(result.Ok, result.Error);
                double expected = .04 * along / Z;
                Assert.AreEqual(expected, result.RequiredWorldShift, 2e-6, "сдвиг в метрах мира");
                Assert.AreEqual(result.RequiredWorldShift, result.RequiredDelta, 1e-5 * Math.Abs(result.RequiredDelta) + 1e-9, "ход и сдвиг в одних единицах");

                Vector3 before = moving.transform.position;
                Vector3 n = moving.NeutralLocalPosition;
                moving.transform.localPosition = new Vector3(n.x, n.y + (float)result.RequiredOffset, n.z);
                Assert.AreEqual(expected, (moving.transform.position - before).magnitude, 2e-6, "деталь сдвинулась на столько метров");
                // Линия R→F после сдвига приходит в T по вертикали.
                Vector3 nf = moving.transform.position, nd = nf - r;
                Vector3 hit = r + nd * (Vector3.Dot(target - r, forward) / Vector3.Dot(nd, forward));
                Assert.AreEqual(0f, Vector3.Dot(hit - target, up), 5e-5f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void NonUniformParent_Refuses()
        {
            var root = new GameObject("Weapon");
            try
            {
                root.transform.localScale = new Vector3(1f, 1f, 1.2f);
                var kit = new GameObject("SightKit_Test").transform;
                kit.SetParent(root.transform, false);
                Assert.Throws<InvalidOperationException>(() => SightKitScale.Apply(kit));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
