using System;
using System.Collections.Generic;
using System.Linq;
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
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                foreach (string dependency in AssetDatabase.GetDependencies(scene.path, true))
                {
                    if (!dependency.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

                    // Только компоненты: в зависимостях сцены бывают и служебные editor-классы ассетов (метаданные
                    // Shader Graph и т.п.) — их в сцене нет.
                    Type type = AssetDatabase.LoadAssetAtPath<MonoScript>(dependency)?.GetClass();
                    if (type == null || !typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type)) continue;

                    string assembly = type.Assembly.GetName().Name;
                    if (project.Contains(assembly) && !player.Contains(assembly))
                        problems.Add($"{scene.path}: {type.Name} из сборки '{assembly}' (только редактор)");
                }
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void Настройки_хранятся_вне_ассетов_и_возвращаются()
        {
            int savedBots = DebugBootstrapSettings.BotCount;
            string savedMap = DebugBootstrapSettings.AutoLoadMapScene;
            try
            {
                DebugBootstrapSettings.BotCount = 7;
                DebugBootstrapSettings.AutoLoadMapScene = "TestMap2";

                Assert.AreEqual(7, EditorPrefs.GetInt(DebugBootstrapSettings.KeyPrefix + "BotCount", -1));
                Assert.AreEqual("TestMap2", SessionState.GetString(DebugBootstrapSettings.KeyPrefix + "AutoLoadMapScene", ""),
                    "Карта автозапуска — только на текущую сессию редактора.");
                Assert.IsEmpty(AssetDatabase.FindAssets("t:ScriptableObject DebugBootstrapConfig"),
                    "Ассета с отладочными настройками быть не должно.");
            }
            finally
            {
                DebugBootstrapSettings.BotCount = savedBots;
                DebugBootstrapSettings.AutoLoadMapScene = savedMap;
            }
        }

        private static HashSet<string> PlayerAssemblies()
        {
            return new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(a => a.name));
        }
    }
}
