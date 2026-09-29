using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Куда стрелять (T-36): хитбоксы частей тела и маска пуль.
    ///
    /// <para>
    /// Что доказывает. У каждого аватара реестра есть хитбоксы головы, торса, обеих рук и — если в
    /// скелете есть ноги — обеих ног; все на слое <c>Hitbox</c>, сплошные, и других сплошных
    /// коллайдеров на аватаре нет (старые хитбоксы на <c>Default</c> сняты). В каждую часть тела
    /// реально попадает луч маской оружия — хотя бы с одной стороны она первая на пути. Маска пуль у
    /// всего оружия одна — <see cref="HitLayers.ProjectileMask"/>. У призрака хитбоксов нет.
    /// Генерирует всё это <c>Tools/VR Battlegrounds/Avatars/Build Hitboxes</c>.
    /// </para>
    /// </summary>
    public class HitboxTests
    {
        private static IEnumerable<TestCaseData> Avatars() =>
            RegisteredAvatars.Prefabs().Select(p => new TestCaseData(AssetDatabase.GetAssetPath(p)).SetName($"{{m}}({p.name})"));

        private static bool HasLegs(UxrAvatar avatar) =>
            avatar.AvatarRig.LeftLeg.UpperLeg != null && avatar.AvatarRig.RightLeg.UpperLeg != null;

        [Test]
        public void Слой_хитбоксов_есть()
        {
            Assert.GreaterOrEqual(HitLayers.HitboxLayer, 0, $"Нет слоя '{HitLayers.HitboxLayerName}' — собери: Tools/VR Battlegrounds/Avatars/Build Hitboxes.");
        }

        /// <summary>
        /// Хитбокс — только для луча пули: физически ни с чем не сталкивается, иначе тело игрока
        /// толкало бы оружие у руки при хвате, магазины и трупы.
        /// </summary>
        [Test]
        public void Хитбоксы_физически_ни_с_чем_не_сталкиваются()
        {
            int hitbox = HitLayers.HitboxLayer;
            Assume.That(hitbox, Is.GreaterThanOrEqualTo(0));

            var colliding = new List<string>();
            for (int layer = 0; layer < 32; layer++)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(layer))) continue;
                if (!Physics.GetIgnoreLayerCollision(hitbox, layer)) colliding.Add(LayerMask.LayerToName(layer));
            }
            Assert.That(colliding, Is.Empty, "Слой хитбоксов сталкивается с этими слоями в Physics Settings (пересобери хитбоксы).");
        }

        [TestCaseSource(nameof(Avatars))]
        public void У_аватара_хитбоксы_всех_частей_тела(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            UxrAvatar avatar = prefab.GetComponent<UxrAvatar>();
            Hitbox[] hitboxes = prefab.GetComponentsInChildren<Hitbox>(true);

            int Count(HitZone part) => hitboxes.Count(h => h.Part == part);
            Assert.GreaterOrEqual(Count(HitZone.Head), 1, $"{prefab.name}: нет хитбокса головы.");
            Assert.GreaterOrEqual(Count(HitZone.Torso), 1, $"{prefab.name}: нет хитбокса торса.");
            Assert.GreaterOrEqual(Count(HitZone.Arm), 2, $"{prefab.name}: нет хитбоксов обеих рук.");
            if (HasLegs(avatar))
                Assert.GreaterOrEqual(Count(HitZone.Leg), 2, $"{prefab.name}: в скелете есть ноги, а хитбоксов ног нет.");

            int layer = HitLayers.HitboxLayer;
            var wrong = new List<string>();
            foreach (Hitbox h in hitboxes)
            {
                Collider c = h.GetComponent<Collider>();
                if (c == null || c.isTrigger || !c.enabled) wrong.Add($"{h.name}: нет включённого сплошного коллайдера");
                if (h.gameObject.layer != layer) wrong.Add($"{h.name}: слой {LayerMask.LayerToName(h.gameObject.layer)}");
            }
            Assert.That(wrong, Is.Empty, $"{prefab.name}: хитбоксы настроены неверно.");

            List<string> stray = prefab.GetComponentsInChildren<Collider>(true)
                .Where(c => !c.isTrigger && c.GetComponent<Hitbox>() == null)
                .Select(c => AnimationUtility.CalculateTransformPath(c.transform, prefab.transform)).ToList();
            Assert.That(stray, Is.Empty, $"{prefab.name}: сплошные коллайдеры вне хитбоксов — пуля попадёт мимо части тела, труп и предметы будут с ними сталкиваться.");
        }

        [TestCaseSource(nameof(Avatars))]
        public void В_каждую_часть_тела_попадает_луч(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject instance = Object.Instantiate(prefab, new Vector3(0f, 0f, 500f), Quaternion.identity);
            try
            {
                Physics.SyncTransforms();
                var own = new HashSet<Collider>(instance.GetComponentsInChildren<Collider>(true));
                int mask = HitLayers.ProjectileMask;

                var missed = new List<string>();
                foreach (Hitbox hitbox in instance.GetComponentsInChildren<Hitbox>(true))
                {
                    Collider target = hitbox.GetComponent<Collider>();
                    Vector3 center = target.bounds.center;
                    bool reached = false;

                    for (int i = 0; i < 8 && !reached; i++)
                    {
                        Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                        reached = FirstHit(center + dir * 3f, -dir, mask, own) == target;
                    }
                    if (!reached) reached = FirstHit(center + Vector3.up * 3f, Vector3.down, mask, own) == target;

                    if (!reached) missed.Add($"{hitbox.Part}: {hitbox.name}");
                }

                Assert.That(missed, Is.Empty, $"{prefab.name}: в эти хитбоксы не попасть ни с одной стороны — их закрывают другие или маска пуль их не видит.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Collider FirstHit(Vector3 from, Vector3 dir, int mask, HashSet<Collider> own)
        {
            return Physics.RaycastAll(from, dir, 6f, mask, QueryTriggerInteraction.Ignore)
                          .Where(h => own.Contains(h.collider))
                          .OrderBy(h => h.distance)
                          .Select(h => h.collider)
                          .FirstOrDefault();
        }

        [Test]
        public void У_всего_оружия_одна_маска_пуль()
        {
            int expected = HitLayers.ProjectileMask;
            Assert.AreNotEqual(0, expected & (1 << HitLayers.HitboxLayer), "Маска пуль не видит хитбоксы.");

            var wrong = new List<string>();
            int shots = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" }))
            {
                var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (UxrProjectileSource source in weapon.GetComponentsInChildren<UxrProjectileSource>(true))
                    foreach (UxrShotDescriptor shot in source.ShotTypes)
                    {
                        shots++;
                        if (shot.CollisionLayerMask.value != expected) wrong.Add($"{weapon.name}: 0x{shot.CollisionLayerMask.value:X}");
                    }
            }
            Assert.Greater(shots, 0, "Не нашлось ни одного оружия.");
            Assert.That(wrong, Is.Empty, $"Маска пуль не HitLayers.ProjectileMask (0x{expected:X}) — пересобери: Build Hitboxes.");
        }

        [Test]
        public void У_призрака_хитбоксов_нет()
        {
            var registry = AssetDatabase.LoadAssetAtPath<AvatarRegistry>(RegisteredAvatars.RegistryPath);
            Assume.That(registry != null && registry.ghost != null && registry.ghost.prefab != null);
            Assert.IsEmpty(registry.ghost.prefab.GetComponentsInChildren<Hitbox>(true), "У призрака хитбоксы — в выбывшего можно попасть.");
        }
    }
}
