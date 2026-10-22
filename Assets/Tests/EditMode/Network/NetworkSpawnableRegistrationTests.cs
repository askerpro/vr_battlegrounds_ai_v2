using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Регистрация сетевых префабов как проверяемое утверждение.
    ///
    /// <para>
    /// Mirror спавнит объект у клиента только если его префаб заранее известен клиенту —
    /// то есть лежит в <c>NetworkManager.spawnPrefabs</c>. Незарегистрированный префаб
    /// сервер спавнит у себя молча и успешно, а клиент отвечает строчкой в лог
    /// («Failed to spawn server object, did you forget to add it to the NetworkManager?»)
    /// и не создаёт ничего. Отказ односторонний и тихий: на хосте он невидим вовсе,
    /// потому что там сервер и клиент — один процесс.
    /// </para>
    ///
    /// <para>
    /// Список ведётся руками. Редакторные утилиты <c>WeaponRegistryEditor</c> и
    /// <c>AvatarRegistryEditor</c> умеют его пополнять, но только по нажатию кнопки —
    /// то есть между «добавили оружие в реестр» и «оружие видно клиентам» стоит шаг,
    /// о котором нечему напомнить. Этот тест и есть напоминание.
    /// </para>
    ///
    /// <para>
    /// Проверяется префаб, а не рантайм: <c>spawnPrefabs</c> сериализован на компоненте
    /// <c>GameNetworkManager</c>, и именно в этом виде уезжает в билд под Quest.
    /// </para>
    /// </summary>
    public class NetworkSpawnableRegistrationTests
    {
        private const string ManagersPrefabPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";

        /// <summary>
        /// Оружие из <see cref="WeaponRegistry"/> раздаёт по сети стена арсенала:
        /// <c>ArsenalWallController.ReplenishWeaponsNetwork</c> делает
        /// <c>Instantiate</c> + <c>NetworkServer.Spawn</c>. Значит каждый
        /// <c>WeaponPrefab</c> обязан быть зарегистрирован.
        /// </summary>
        [Test]
        public void Оружие_из_реестра_зарегистрировано_в_spawnPrefabs()
        {
            NetworkManager manager = LoadNetworkManager();
            WeaponRegistry registry = LoadWeaponRegistry();

            HashSet<GameObject> registered = Registered(manager);
            List<string> missing = new List<string>();

            foreach (WeaponInfo weapon in registry.Weapons)
            {
                if (weapon == null) continue;

                if (weapon.WeaponPrefab == null)
                {
                    missing.Add($"{weapon.name}: в реестре не задан WeaponPrefab");
                    continue;
                }

                if (!registered.Contains(weapon.WeaponPrefab))
                    missing.Add($"{weapon.name} -> префаб '{weapon.WeaponPrefab.name}'");
            }

            Assert.IsEmpty(missing,
                $"Эти префабы оружия стена арсенала спавнит через NetworkServer.Spawn, " +
                $"но в spawnPrefabs на '{ManagersPrefabPath}' их нет — у клиентов оружие " +
                "не появится вовсе, стойки будут пустыми, и взять с них будет нечего " +
                "(на хосте не воспроизводится). Лечится кнопкой в инспекторе WeaponRegistry.\n  " +
                string.Join("\n  ", missing));
        }

        /// <summary>
        /// Магазины идут тем же путём: оружие спавнится вместе со своим магазином,
        /// и незарегистрированный магазин даёт у клиента ту же тихую пропажу.
        /// </summary>
        [Test]
        public void Магазины_из_реестра_зарегистрированы_в_spawnPrefabs()
        {
            NetworkManager manager = LoadNetworkManager();
            WeaponRegistry registry = LoadWeaponRegistry();

            HashSet<GameObject> registered = Registered(manager);
            List<string> missing = new List<string>();

            foreach (WeaponInfo weapon in registry.Weapons)
            {
                if (weapon == null || weapon.MagazinePrefab == null) continue;

                if (!registered.Contains(weapon.MagazinePrefab))
                    missing.Add($"{weapon.name} -> магазин '{weapon.MagazinePrefab.name}'");
            }

            Assert.IsEmpty(missing,
                "Магазины из реестра не зарегистрированы в spawnPrefabs — у клиентов их не будет.\n  " +
                string.Join("\n  ", missing));
        }

        /// <summary>
        /// Жетон готовности стена тоже выдаёт по сети: <c>ArsenalWallController.ServerUpdateDogTag</c>
        /// спавнит <c>_tagPrefab</c> панели жетона. Каждый такой префаб обязан быть зарегистрирован,
        /// иначе у клиентов жетона нет, и объявить готовность нечем.
        /// </summary>
        [Test]
        public void Жетоны_панелей_арсенала_зарегистрированы_в_spawnPrefabs()
        {
            NetworkManager manager = LoadNetworkManager();
            HashSet<GameObject> registered = Registered(manager);
            List<string> missing = new List<string>();
            int checks = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (DogTagController panel in prefab.GetComponentsInChildren<DogTagController>(true))
                {
                    checks++;
                    if (panel.TagPrefab == null)
                        missing.Add($"{path}: у панели '{panel.name}' не задан _tagPrefab");
                    else if (!registered.Contains(panel.TagPrefab))
                        missing.Add($"{path}: жетон '{panel.TagPrefab.name}'");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одной панели жетона — тест ничего не проверил.");
            Assert.IsEmpty(missing,
                "Жетоны панелей не зарегистрированы в spawnPrefabs — у клиентов жетона не будет.\n  " +
                string.Join("\n  ", missing));
        }

        /// <summary>
        /// Дыры в самом списке. <c>null</c>-элемент Mirror пропускает с ошибкой при
        /// старте, дубль регистрирует поверх — и то и другое означает, что список
        /// правили руками и промахнулись.
        /// </summary>
        [Test]
        public void В_spawnPrefabs_нет_пустых_записей_и_дублей()
        {
            NetworkManager manager = LoadNetworkManager();

            int nulls = manager.spawnPrefabs.Count(p => p == null);
            List<string> duplicates = manager.spawnPrefabs
                                             .Where(p => p != null)
                                             .GroupBy(p => p)
                                             .Where(g => g.Count() > 1)
                                             .Select(g => $"{g.Key.name} x{g.Count()}")
                                             .ToList();

            Assert.Zero(nulls, $"В spawnPrefabs есть пустые записи: {nulls} шт.");
            Assert.IsEmpty(duplicates,
                "В spawnPrefabs есть дубли: " + string.Join(", ", duplicates));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static HashSet<GameObject> Registered(NetworkManager manager)
        {
            return new HashSet<GameObject>(manager.spawnPrefabs.Where(p => p != null));
        }

        private static NetworkManager LoadNetworkManager()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPrefabPath);
            Assert.IsNotNull(prefab, $"Префаб не найден: {ManagersPrefabPath}");

            NetworkManager manager = prefab.GetComponentInChildren<NetworkManager>(true);
            Assert.IsNotNull(manager, $"На префабе '{ManagersPrefabPath}' нет NetworkManager");

            return manager;
        }

        private static WeaponRegistry LoadWeaponRegistry()
        {
            WeaponRegistry registry = Resources.Load<WeaponRegistry>(nameof(WeaponRegistry));
            Assert.IsNotNull(registry,
                "Не найден Resources/WeaponRegistry.asset — реестр оружия обязан лежать в Resources");

            return registry;
        }
    }
}
