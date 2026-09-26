using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Каждый символ текста в UI-префабах есть в атласе его шрифта (или в fallback).
    ///
    /// <para>
    /// Символ, которого нет ни в шрифте, ни в цепочке fallback, TextMeshPro рисует
    /// пустым квадратом — в редакторе это легко пропустить, а в шлеме меню превращается
    /// в «кракозябры». Так было с UI-01: динамический <c>LiberationSans SDF - Fallback</c>
    /// выкинули из git, а статические шрифты меню (LiberationSans, RUBIK, LATO) содержали
    /// только латиницу — вся кириллица меню пропала разом.
    /// </para>
    ///
    /// <para>
    /// <c>tryAddCharacter: false</c> — проверяется то, что реально запечено в атласы,
    /// а не то, что динамический шрифт сумел бы дорисовать в редакторе.
    /// </para>
    /// </summary>
    public class UiFontCoverageTests
    {
        private static readonly string[] UiPrefabRoots = { "Assets/Prefabs/UI" };

        /// <summary>Строка-образец: буквы, которых чаще всего не хватает.</summary>
        private const string CyrillicSample = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя№«»—";

        [Test]
        public void AllUiPrefabTexts_HaveGlyphsForEveryCharacter()
        {
            var failures = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", UiPrefabRoots))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (string.IsNullOrEmpty(text.text)) continue;

                    if (text.font == null)
                    {
                        failures.Add($"{path} / {text.name}: шрифт не назначен");
                        continue;
                    }

                    if (!text.font.HasCharacters(text.text, out uint[] missing, true, false))
                    {
                        var chars = new string(missing.Select(c => (char)c).Distinct().ToArray());
                        failures.Add($"{path} / {text.name} [{text.font.name}]: нет глифов «{chars}»");
                    }
                }
            }

            Assert.IsEmpty(failures, "Тексты с отсутствующими глифами:\n" + string.Join("\n", failures));
        }

        [Test]
        public void DefaultTmpFont_CoversCyrillic()
        {
            // Шрифт по умолчанию достаётся каждому новому TMP-тексту и тексту из кода.
            var font = TMP_Settings.defaultFontAsset;
            Assert.IsNotNull(font, "В TMP Settings не задан шрифт по умолчанию");

            // При успехе TMP возвращает missing == null, поэтому сообщение строится только при отказе.
            if (!font.HasCharacters(CyrillicSample, out uint[] missing, true, false))
                Assert.Fail($"Шрифт по умолчанию {font.name} не содержит: " +
                            new string(missing.Select(c => (char)c).ToArray()));
        }
    }
}
