using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Оружие и магазины не носят материалы, встроенные в модель (<c>.fbx</c>).
    ///
    /// <para>
    /// Модели пака Hands несут заглушки <c>NN - Default</c> — серый Lit без текстур (у части — только альбедо).
    /// Настоящие материалы пак назначает в своих префабах <c>Prefabs/Hands_*.prefab</c>. Сборщик брал материал у
    /// рендерера модели, и SCAR, Uzi, снайперка, <c>Shotgun_real</c> вышли серо-белыми.
    /// </para>
    /// </summary>
    public class WeaponMaterialTests
    {
        [Test]
        public void У_оружия_нет_материалов_из_модели()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    checks++;
                    string source = AssetDatabase.GetAssetPath(m).ToLowerInvariant();
                    if (source.EndsWith(".fbx") || source.EndsWith(".obj"))
                        failures.Add($"{prefab.name}/{r.name}: «{m.name}» — заглушка из модели {AssetDatabase.GetAssetPath(m)}");
                }
            }

            Assert.Greater(checks, 0, "Контроль: материалов оружия не найдено.");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
