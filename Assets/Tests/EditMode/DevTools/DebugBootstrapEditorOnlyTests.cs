using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Быстрые отладочные сценарии (<see cref="DebugOrchestrator"/>, <see cref="DebugBootstrapSettings"/>) живут
    /// только в редакторе: в сборке им делать нечего, а их настройки — личные настройки разработчика, не ассет в git.
    /// </summary>
    public class DebugBootstrapEditorOnlyTests
    {
        [TestCase(typeof(DebugOrchestrator))]
        [TestCase(typeof(VRScreenshotCapture))]
        public void Отладочный_инструмент_не_попадает_в_сборку_плеера(Type tool)
        {
            string assembly = tool.Assembly.GetName().Name;
            Assert.IsFalse(PlayerAssemblies().Contains(assembly),
                $"{tool.Name} лежит в сборке плеера '{assembly}' — он нужен только редактору.");
        }

        /// <summary>
        /// Класс ошибки: компонент из сборки, которой нет в плеере, оставлен в сцене или префабе билда — в сборке
        /// на его месте «missing script». Ловит любой такой компонент, не только оркестратор.
        /// </summary>
        [Test]
        public void В_сценах_билда_нет_компонентов_из_сборок_только_для_редактора()
        {
            HashSet<string> player = PlayerAssemblies();
            HashSet<string> project = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name));

            var problems = new List<string>();
            string[] scenes = ReleaseScenes();
            Assert.IsNotEmpty(scenes, "Resolver релизной сборки не вернул ни одной сцены.");
            foreach (string scene in scenes)
            {
                foreach (string dependency in AssetDatabase.GetDependencies(scene, true))
                {
                    if (!dependency.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

                    // Только компоненты: в зависимостях сцены бывают и служебные editor-классы ассетов (метаданные
                    // Shader Graph и т.п.) — их в сцене нет.
                    Type type = AssetDatabase.LoadAssetAtPath<MonoScript>(dependency)?.GetClass();
                    if (type == null || !typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type)) continue;

                    string assembly = type.Assembly.GetName().Name;
                    if (project.Contains(assembly) && !player.Contains(assembly))
                        problems.Add($"{scene}: {type.Name} из сборки '{assembly}' (только редактор)");
                }
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void Настройки_хранятся_вне_ассетов_и_возвращаются()
        {
            if (PlayLaunchSettings.RequestActive || PlayLaunchSettings.HasLaunchConfiguration)
                Assert.Ignore("Проверка профиля требует отсутствия активного launch request; его владелец и snapshot не изменяются.");

            const string environmentKey = "VRBG_LAUNCH_CONFIG";
            string savedPath = Environment.GetEnvironmentVariable(environmentKey);
            string mapKey = DebugBootstrapSettings.KeyPrefix + nameof(DebugBootstrapSettings.AutoLoadMapScene);
            string missingMap = Guid.NewGuid().ToString("N");
            string savedMap = SessionState.GetString(mapKey, missingMap);
            string directory = Path.Combine(Path.GetTempPath(), "VRBG-DebugBootstrapTests-" + Guid.NewGuid().ToString("N"));
            string profilePath = Path.Combine(directory, "play-launch.json");
            try
            {
                var store = ProfileStore(profilePath);
                store.Save(new PlayLaunchConfiguration { BotCount = 1, SceneSource = "active", ScenePath = "" });
                Environment.SetEnvironmentVariable(environmentKey, profilePath);
                Assert.AreEqual(profilePath, PlayLaunchSettings.ProfilePath, "Тест должен использовать только собственный JSON.");

                DebugBootstrapSettings.BotCount = 7;
                DebugBootstrapSettings.AutoLoadMapScene = "Warehouse";

                // Независимый store проверяет запись на диск, минуя кэш PlayLaunchSettings.
                PlayLaunchConfiguration persisted = ProfileStore(profilePath).Read();
                Assert.AreEqual(7, persisted.BotCount, "BotCount должен сохраниться в checkout-профиле JSON.");
                Assert.AreEqual("active", persisted.SceneSource);
                Assert.IsEmpty(persisted.ScenePath, "Сессионная карта не должна попадать в постоянный профиль.");
                Assert.AreEqual(7, DebugBootstrapSettings.BotCount);
                Assert.AreEqual("Warehouse", DebugBootstrapSettings.AutoLoadMapScene);
                Assert.AreEqual("Warehouse", SessionState.GetString(mapKey, ""),
                    "Карта автозапуска — только на текущую сессию редактора.");
                Assert.IsEmpty(AssetDatabase.FindAssets("t:ScriptableObject DebugBootstrapConfig"),
                    "Ассета с отладочными настройками быть не должно.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentKey, savedPath);
                if (savedMap == missingMap) SessionState.EraseString(mapKey);
                else SessionState.SetString(mapKey, savedMap);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static PlayLaunchProfileStore ProfileStore(string path)
        {
            return new PlayLaunchProfileStore(PlayLaunchSettings.CheckoutRoot, PlayLaunchSettings.Decode,
                config => UnityEngine.JsonUtility.ToJson(config, true), () => PlayLaunchSettings.RequestActive, path);
        }

        private static string[] ReleaseScenes()
        {
            // Тестовая asmdef не может ссылаться на predefined Assembly-CSharp-Editor.
            Type resolver = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("VrBattlegrounds.EditorTools.BuildSceneResolver"))
                .FirstOrDefault(type => type != null);
            Assert.IsNotNull(resolver, "Editor assembly с BuildSceneResolver не загружена.");
            MethodInfo resolve = resolver.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(bool), typeof(IEnumerable<string>) }, null);
            Assert.IsNotNull(resolve, "Не найден штатный BuildSceneResolver.Resolve.");
            try { return (string[])resolve.Invoke(null, new object[] { false, null }); }
            catch (TargetInvocationException exception)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException ?? exception).Throw();
                throw;
            }
        }

        private static HashSet<string> PlayerAssemblies()
        {
            return new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(a => a.name));
        }
    }
}
