using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.UI.Menu.Overview;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Монитор попаданий стрельбища (lobby-arena-cleanup, принято пользователем 2026-10-24): снимок своего
    /// попадания на главном экране планшета; урон — спад SDK по дистанции × множитель зоны щита, размеченной
    /// на префабе мишени (голова ×3 из <see cref="HitZoneDamage"/>, остальной щит — торс ×1).
    /// </summary>
    public class ShootingRangeMonitorTests
    {
        private const string TargetPath = "Assets/Prefabs/Environment/Lobby/ShootingTarget.prefab";
        private const float Eps = 1e-3f;

        private static Transform Pivot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath);
            Assert.IsNotNull(prefab, TargetPath);
            Transform pivot = prefab.transform.Find("Pivot");
            Assert.IsNotNull(pivot, "у мишени нет Pivot");
            return pivot;
        }

        [Test]
        public void Мишень_размечена_головой()
        {
            Transform pivot = Pivot();
            var head = pivot.Find("HeadZone")?.GetComponent<ShootingTargetZone>();
            Assert.IsNotNull(head, "на щите нет разметки HeadZone");
            Assert.AreEqual(HitZone.Head, head.Zone);

            // Голова силуэта — верх щита (0,6 × 0,7 м, центр на 0,36 м над Pivot); центр щита — торс.
            Assert.AreEqual(HitZone.Head, ShootingTargetZone.ZoneAt(pivot, pivot.TransformPoint(new Vector3(0f, .65f, .016f))));
            Assert.AreEqual(HitZone.Torso, ShootingTargetZone.ZoneAt(pivot, pivot.TransformPoint(new Vector3(0f, .36f, .016f))),
                "центр щита (кольца) — торс");
            Assert.AreEqual(HitZone.Torso, ShootingTargetZone.ZoneAt(pivot, pivot.TransformPoint(new Vector3(.2f, .65f, .016f))),
                "плечо рядом с головой — торс");
        }

        [Test]
        public void Голова_мишени_как_в_игре()
        {
            Assert.AreEqual(3f, HitZoneDamage.Multiplier(HitZone.Head), Eps, "голова ×3 (T-38)");
            Assert.AreEqual(1f, HitZoneDamage.Multiplier(HitZone.Torso), Eps);
        }

        [Test]
        public void Урон_спадает_по_дистанции_как_у_SDK()
        {
            var shot = new UxrShotDescriptor();
            Set(shot, "_projectileDamageNear", 40f);
            Set(shot, "_projectileDamageFar", 20f);
            Set(shot, "_projectileMaxDistance", 100f);
            Assert.AreEqual(40f, ShootingRangePhotoCapture.DamageAt(shot, 0f), Eps);
            Assert.AreEqual(35f, ShootingRangePhotoCapture.DamageAt(shot, 25f), Eps, "lerp(40; 20; 0,25)");
            Assert.AreEqual(20f, ShootingRangePhotoCapture.DamageAt(shot, 100f), Eps);
        }

        [Test]
        public void Подпись_монитора_урон_и_зона()
        {
            string text = OverviewRangeMonitor.Caption(81.4f, HitZone.Head, "Mk14", 15.24f, new Vector2(12.4f, -5.2f));
            StringAssert.Contains("Урон 81", text);
            StringAssert.Contains("голова ×3", text);
            StringAssert.Contains("Mk14 · 15.2 м", text.Replace(',', '.'));
            StringAssert.Contains("X +12 · Y -5 мм", text);
            StringAssert.Contains("торс ×1", OverviewRangeMonitor.Caption(27f, HitZone.Torso, "", 10f, Vector2.zero));
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                  .SetValue(target, value);
    }
}
