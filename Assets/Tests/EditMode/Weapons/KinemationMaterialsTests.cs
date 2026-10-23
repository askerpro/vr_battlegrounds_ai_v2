using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Упаковка металличности и гладкости материалов KINEMATION в URP Lit (KinemationMaterials, Assembly-CSharp-Editor)
    /// совпадает с шейдером пака <c>Shader_TacticalShooterWeapon</c>: гладкость = lerp(x, y, 1 − шероховатость),
    /// металличность = lerp(x, y, карта), карты читаются линеаризованными (импорт пака sRGB). Прежняя формула
    /// 1 − lerp(x, y, шероховатость) без линеаризации давала «хром» (SRM-12 гладкость 0,82 вместо 0,55).
    /// Тон альбедо (AlbedoBoost: цель 0,055, не больше ×1,6) — тоже принят пользователем 2026-10-23.
    /// Принято пользователем 2026-10-23 (арсенал Lobby в worker, задача weapon-metal-materials).
    /// </summary>
    public class KinemationMaterialsTests
    {
        private const float Eps = 0.004f;

        // Тестовая asmdef не может ссылаться на predefined Assembly-CSharp-Editor.
        private static Type Materials()
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VrBattlegrounds.Editor.Gameplay.KinemationMaterials"))
                .FirstOrDefault(t => t != null);
            Assert.IsNotNull(type, "Editor assembly с KinemationMaterials не загружена.");
            return type;
        }

        private static Color Pack(float metallic, float roughness, Vector2 metallicRemap, Vector2 roughnessRemap) =>
            (Color)Materials().GetMethod("PackPixel", BindingFlags.Public | BindingFlags.Static)
                              .Invoke(null, new object[] { metallic, roughness, metallicRemap, roughnessRemap });

        [Test]
        public void Smoothness_RemapsInvertedRoughness()
        {
            // Диапазон SRM-12: шероховатость (0; 0,6). Гладкость не выше 0,6 при любой карте.
            var remap = new Vector2(0f, 0.6f);
            Assert.AreEqual(0.6f, Pack(0f, 0f, Vector2.up, remap).a, Eps, "карта 0 — гладкость = верх диапазона");
            Assert.AreEqual(0f, Pack(0f, 1f, Vector2.up, remap).a, Eps, "карта 1 — гладкость = низ диапазона");
            Assert.AreEqual(0.48f, Pack(0f, 0.2f, Vector2.up, remap).a, Eps, "lerp(0; 0,6; 0,8)");
        }

        [Test]
        public void Metallic_RemapsMapIntoAllChannels()
        {
            Color c = Pack(0.5f, 0.5f, new Vector2(0.2f, 0.8f), new Vector2(0.2f, 0.7f));
            Assert.AreEqual(0.5f, c.r, Eps);
            Assert.AreEqual(c.r, c.g, Eps);
            Assert.AreEqual(c.r, c.b, Eps);
            Assert.AreEqual(0.45f, c.a, Eps, "lerp(0,2; 0,7; 0,5)");
        }

        private static float Boost(float mean) =>
            (float)Materials().GetMethod("AlbedoBoost", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { mean });

        [Test]
        public void AlbedoBoost_FollowsAcceptedRule()
        {
            // Формула на опорных средних: 0,055 / среднее, в пределах 1..1,6, округление 0,01. Значения в .mat
            // (Kinemation/Materials) — производные Convert от фактических средних карт, не константы этого теста.
            Assert.AreEqual(1.31f, Boost(0.042f), Eps, "0,055 / 0,042");
            Assert.AreEqual(1.57f, Boost(0.035f), Eps, "0,055 / 0,035");
            Assert.AreEqual(1.06f, Boost(0.052f), Eps, "0,055 / 0,052");
            Assert.AreEqual(1.6f, Boost(0.017f), Eps, "тёмная карта — потолок");
            Assert.AreEqual(1f, Boost(0.108f), Eps, "светлее цели — не трогается");
            Assert.AreEqual(1f, Boost(0.548f), Eps, "светлая карта (патроны)");
            Assert.AreEqual(1f, Boost(0f), Eps, "нет карты");
        }

        [Test]
        public void Read_LinearizesSrgbMap()
        {
            // Серый 128 в sRGB-текстуре шейдер видит как ~0,216, а не 0,502.
            var src = new Texture2D(4, 4, TextureFormat.RGBA32, false, false);
            try
            {
                src.SetPixels32(Enumerable.Repeat(new Color32(128, 128, 128, 255), 16).ToArray());
                src.Apply();
                var pixels = (Color[])Materials().GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)
                                                 .Invoke(null, new object[] { src });
                Assert.AreEqual(Mathf.GammaToLinearSpace(128f / 255f), pixels[pixels.Length / 2].r, 0.01f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(src);
            }
        }
    }
}
