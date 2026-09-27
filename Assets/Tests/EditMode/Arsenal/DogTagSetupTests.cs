using System;
using System.Collections.Generic;
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
    /// Жетон стоит в своём якоре с первого кадра и не падает на пол при старте сцены.
    ///
    /// <para>
    /// UltimateXR ставит grabbable в якорь и делает его тело kinematic в <c>Awake</c>, только
    /// если заданы <b>оба</b> поля: <c>_startAnchor</c> и <c>_rigidBodySource</c>. Без них жетон —
    /// обычное физическое тело с гравитацией: он падает на пол, а событие якоря <c>Removed</c>
    /// не приходит никогда, и объявить готовность жетоном нельзя.
    /// </para>
    ///
    /// <para>
    /// После отпускания жетон динамический, поэтому к нему применяются правила PHY-01:
    /// твёрдый коллайдер, не-<c>Discrete</c> и <see cref="OutOfWorldGuard" />.
    /// </para>
    ///
    /// <para>
    /// Проверяются и префабы, и сцены: стены стоят на картах вложенными экземплярами, и
    /// переопределение в экземпляре сломало бы жетон мимо проверки ассета.
    /// </para>
    /// </summary>
    public class DogTagSetupTests
    {
        private static readonly string[] PrefabRoots = { "Assets/Prefabs" };
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/Lobby.unity",
            "Assets/Scenes/Maps/TestMap1.unity",
            "Assets/Scenes/Maps/TestMap2.unity"
        };

        [Test]
        public void DogTagInPrefabs_StartsPlacedAndKinematic()
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
                    Validate(controller, path, failures);
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного DogTagController в префабах — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Жетон настроен неверно:\n" + string.Join("\n", failures));
        }

        [Test]
        public void DogTagInScenes_StartsPlacedAndKinematic()
        {
            var failures = new List<string>();

            foreach (var scenePath in Scenes)
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);

                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                    foreach (var controller in root.GetComponentsInChildren<DogTagController>(true))
                        Validate(controller, scenePath, failures);
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.IsEmpty(failures, "Жетон настроен неверно:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// У якоря и жетона каждой стены в сцене свой <c>UniqueId</c>: по нему UltimateXR
        /// адресует захват по сети, и два жетона с одним id сливаются в один.
        ///
        /// <para>
        /// Id экземпляров живут переопределениями в <c>Environment.prefab</c> и сценах.
        /// Перестройка префаба стены (например, вынос панели во вложенный префаб) меняет
        /// fileID, переопределения сиротеют, и все экземпляры наследуют один id.
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

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного компонента жетона в сценах — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Жетоны делят UniqueId — сетевой захват спутает их:\n" + string.Join("\n", failures));
        }

        private static void Validate(DogTagController controller, string where, List<string> failures)
        {
            var so     = new SerializedObject(controller);
            var anchor = so.FindProperty("_tagAnchor").objectReferenceValue as UxrGrabbableObjectAnchor;
            var tag    = so.FindProperty("_tagObject").objectReferenceValue as UxrGrabbableObject;
            string at  = $"{where} / {controller.name}";

            if (anchor == null) { failures.Add($"{at}: _tagAnchor не задан"); return; }
            if (tag == null)    { failures.Add($"{at}: _tagObject не задан"); return; }

            var body = tag.GetComponent<Rigidbody>();

            if (tag.StartAnchor != anchor)
                failures.Add($"{at}: _startAnchor жетона = '{(tag.StartAnchor ? tag.StartAnchor.name : "null")}', а не '{anchor.name}' — жетон не встанет в якорь");
            if (body == null)
            {
                failures.Add($"{at}: у жетона нет Rigidbody");
                return;
            }
            if (tag.RigidBodySource != body)
                failures.Add($"{at}: _rigidBodySource не указывает на Rigidbody жетона — UltimateXR не сделает его kinematic в якоре");
            if (!body.isKinematic)
                failures.Add($"{at}: Rigidbody жетона не kinematic в ассете — до Awake UltimateXR он уже под гравитацией");
            if (body.collisionDetectionMode == CollisionDetectionMode.Discrete)
                failures.Add($"{at}: Collision Detection = Discrete (PHY-01)");
            if (!WeaponDropPhysicsTests.HasSolidCollider(tag.gameObject, body))
                failures.Add($"{at}: у жетона нет не-trigger коллайдера (PHY-01)");
            if (tag.GetComponent<OutOfWorldGuard>() == null)
                failures.Add($"{at}: нет OutOfWorldGuard (PHY-01)");
        }
    }
}
