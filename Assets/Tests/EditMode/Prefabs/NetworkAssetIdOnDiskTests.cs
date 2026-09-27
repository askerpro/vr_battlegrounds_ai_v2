using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// У каждого сетевого префаба на диске записан канонический <c>_assetId</c> —
    /// <see cref="NetworkIdentity.AssetGuidToUint" /> от GUID самого префаба.
    ///
    /// <para>
    /// <b>Зачем.</b> Для сети число с диска не важно: Mirror пересчитывает его при загрузке,
    /// и в сборку уходит правильное (troubleshooting, «В диффе префаба _assetId»). Важен шум:
    /// поле писали два механизма по-разному. Mirror при загрузке в редакторе выставляет
    /// канон и помечает префаб грязным, а сохранение через <c>LoadPrefabContents</c> /
    /// <c>SaveAsPrefabAsset</c> пишет 0 (варианту — id базы), потому что в изолированной
    /// сцене Mirror префаб не узнаёт. Значение скакало в диффах туда-обратно.
    /// </para>
    ///
    /// <para>
    /// Канон на диске держит <c>NetworkAssetIdNormalizer</c> (постпроцессор импорта,
    /// <c>Assets/Editor/VR_Battlegrounds/VersionControl/</c>). Тест ловит, если он отвалился.
    /// </para>
    /// </summary>
    public class NetworkAssetIdOnDiskTests
    {
        private static readonly string[] Roots = { "Assets/Prefabs" };

        private static readonly Regex OwnValue =
            new Regex(@"^  _assetId: (\d+)", RegexOptions.Multiline);

        private static readonly Regex OverrideValue =
            new Regex(@"propertyPath: _assetId\r?\n\s+value: (\d+)", RegexOptions.Multiline);

        /// <summary>
        /// Значение на диске: у обычного префаба — строка компонента, у варианта —
        /// переопределение в <c>m_Modifications</c>. Нет ни того, ни другого — null
        /// (вариант наследует id базы, то есть заведомо не свой).
        /// </summary>
        public static uint? ReadOnDisk(string path)
        {
            string text = File.ReadAllText(path);

            Match own = OwnValue.Match(text);
            if (own.Success) return uint.Parse(own.Groups[1].Value);

            Match over = OverrideValue.Match(text);
            if (over.Success) return uint.Parse(over.Groups[1].Value);

            return null;
        }

        [Test]
        public void Сетевые_префабы_хранят_канонический_assetId()
        {
            var wrong = new List<string>();
            int checks = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", Roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<NetworkIdentity>() == null) continue;

                checks++;
                uint canonical = NetworkIdentity.AssetGuidToUint(new Guid(guid));
                uint? onDisk = ReadOnDisk(path);

                if (onDisk != canonical)
                    wrong.Add($"{path}: на диске {(onDisk.HasValue ? onDisk.ToString() : "нет значения")}, канон {canonical}");
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного сетевого префаба — тест ничего не проверил.");
            Assert.IsEmpty(wrong,
                "У сетевых префабов на диске не канонический _assetId — будет скакать в диффах.\n" +
                "Исправляет Tools/VR Battlegrounds/VersionControl/Normalize Network Asset Ids.\n" +
                string.Join("\n", wrong));
        }
    }
}
