using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Оружие в руке размером с настоящее.
    ///
    /// <para>
    /// Модели пришли из разных паков в разных масштабах: <c>Gun_real</c> был 30 см против 20 см
    /// у Glock, M16 — 120 см против 100. В шлеме это сразу видно рядом с рукой аватара.
    /// Масштаб правится на корне префаба: точки хвата, якоря и ход затвора уменьшаются вместе
    /// с моделью, а UltimateXR при смене родителя сохраняет мировой масштаб.
    /// </para>
    ///
    /// <para>
    /// Новое оружие — добавь эталон в <see cref="RealLengths" />, иначе тест упадёт.
    /// </para>
    /// </summary>
    public class WeaponScaleTests
    {
        private static readonly string[] WeaponRoots = { "Assets/Prefabs/Weapons" };

        // Наибольший габарит с вложенным магазином, метры. Эталон — реальный прототип.
        private static readonly Dictionary<string, Vector2> RealLengths = new Dictionary<string, Vector2>
        {
            { "BrowningHiPower",         new Vector2(0.17f, 0.23f) }, // Browning Hi-Power: принятый игровой диапазон сохраняется
            { "Gun",              new Vector2(0.17f, 0.23f) }, // пистолет сэмпла UltimateXR
            { "AR15", new Vector2(0.95f, 1.05f) }, // AR-15: принятый игровой диапазон сохраняется
            { "Shotgun",          new Vector2(0.80f, 1.10f) }, // Remington 870: 100–106 см
            { "FabarmSDASS",     new Vector2(0.66f, 0.78f) }, // FABARM SDASS: принятый игровой диапазон сохраняется
            { "Machinegun",       new Vector2(0.85f, 1.20f) }, // ручной пулемёт / штурмовая винтовка
            { "Grenade",          new Vector2(0.08f, 0.12f) }, // M67: 9 см, с запалом 11

            // T-38, пак Hands: размер — по руке пака (длина в паке × 0,707, как у Gun_real), чтобы хват из
            // клипа пака ложился в ладонь; там, где модель пака крупнее прототипа, это отмечено.
            { "Scar",             new Vector2(0.60f, 0.70f) }, // SCAR-L CQC: 63.5 см
            { "Uzi",              new Vector2(0.30f, 0.40f) }, // Mini-Uzi со сложенным прикладом: 36 см (Micro-Uzi 25 — модель пака крупнее)
            { "MP5K",             new Vector2(0.29f, 0.37f) }, // HK MP5K: 32.5 см
            { "PPK",              new Vector2(0.17f, 0.23f) }, // Walther PPK: 15.5 см; модель пака крупнее — по руке пака 20 см
            { "Revolver",         new Vector2(0.30f, 0.40f) }, // Taurus Raging Bull 6.5": ~31 см; по руке пака 35
            { "SniperRifle",      new Vector2(1.05f, 1.25f) }, // AX-50: ~123 см; по руке пака 115

            // T-39, пак KINEMATION: модели в реальном масштабе, корень ×1.
            { "SRM12",            new Vector2(0.85f, 1.00f) }, // Desert Tech SRS A2 (булл-пап, ствол 26"): ~95 см; модель 91
            { "Mk14",             new Vector2(0.90f, 1.12f) }, // Mk 14 EBR: 89–112 см по прикладу; модель 101
            { "AK105",            new Vector2(0.75f, 0.87f) }, // АК-105: 82 см с разложенным прикладом; модель 80
            { "R08",              new Vector2(0.24f, 0.33f) }, // S&W 686 6": ~30 см; модель 27
            { "Viper",            new Vector2(0.19f, 0.26f) }, // 2011/1911 5": 22 см
            { "MKR9",             new Vector2(0.48f, 0.62f) }, // ПП 9 мм с упором (SIG MPX): ~55–60 см; модель 54
            { "Herrington",       new Vector2(0.95f, 1.15f) }, // Remington 11-87: ~100–115 см; модель 100
            // M4A1-S: карабин со сложенным прикладом ~76 см + глушитель; модель с глушителем 86 см. Длиннее «нормы двуручного»
            // T-38 (~0,75 м) — как и остальные стволы KINEMATION реального масштаба: уменьшать масштаб нельзя, хват из клипа
            // пака перестанет ложиться в ладонь.
            { "TR15",             new Vector2(0.80f, 0.92f) }
        };

        [Test]
        public void WeaponPrefabs_MatchRealWorldSize()
        {
            var failures = new List<string>();
            int checks   = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", WeaponRoots))
            {
                var path   = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // Оружие — то, что стреляет или взрывается; магазины проверяет второй тест.
                if (prefab.GetComponent<UxrFirearmWeapon>() == null && prefab.GetComponent<UxrGrenadeWeapon>() == null) continue;

                if (!RealLengths.TryGetValue(prefab.name, out Vector2 range))
                {
                    failures.Add($"{path}: нет эталона размера в WeaponScaleTests.RealLengths");
                    continue;
                }

                checks++;
                float length = MaxDimension(prefab);
                if (length < range.x || length > range.y)
                    failures.Add($"{prefab.name}: {length * 100f:F1} см, ожидается {range.x * 100f:F0}–{range.y * 100f:F0} см " +
                                 $"(масштаб корня ×{(range.x + range.y) * 0.5f / length * prefab.transform.localScale.x:F2})");
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного оружия — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Оружие не реального размера:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Магазин из кармана того же размера, что вставленный в оружие. Вложенный магазин живёт
        /// под масштабом оружия, выданный — под своим корнем; разойдутся — магазин будет
        /// меняться в размере при вставке.
        /// </summary>
        [Test]
        public void ArsenalMagazine_SameSizeAsNested()
        {
            var failures = new List<string>();
            int checks   = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null || info.MagazinePrefab == null) continue;

                var nested = info.WeaponPrefab.GetComponentInChildren<UxrFirearmMag>(true);
                if (nested == null) continue;

                checks++;
                float nestedScale = nested.transform.lossyScale.x;
                float looseScale  = info.MagazinePrefab.transform.lossyScale.x;

                if (Mathf.Abs(nestedScale - looseScale) > 0.01f)
                    failures.Add($"{info.name}: вложенный {nested.name} ×{nestedScale:F3}, выдаваемый {info.MagazinePrefab.name} ×{looseScale:F3}");
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного оружия с магазином — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Магазин меняет размер при вставке:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Наибольший габарит мешей в осях корня с учётом его масштаба — как предмет выглядит в мире.
        /// </summary>
        private static float MaxDimension(GameObject prefab)
        {
            Transform root  = prefab.transform;
            Matrix4x4 toRoot = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix;
            var       bounds = new Bounds();
            bool      any    = false;

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                          : renderer.GetComponent<MeshFilter>() is MeshFilter filter ? filter.sharedMesh : null;
                if (mesh == null) continue;

                Matrix4x4 m = toRoot * renderer.transform.localToWorldMatrix;
                Bounds    b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p  = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }

            return any ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) : 0f;
        }
    }
}
