using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Выпущенную пулю видно в шлеме — тонким светлым следом, а не стрелкой.
    ///
    /// <para>
    /// <b>История.</b> У M16 и <c>Gun_real</c> пули не было видно: снаряд — меш пули UltimateXR
    /// 2 × 17 см при 150 м/с. Растянутый до 8 × 144 см белый меш стал виден, но выглядел
    /// «стрелкой размером с ладонь» — у меша острый нос. Теперь трассер — <see cref="TrailRenderer" />:
    /// след от пролёта, без формы снаряда.
    /// </para>
    ///
    /// <para>
    /// <b>Правило.</b> Оружие проекта (не сэмплы UltimateXR) стреляет снарядом со следом: не тоньше
    /// <see cref="MinWidth" />, живёт не меньше кадра на 72 Гц (иначе на Quest его не увидеть),
    /// светлый (не цветной, как у сэмплов SDK), без меша-формы и на шейдере URP.
    /// </para>
    /// </summary>
    public class TracerVisibilityTests
    {
        private const float QuestFrame = 1f / 72f;
        private const float MinWidth = 0.008f;
        private const float MinBrightness = 0.8f;

        [Test]
        public void Трассер_оружия_проекта_светлый_след_без_стрелки()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null || IsSdkSample(info.WeaponPrefab)) continue;

                var source = info.WeaponPrefab.GetComponent<UxrProjectileSource>();
                if (source == null) continue;

                for (int i = 0; i < source.ShotTypes.Count; i++)
                {
                    GameObject projectile = source.ShotTypes[i].ProjectilePrefab;
                    string name = $"{info.WeaponPrefab.name} (выстрел {i})";
                    checks++;

                    if (projectile == null) { failures.Add($"{name}: нет снаряда — пулю не видно совсем"); continue; }

                    if (projectile.GetComponentsInChildren<MeshRenderer>(true).Length > 0)
                        failures.Add($"{name}: у снаряда {projectile.name} меш — в шлеме это стрелка");

                    TrailRenderer trail = projectile.GetComponentInChildren<TrailRenderer>(true);
                    if (trail == null) { failures.Add($"{name}: у снаряда {projectile.name} нет следа (TrailRenderer)"); continue; }

                    float width = trail.widthCurve.Evaluate(0f) * trail.widthMultiplier;
                    Color head = trail.colorGradient.Evaluate(0f);

                    if (width < MinWidth) failures.Add($"{name}: след шириной {width * 100f:F1} см, нужно от {MinWidth * 100f:F1}");
                    if (trail.time < QuestFrame) failures.Add($"{name}: след живёт {trail.time * 1000f:F0} мс — меньше кадра на 72 Гц");
                    if (Mathf.Min(head.r, head.g, head.b) < MinBrightness) failures.Add($"{name}: след цветной ({head}), нужен светлый");

                    Material material = trail.sharedMaterial;
                    if (material == null || !material.shader.name.StartsWith("Universal Render Pipeline/"))
                        failures.Add($"{name}: материал следа {(material ? material.shader.name : "нет")} — не URP");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Контроль: оружия проекта со снарядом не найдено.");
            Assert.IsEmpty(failures, "Трассер пули:\n" + string.Join("\n", failures));
        }

        /// <summary>Сэмпл UltimateXR — префаб-вариант ассета из ThirdParty; у них свои трассеры.</summary>
        private static bool IsSdkSample(GameObject prefab)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            return source != null && AssetDatabase.GetAssetPath(source).StartsWith("Assets/ThirdParty/");
        }
    }
}
