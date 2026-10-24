using System;
using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Tests.Prefabs;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Жетон готовности — сетевой предмет стены, а не часть панели.
    ///
    /// <para>
    /// Панель (<see cref="DogTagController"/>) держит крючок и префаб жетона; экземпляр выдаёт сервер
    /// (<c>ArsenalWallController.ServerUpdateDogTag</c>) и сам вешает на крючок до спавна. Поэтому:
    /// в панели и в сценах жетона нет (иначе вернутся per-instance id в каждой сцене), а префаб годится
    /// для сетевой выдачи — <c>NetworkIdentity</c>, метка <see cref="DogTagItem"/>, без стартового якоря
    /// и Auto Anchor (на крючок его ставит выдача, а лишний якорь разошёлся бы между машинами).
    /// </para>
    ///
    /// <para>
    /// Висит жетон кинематическим, после отпускания он динамический — правила PHY-01: твёрдый коллайдер,
    /// не-<c>Discrete</c> и <see cref="OutOfWorldGuard" />.
    /// </para>
    /// </summary>
    public class DogTagSetupTests
    {
        private static readonly string[] PrefabRoots = { "Assets/Prefabs" };
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/Lobby.unity",
            "Assets/Scenes/Maps/Game/MilitaryBase.unity",
            "Assets/Scenes/Maps/Game/Warehouse.unity",
            "Assets/Scenes/Maps/Dev/BlockStand.unity",
            "Assets/Scenes/Maps/Game/ServiceYard.unity",
            "Assets/Scenes/Maps/Dev/ReferenceMap04.unity",
            "Assets/Scenes/Stands/Bots/BotCombatStand.unity",
            "Assets/Scenes/Stands/Arsenal/CommonArsenalReview.unity"
        };

        [Test]
        public void DogTagPanelsInPrefabs_HaveAnchorAndNetworkTagPrefab()
        {
            var failures = new List<string>();
            int checks   = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", PrefabRoots))
            {
                var path   = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                foreach (var controller in prefab.GetComponentsInChildren<DogTagController>(true))
                {
                    checks++;
                    ValidatePanel(controller, path, failures);
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного DogTagController в префабах — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Жетон настроен неверно:\n" + string.Join("\n", failures));
        }

        [Test]
        public void DogTagPanelsInScenes_HaveNoBakedTag()
        {
            var failures = new List<string>();
            int checks   = 0;

            foreach (var scenePath in Scenes)
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);

                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                    foreach (var controller in root.GetComponentsInChildren<DogTagController>(true))
                    {
                        checks++;
                        ValidatePanel(controller, scenePath, failures);
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одной панели жетона в сценах — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Жетон настроен неверно:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// У крючка каждой стены в сцене свой <c>UniqueId</c>: по нему UltimateXR адресует размещение
        /// жетона по сети, и два крючка с одним id сливаются в один. Сам жетон сюда не входит — его id
        /// выравнивается по <c>netId</c> при выдаче.
        ///
        /// <para>
        /// Id экземпляров живут переопределениями в сценах. Перестройка префаба стены (например, вынос
        /// панели во вложенный префаб) меняет fileID, переопределения сиротеют, и все экземпляры
        /// наследуют один id.
        /// </para>
        ///
        /// <para>
        /// Читается <b>сохранённое</b> значение: автогенерация UltimateXR на время теста
        /// выключена. Иначе <c>OnValidate</c> при открытии сцены перевыдаёт дубли в памяти,
        /// тест зеленеет, а в сборку уходят файлы с дублями — там <c>OnValidate</c> не работает.
        /// </para>
        /// </summary>
        [Test]
        public void DogTagUniqueIds_AreDistinctInEachScene()
        {
            var failures = new List<string>();
            int checks   = 0;

            string prefsKey = UltimateXR.Core.UxrConstants.Editor.AutomaticIdGenerationPrefs;
            bool   autoIds  = EditorPrefs.GetBool(prefsKey, true);
            EditorPrefs.SetBool(prefsKey, false);

            try
            {
                foreach (var scenePath in Scenes)
                {
                    Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);

                    try
                    {
                        var owners = new Dictionary<Guid, string>();

                        foreach (var root in scene.GetRootGameObjects())
                        foreach (var controller in root.GetComponentsInChildren<DogTagController>(true))
                        foreach (var component in controller.GetComponentsInChildren<UltimateXR.Core.Components.UxrComponent>(true))
                        {
                            checks++;
                            string who = $"{scenePath} / {component.transform.parent.name}/{component.name} ({component.GetType().Name})";

                            if (component.UniqueId == Guid.Empty)
                                failures.Add($"{who}: пустой UniqueId");
                            else if (owners.TryGetValue(component.UniqueId, out var other))
                                failures.Add($"{who}: UniqueId {component.UniqueId} совпадает с {other}");
                            else
                                owners.Add(component.UniqueId, who);
                        }
                    }
                    finally
                    {
                        EditorSceneManager.ClosePreviewScene(scene);
                    }
                }
            }
            finally
            {
                EditorPrefs.SetBool(prefsKey, autoIds);
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного компонента панели жетона в сценах — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Крючки жетонов делят UniqueId — сетевое размещение спутает их:\n" + string.Join("\n", failures));
        }

        private static void ValidatePanel(DogTagController controller, string where, List<string> failures)
        {
            var so     = new SerializedObject(controller);
            var anchor = so.FindProperty("_tagAnchor").objectReferenceValue as UxrGrabbableObjectAnchor;
            var prefab = so.FindProperty("_tagPrefab").objectReferenceValue as GameObject;
            string at  = $"{where} / {controller.name}";

            if (anchor == null) failures.Add($"{at}: _tagAnchor не задан");
            else if (!anchor.transform.IsChildOf(controller.transform)) failures.Add($"{at}: _tagAnchor не внутри панели");

            var baked = controller.GetComponentInChildren<UxrGrabbableObject>(true);
            if (baked != null)
                failures.Add($"{at}: в панели вшит хватаемый объект '{baked.name}' — жетон выдаёт стена, в панели и сценах его быть не должно");

            if (prefab == null) { failures.Add($"{at}: _tagPrefab не задан"); return; }
            ValidateTagPrefab(prefab, $"{at} → {AssetDatabase.GetAssetPath(prefab)}", failures);
        }

        private static void ValidateTagPrefab(GameObject prefab, string at, List<string> failures)
        {
            var tag  = prefab.GetComponent<UxrGrabbableObject>();
            var body = prefab.GetComponent<Rigidbody>();

            if (prefab.GetComponent<DogTagItem>() == null)       failures.Add($"{at}: на корне нет DogTagItem");
            if (prefab.GetComponent<NetworkIdentity>() == null)  failures.Add($"{at}: на корне нет NetworkIdentity — NetworkServer.Spawn невозможен");
            if (tag == null) { failures.Add($"{at}: на корне нет UxrGrabbableObject"); return; }

            if (tag.StartAnchor != null)
                failures.Add($"{at}: задан _startAnchor '{tag.StartAnchor.name}' — на крючок жетон ставит выдача");
            if (tag.AutoCreateStartAnchor)
                failures.Add($"{at}: включён Auto Anchor — лишний якорь разойдётся между машинами");
            if (body == null)
            {
                failures.Add($"{at}: у жетона нет Rigidbody");
                return;
            }
            if (tag.RigidBodySource != body)
                failures.Add($"{at}: _rigidBodySource не указывает на Rigidbody жетона");
            if (!body.isKinematic)
                failures.Add($"{at}: Rigidbody жетона не kinematic в ассете — до привязки к крючку он уже под гравитацией");
            if (body.collisionDetectionMode == CollisionDetectionMode.Discrete)
                failures.Add($"{at}: Collision Detection = Discrete (PHY-01)");
            if (!WeaponDropPhysicsTests.HasSolidCollider(prefab, body))
                failures.Add($"{at}: у жетона нет не-trigger коллайдера (PHY-01)");
            if (prefab.GetComponent<OutOfWorldGuard>() == null)
                failures.Add($"{at}: нет OutOfWorldGuard (PHY-01)");
        }
    }
}
