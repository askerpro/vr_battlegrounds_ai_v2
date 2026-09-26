using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Брошенное оружие и магазины остаются на полу карты (PHY-01).
    ///
    /// <para>
    /// Предмет, прошедший сквозь пол, падает вечно: rigidbody не засыпает, и
    /// <c>UxrGrabbableObject.RegularPhysicsSyncCoroutine</c> у отпустившего игрока без конца
    /// рассылает <c>UpdateRigidbody</c> по сети. Тест ловит три найденные причины:
    /// оружие без коллайдеров (<c>Gun_real</c>), <c>Discrete</c> против тонкого пола при броске,
    /// и вставленный магазин внутри выпуклого коллайдера корпуса.
    /// </para>
    ///
    /// <para>
    /// Физика считается в preview-сцене карты: своя <see cref="PhysicsScene" />, реальная
    /// геометрия, игровой цикл не нужен. Магазин в якоре держится kinematic — как у UltimateXR.
    /// </para>
    /// </summary>
    public class WeaponDropPhysicsTests
    {
        private static readonly string[] WeaponRoots = { "Assets/Prefabs/Weapons" };
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/Lobby.unity",
            "Assets/Scenes/Maps/TestMap1.unity",
            "Assets/Scenes/Maps/TestMap2.unity"
        };

        // Бросок сверху вниз и под углом: 25 м/с — с запасом выше реального броска рукой.
        private static readonly Vector3[] Velocities =
        {
            Vector3.zero,
            new Vector3(0f, -10f, 0f),
            new Vector3(4f, -25f, 2f)
        };

        private static readonly Vector3 DropPoint = new Vector3(0f, 1.5f, 0f);
        private const float  SimulatedSeconds = 4f;
        private const float  MinRestY         = -0.3f;

        [Test]
        public void DroppedWeaponsAndMagazines_StayOnFloor()
        {
            var failures = new List<string>();
            int checks   = 0;

            foreach (var scenePath in Scenes)
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);

                try
                {
                    PhysicsScene physics = scene.GetPhysicsScene();

                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", WeaponRoots))
                    {
                        var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                        var prefab     = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                        var grabbable = prefab.GetComponent<UxrGrabbableObject>();
                        if (grabbable == null || prefab.GetComponent<Rigidbody>() == null) continue;
                        if (!grabbable.RigidBodyDynamicOnRelease) continue;

                        foreach (var velocity in Velocities)
                        {
                            checks++;
                            float y = Drop(prefab, scene, physics, velocity);

                            if (y < MinRestY)
                                failures.Add($"{scenePath} / {prefab.name} v={velocity}: y={y:F1}");
                        }
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного префаба оружия — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Предметы провалились сквозь пол:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// У каждого динамического предмета оружия есть хотя бы один не-trigger коллайдер на его
        /// собственном <see cref="Rigidbody" /> (правило из CLAUDE.md). Коллайдеры вложенных
        /// тел — магазина в якоре — не в счёт: они принадлежат магазину, не корпусу.
        /// </summary>
        [Test]
        public void DynamicWeaponPrefabs_HaveSolidCollider()
        {
            var missing = new List<string>();
            int checks  = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", WeaponRoots))
            {
                var path      = AssetDatabase.GUIDToAssetPath(guid);
                var prefab    = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var grabbable = prefab.GetComponent<UxrGrabbableObject>();
                var body      = prefab.GetComponent<Rigidbody>();

                if (grabbable == null || body == null || !grabbable.RigidBodyDynamicOnRelease) continue;
                checks++;

                if (!HasSolidCollider(prefab, body)) missing.Add(path);
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного префаба оружия — тест ничего не проверил.");
            Assert.IsEmpty(missing, "Нет ни одного не-trigger коллайдера на Rigidbody предмета:\n" + string.Join("\n", missing));
        }

        /// <summary>
        /// Есть ли у тела свой твёрдый коллайдер. <c>attachedRigidbody</c> у ассета префаба не
        /// заполнен (объект не в сцене), поэтому владелец ищется как ближайший Rigidbody вверх.
        /// </summary>
        public static bool HasSolidCollider(GameObject root, Rigidbody body)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || !collider.enabled || !collider.gameObject.activeSelf) continue;
                if (collider.GetComponentInParent<Rigidbody>(true) == body) return true;
            }

            return false;
        }

        /// <summary>
        /// Пол держит и тело с <c>Discrete</c>: плоский MeshCollider нулевой толщины такое тело
        /// на броске проскакивает за один шаг физики. Страховка для любого будущего предмета,
        /// которому забудут выставить <c>Continuous</c>.
        /// </summary>
        [Test]
        public void Floor_StopsFastDiscreteBody()
        {
            var failures = new List<string>();

            foreach (var scenePath in Scenes)
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);

                try
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    SceneManager.MoveGameObjectToScene(cube, scene);
                    cube.transform.localScale = Vector3.one * 0.1f;

                    var body = cube.AddComponent<Rigidbody>();
                    body.collisionDetectionMode = CollisionDetectionMode.Discrete;

                    foreach (var velocity in Velocities)
                    {
                        cube.transform.SetPositionAndRotation(DropPoint, Quaternion.identity);
                        body.linearVelocity  = velocity;
                        body.angularVelocity = Vector3.zero;

                        PhysicsScene physics = scene.GetPhysicsScene();
                        int steps = Mathf.CeilToInt(SimulatedSeconds / Time.fixedDeltaTime);
                        for (int i = 0; i < steps; i++) physics.Simulate(Time.fixedDeltaTime);

                        float y = cube.transform.position.y;
                        if (y < MinRestY) failures.Add($"{scenePath} v={velocity}: y={y:F1}");
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.IsEmpty(failures, "Discrete-тело прошло сквозь пол:\n" + string.Join("\n", failures));
        }

        private static float Drop(GameObject prefab, Scene scene, PhysicsScene physics, Vector3 velocity)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            try
            {
                instance.transform.SetPositionAndRotation(DropPoint, Quaternion.Euler(20f, 30f, 10f));

                var body = instance.GetComponent<Rigidbody>();

                // Вложенные тела — предметы в якорях: у UltimateXR они kinematic, пока лежат там.
                foreach (var nested in instance.GetComponentsInChildren<Rigidbody>(true))
                    if (nested != body) nested.isKinematic = true;

                foreach (var ignore in instance.GetComponentsInChildren<AnchoredItemCollisionIgnore>(true))
                    ignore.SyncIgnoredCollisions();

                body.isKinematic    = false;
                body.linearVelocity = velocity;

                int steps = Mathf.CeilToInt(SimulatedSeconds / Time.fixedDeltaTime);
                for (int i = 0; i < steps; i++) physics.Simulate(Time.fixedDeltaTime);

                return instance.transform.position.y;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
