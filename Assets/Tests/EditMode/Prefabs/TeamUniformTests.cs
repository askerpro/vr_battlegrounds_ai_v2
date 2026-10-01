using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Форма в цветах команды — шейдером, а не запечёнными текстурами (<c>Docs/avatar-team-colors.md</c>).
    ///
    /// <para>
    /// Каждый зарегистрированный аватар рисует тело шейдером <see cref="TeamUniformColors.ShaderName"/> с маской
    /// (R — одежда, G — экипировка, B — каска) и несёт <see cref="TeamUniformColors"/>, который передаёт в шейдер
    /// <see cref="TeamData.mainColor"/> / <see cref="TeamData.additionalColor"/>. Иначе правка цветов команды
    /// не дойдёт до игрока.
    /// </para>
    /// </summary>
    public class TeamUniformTests
    {
        /// <summary>
        /// Зарегистрированные аватары, у модели которых есть маска формы (<c>&lt;модель&gt;_TeamMask.png</c> рядом с FBX
        /// меша тела). Модели без маски (киборг) красить нечем — им правило не нужно.
        /// </summary>
        public static IEnumerable<string> Avatars() =>
            RegisteredAvatars.Prefabs()
                             .Where(p => p.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => MaskOf(r.sharedMesh) != null))
                             .Select(AssetDatabase.GetAssetPath).Distinct().OrderBy(p => p);

        private static Texture2D MaskOf(Mesh mesh)
        {
            if (mesh == null) return null;
            string model = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(model) || !model.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return null;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(model.Substring(0, model.Length - 4) + "_TeamMask.png");
        }

        [Test]
        public void Есть_аватары_с_маской_формы()
        {
            Assert.That(Avatars(), Is.Not.Empty, "Ни у одного зарегистрированного аватара нет модели с маской формы");
        }

        [Test]
        public void Шейдер_формы_собирается_без_ошибок()
        {
            Shader shader = Shader.Find(TeamUniformColors.ShaderName);
            Assert.IsNotNull(shader, $"Нет шейдера {TeamUniformColors.ShaderName}");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader),
                string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(m => $"{m.severity}: {m.message} ({m.file}:{m.line})")));
        }

        [TestCaseSource(nameof(Avatars))]
        public void Тело_аватара_на_шейдере_формы_с_маской(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            SkinnedMeshRenderer[] body = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                               .Where(r => MaskOf(r.sharedMesh) != null)
                                               .ToArray();
            Assert.That(body, Is.Not.Empty, $"{path}: нет мешей модели с маской");

            var wrong = body.Where(r => r.sharedMaterial == null || r.sharedMaterial.shader.name != TeamUniformColors.ShaderName)
                            .Select(r => $"{r.name}: {(r.sharedMaterial ? r.sharedMaterial.shader.name : "нет материала")}");
            Assert.That(wrong, Is.Empty, $"{path}: тело не на шейдере формы — цвета команды не применятся");

            var noMask = body.Select(r => r.sharedMaterial).Distinct().Where(m => m.GetTexture("_TeamMask") == null).Select(m => m.name);
            Assert.That(noMask, Is.Empty, $"{path}: у материала нет маски _TeamMask");
        }

        [TestCaseSource(nameof(Avatars))]
        public void Аватар_красится_цветами_команды(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab.GetComponent<TeamUniformColors>(), $"{path}: нет TeamUniformColors — цвета команды не дойдут до шейдера");
            Assert.That(TeamUniformColors.UniformRenderers(prefab).Length, Is.GreaterThan(0), $"{path}: TeamUniformColors не найдёт рендереров формы");
        }

        [Test]
        public void Цвета_доходят_до_блока_свойств_с_силой_в_альфе()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Renderer renderer = go.GetComponent<Renderer>();
                Color main = TeamUniformColors.WithStrength(new Color(0.1f, 0.2f, 0.3f, 0.5f), 0.75f);
                Color additional = TeamUniformColors.WithStrength(Color.gray, 2f);

                Color helmet = TeamUniformColors.WithStrength(Color.black, 0.5f);
                TeamUniformColors.ApplyTo(new[] { renderer }, main, additional, helmet, new MaterialPropertyBlock());

                var read = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(read);
                Color got = read.GetColor("_TeamMainColor");
                Assert.AreEqual(0.1f, got.r, 1e-3f, "Цвет одежды не дошёл");
                Assert.AreEqual(0.75f, got.a, 1e-3f, "Альфа одежды — сила, а не альфа цвета команды");
                Assert.AreEqual(1f, read.GetColor("_TeamAdditionalColor").a, 1e-3f, "Сила зажимается в 0..1");
                Assert.AreEqual(0.5f, read.GetColor("_TeamHelmetColor").a, 1e-3f, "Цвет головы (каска, очки) не дошёл");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
