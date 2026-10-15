using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Tests.DevTools
{
    public class BuildSceneResolverTests
    {
        private const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";
        private const string EditorOnlyScene = "Assets/Scenes/Debug/BotCombatStand.unity";
        private const string EditorOnlyScript = "Assets/Scripts/Debug/BotCombatStand/BotCombatStand.cs";

        [Test]
        public void Automation_отказывает_явно_запрошенной_сцене_с_компонентом_вне_player_сборок()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath);
            Assert.IsNotNull(catalog);
            Assert.IsTrue(catalog.DebugMaps.Any(map => map != null && map.sceneName == "BotCombatStand"));
            Assert.IsTrue(EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == EditorOnlyScene));

            Type component = AssetDatabase.LoadAssetAtPath<MonoScript>(EditorOnlyScript)?.GetClass();
            Assert.IsNotNull(component, "Контроль: настоящий компонент BotCombatStand должен быть загружен.");
            Assert.IsTrue(typeof(MonoBehaviour).IsAssignableFrom(component));
            string assembly = component.Assembly.GetName().Name;
            Assert.IsTrue(AssemblyNames(AssembliesType.Editor).Contains(assembly));
            Assert.IsFalse(AssemblyNames(AssembliesType.PlayerWithoutTestAssemblies).Contains(assembly),
                "Контроль: BotCombatStand остаётся Editor/MPPM стендом, без player-сборки.");

            var error = Assert.Throws<InvalidOperationException>(() => Resolve(true, "BotCombatStand"));
            StringAssert.StartsWith("AutomationScenePlayerAssemblyMissing:BotCombatStand:", error.Message);
            StringAssert.Contains(assembly, error.Message);
            StringAssert.Contains(EditorOnlyScript, error.Message);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Без_явного_запроса_editor_only_стенд_не_попадает_в_player_список(bool automation)
        {
            string[] scenes = Resolve(automation);

            CollectionAssert.DoesNotContain(scenes, EditorOnlyScene);
            CollectionAssert.Contains(scenes, "Assets/Scenes/Offline.unity");
            CollectionAssert.Contains(scenes, "Assets/Scenes/Lobby.unity");
        }

        [Test]
        public void Для_незарегистрированной_fixture_сохраняется_ошибка_отсутствующей_сцены()
        {
            const string missing = "__BuildSceneResolverMissingFixture__";

            var error = Assert.Throws<InvalidOperationException>(() => Resolve(true, missing));

            Assert.AreEqual("AutomationSceneMissing:" + missing, error.Message);
        }

        [Test]
        public void Совместимая_fixture_включается_только_по_явному_automation_запросу()
        {
            const string sceneName = "TestMap1";
            const string scenePath = "Assets/Scenes/Maps/TestMap1.unity";
            var catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath);
            Assert.IsNotNull(catalog);
            Assert.IsTrue(EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == scenePath));
            FieldInfo debugMaps = typeof(MapRuntimeCatalog).GetField("_debugMaps", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(debugMaps);
            var original = (MapData[])debugMaps.GetValue(catalog);
            var fixture = ScriptableObject.CreateInstance<MapData>();
            fixture.sceneName = sceneName;
            fixture.kind = MapRunKind.Debug;
            bool wasDirty = EditorUtility.IsDirty(catalog);
            try
            {
                // Только память: не сохраняем и не помечаем каталог изменённым; ссылка восстанавливается в finally.
                debugMaps.SetValue(catalog, (original ?? Array.Empty<MapData>()).Concat(new[] { fixture }).ToArray());

                CollectionAssert.DoesNotContain(Resolve(false), scenePath);
                CollectionAssert.DoesNotContain(Resolve(true), scenePath);
                CollectionAssert.Contains(Resolve(true, sceneName), scenePath);
                CollectionAssert.DoesNotContain(Resolve(true, sceneName), EditorOnlyScene);
                Assert.AreEqual(wasDirty, EditorUtility.IsDirty(catalog), "Resolver не должен изменять asset каталога.");
            }
            finally
            {
                debugMaps.SetValue(catalog, original);
                UnityEngine.Object.DestroyImmediate(fixture);
            }
            Assert.AreSame(original, debugMaps.GetValue(catalog));
        }

        private static HashSet<string> AssemblyNames(AssembliesType type)
        {
            return new HashSet<string>(CompilationPipeline.GetAssemblies(type).Select(assembly => assembly.name));
        }

        private static string[] Resolve(bool automation, params string[] required)
        {
            // Resolver живёт в Assembly-CSharp-Editor; тестовый asmdef не может ссылаться на неё напрямую.
            Type resolver = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("VrBattlegrounds.EditorTools.BuildSceneResolver", false))
                .FirstOrDefault(type => type != null);
            Assert.IsNotNull(resolver);
            MethodInfo resolve = resolver.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(resolve);
            try
            {
                return (string[])resolve.Invoke(null, new object[] { automation, required });
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }
}
