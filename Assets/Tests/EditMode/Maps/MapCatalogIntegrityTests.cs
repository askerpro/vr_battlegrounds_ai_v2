using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Авторская целостность запуска карт: центральный каталог и MapRoot каждой карты каталога (реестр меню и
    /// отладочные стенды). Перенесено из временных probes root-preflight/catalog-adversarial.
    ///
    /// <para>
    /// Что доказывает. Каталог валиден против реальных зарегистрированных сетевых префабов (судья, координатор,
    /// режимы, оружие, магазины); у каждой карты каталога есть запечённый отпечаток; MapRoot каждой сцены проходит
    /// полную проверку привязок со сканом сетевых и UltimateXR-идентичностей; ни в одной сцене нет сценового
    /// MapReferee или координатора — неуправляемого пути нет. Отпечатки содержимого пересчитывает сборка
    /// (<c>MapCatalogBuildStep</c>), здесь они не сверяются.
    /// </para>
    /// </summary>
    public class MapCatalogIntegrityTests
    {
        private const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";
        private const string ManagersPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";

        private static MapRuntimeCatalog Catalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "Нет центрального каталога запуска карт: " + CatalogPath);
            return catalog;
        }

        private static List<GameObject> RegisteredPrefabs()
        {
            var managers = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPath);
            var manager = managers != null ? managers.GetComponentInChildren<GameNetworkManager>(true) : null;
            Assert.IsNotNull(manager, "В постоянных менеджерах нет GameNetworkManager.");
            Assert.AreSame(Catalog(), new SerializedObject(manager).FindProperty("_mapRuntimeCatalog").objectReferenceValue,
                "GameNetworkManager ссылается не на центральный каталог.");
            return manager.spawnPrefabs;
        }

        [Test]
        public void Каталог_валиден_против_зарегистрированных_префабов()
        {
            MapCatalogValidation validation = Catalog().Validate(RegisteredPrefabs());
            CollectionAssert.IsEmpty(validation.Errors, "Каталог запуска карт не проходит проверку.");
        }

        [Test]
        public void У_каждой_карты_каталога_есть_отпечаток_содержимого()
        {
            MapRuntimeCatalog catalog = Catalog();
            foreach (var map in catalog.AllMaps)
                Assert.IsFalse(string.IsNullOrEmpty(catalog.ContentFingerprintFor(map)),
                    $"Карта '{map.sceneName}' без запечённого отпечатка — Rebake Catalog.");
        }

        [Test]
        public void MapRoot_каждой_карты_каталога_проходит_проверку_и_сценовых_служб_нет()
        {
            var failures = new List<string>();
            foreach (var entry in Catalog().EditorContent)
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(entry.ScenePath);
                try
                {
                    MapRoot[] roots = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapRoot>(true)).ToArray();
                    if (roots.Length != 1)
                    {
                        failures.Add($"{entry.Map.sceneName}: MapRoot {roots.Length}");
                        continue;
                    }
                    if (roots[0].GetComponent<MapBootstrap>() == null) failures.Add(entry.Map.sceneName + ": нет MapBootstrap");
                    if (scene.GetRootGameObjects().Any(r => r.GetComponentInChildren<MapReferee>(true) != null))
                        failures.Add(entry.Map.sceneName + ": сценовый MapReferee — неуправляемый путь");
                    failures.AddRange(roots[0].ValidateBindings().Errors.Take(10).Select(e => entry.Map.sceneName + ": " + e));
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            CollectionAssert.IsEmpty(failures, "Карты каталога не готовы к запуску MapBootstrap.");
        }
    }
}
