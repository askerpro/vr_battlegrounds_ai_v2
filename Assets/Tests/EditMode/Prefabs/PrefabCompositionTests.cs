using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;
using UltimateXR.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Состав префабов как проверяемое утверждение.
    ///
    /// <para>
    /// Зачем эти тесты вообще существуют. Ни один ярус проверок в проекте не смотрел
    /// на то, <b>где размещены</b> компоненты: ярусы A/A+/B создают объекты сами,
    /// ярус C гоняет уже собранный плеер и молчит про сцены. В эту дыру провалились
    /// сразу две находки:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><b>ARCH-01</b> — <see cref="SessionRecoveryManager" /> не размещён ни в одной
    ///       сцене и ни в одном префабе, поэтому <c>Instance</c> всегда <c>null</c>,
    ///       а все вызовы обёрнуты в <c>if (Instance != null)</c> и молча пропускаются.
    ///       Задачи T-04 и T-20 чинили код, который никогда не исполнялся, и их
    ///       EditMode-тесты этого не видели: они создают менеджер сами.</item>
    /// <item><b>VR-07</b> — у одного из шести аватарных префабов нет пивота камеры
    ///       прямым потомком корня и нет <c>NetworkTransform</c> на камере, из-за чего
    ///       смещение высоты не реплицируется.</item>
    /// </list>
    ///
    /// <para>
    /// Оба дефекта одного класса: «компонент есть в коде, но его нет там, где его ищут».
    /// Тесты ниже закрывают именно класс, а не два вхождения — списки составов ведутся
    /// руками и сами являются утверждением о том, как проект должен быть собран.
    /// </para>
    /// </summary>
    public class PrefabCompositionTests
    {
        // ══════════════════════════════════════════════════════════════════
        //  Постоянные менеджеры (ARCH-01)
        // ══════════════════════════════════════════════════════════════════

        private const string ManagersPrefabPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";

        /// <summary>
        /// Постоянные менеджеры, которые обязаны лежать на префабе <c>--- MANAGERS ---</c>.
        ///
        /// <para>
        /// Список повторяет объявленный состав <c>ManagerBootstrap.PersistentRoster</c>,
        /// но проверяет другое: тот проверяет <c>Instance</c> в рантайме и терпит
        /// отсутствующий менеджер как предупреждение, а здесь отсутствие — отказ.
        /// Рантайм-предупреждение никто не читает, поэтому ARCH-01 и прожила
        /// незамеченной, будучи прямо описанной в комментарии к слоту.
        /// </para>
        /// </summary>
        private static readonly Type[] PersistentManagers =
        {
            typeof(PersistentRoot),
            typeof(PlayersManager),
            typeof(SessionRecoveryManager),
            typeof(global::VrBattlegrounds.Player.Avatars.AvatarManager),
            typeof(MapLoader),
            typeof(global::VrBattlegrounds.PhysicalSpaceUtils.PhysicalSpaceSyncManager),
            typeof(global::VrBattlegrounds.Network.GameNetworkManager)
        };

        [Test]
        public void Все_постоянные_менеджеры_лежат_на_префабе_MANAGERS()
        {
            GameObject prefab = LoadPrefab(ManagersPrefabPath);
            List<string> missing = new List<string>();

            foreach (Type manager in PersistentManagers)
            {
                if (prefab.GetComponentInChildren(manager, true) == null)
                    missing.Add(manager.Name);
            }

            Assert.IsEmpty(missing,
                $"На префабе '{ManagersPrefabPath}' нет менеджеров: {string.Join(", ", missing)}. " +
                "Синглтон, которого нет ни в одном префабе и ни в одной сцене, никогда не " +
                "поднимется, и весь его код мёртв — при этом он продолжает компилироваться " +
                "и покрываться юнит-тестами, которые создают его сами (ARCH-01).");
        }

        /// <summary>
        /// Менеджер, лежащий на префабе, объявлен в <c>ManagerBootstrap.PersistentRoster</c>
        /// обязательным. Иначе объявление врёт: после ARCH-01 менеджер восстановления был размещён,
        /// а слот остался «необязательный, нигде не размещён», и лог каждого запуска это повторял.
        /// Состав приватный — читается рефлексией.
        /// </summary>
        [Test]
        public void Менеджер_на_префабе_объявлен_обязательным()
        {
            GameObject prefab = LoadPrefab(ManagersPrefabPath);
            var roster = (Array)typeof(ManagerBootstrap)
                .GetField("PersistentRoster", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            var wrong = new List<string>();

            foreach (object slot in roster)
            {
                Type slotType = slot.GetType();
                var type = (Type)slotType.GetField("Type").GetValue(slot);
                var required = (bool)slotType.GetField("Required").GetValue(slot);

                if (prefab.GetComponentInChildren(type, true) != null && !required)
                    wrong.Add(type.Name);
            }

            Assert.IsEmpty(wrong,
                $"Лежат на '{ManagersPrefabPath}', но объявлены необязательными: {string.Join(", ", wrong)}. " +
                "Поправь слот в ManagerBootstrap.PersistentRoster: Required = true и место жизни.");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Аватарные префабы (VR-07)
        // ══════════════════════════════════════════════════════════════════


        /// <summary>
        /// Компоненты, без которых аватар не работает в сети. Список намеренно короткий:
        /// сюда попадает только то, отсутствие чего ломает игру, а не косметические
        /// различия между префабами (те — в аудите, не в утверждениях).
        /// </summary>
        private static readonly Type[] RequiredOnAvatarRoot =
        {
            typeof(NetworkIdentity),
            typeof(NetworkTransformBase),
            typeof(PlayerController),
            typeof(UxrAvatar),
            typeof(UltimateXR.Mechanics.Weapons.UxrActor),
            typeof(UltimateXR.Networking.Integrations.Net.Mirror.UxrMirrorAvatar)
        };

        /// <summary>
        /// Аватарные префабы — зарегистрированные (<see cref="RegisteredAvatars"/>): то, что игра
        /// может выдать игроку. Раньше здесь был поиск по папке <c>Assets/Prefabs/Player</c>, и заброшенный
        /// незарегистрированный скин валил тест, хотя в игру не попадал.
        /// </summary>
        private static IEnumerable<GameObject> AvatarPrefabs()
        {
            return RegisteredAvatars.Prefabs().OrderBy(AssetDatabase.GetAssetPath);
        }

        [Test]
        public void Аватарных_префабов_найдено_столько_же_сколько_ожидается()
        {
            List<GameObject> avatars = AvatarPrefabs().ToList();

            Assert.GreaterOrEqual(avatars.Count, 1,
                $"В реестре '{RegisteredAvatars.RegistryPath}' нет ни одного аватара — проверять нечего.");
            Assert.That(avatars.All(a => a.GetComponent<PlayerController>() != null),
                "В реестре префаб без PlayerController на корне: " +
                string.Join(", ", avatars.Where(a => a.GetComponent<PlayerController>() == null).Select(a => a.name)));
        }

        [Test]
        public void У_каждого_аватара_на_корне_есть_обязательные_компоненты()
        {
            List<string> problems = new List<string>();

            foreach (GameObject avatar in AvatarPrefabs())
            {
                foreach (Type required in RequiredOnAvatarRoot.Distinct())
                {
                    if (avatar.GetComponent(required) == null)
                        problems.Add($"{avatar.name}: нет {required.Name}");
                }
            }

            Assert.IsEmpty(problems,
                "Состав корневых компонентов аватаров разошёлся:\n  " +
                string.Join("\n  ", problems));
        }

        /// <summary>
        /// Контракт UltimateXR: пивот камеры («Camera Controller») обязан быть
        /// <b>прямым</b> потомком корня аватара, а камера — его потомком.
        ///
        /// <para>
        /// Почему это утверждение, а не мелочь. <c>UxrAvatar.InitializeCamera</c> ищет
        /// пивот подъёмом от камеры вверх, пока родитель не окажется корнем аватара.
        /// Если пивот лежит глубже, в <c>UxrAvatar.CameraController</c> попадёт
        /// промежуточный объект — например, весь риг с телом и костями, — и
        /// <c>PhysicalSpaceSyncManager.ApplyHeightDelta</c> начнёт двигать не камеру,
        /// а всего персонажа. Предупреждение SDK при этом <b>не печатается</b>: условие
        /// выхода из цикла оказывается выполненным. Ровно так проявляется VR-07.
        /// </para>
        /// </summary>
        [Test]
        public void У_каждого_аватара_пивот_камеры_прямой_потомок_корня()
        {
            List<string> problems = new List<string>();

            foreach (GameObject avatar in AvatarPrefabs())
            {
                Camera camera = avatar.GetComponentInChildren<Camera>(true);

                if (camera == null)
                {
                    problems.Add($"{avatar.name}: камеры нет вообще");
                    continue;
                }

                Transform pivot = camera.transform.parent;

                if (pivot == null)
                {
                    problems.Add($"{avatar.name}: камера '{camera.name}' лежит на самом корне, пивота нет");
                    continue;
                }

                if (pivot.parent != avatar.transform)
                {
                    problems.Add($"{avatar.name}: пивот камеры '{pivot.name}' — потомок " +
                                 $"'{pivot.parent?.name ?? "(нет родителя)"}', а не корня аватара. " +
                                 $"UxrAvatar.CameraController получит '{ResolveCameraController(avatar.transform, camera.transform).name}'");
                }
            }

            Assert.IsEmpty(problems,
                "Пивот камеры собран не по общему шаблону:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Трекинг головы реплицируется только через <c>NetworkTransform</c> на камере:
        /// корень аватара двигается локомоцией, а поворот и наклон головы внутри игровой
        /// зоны — это <c>localPosition</c> камеры относительно её пивота. Аватар без
        /// второго <c>NetworkTransform</c> не передаёт движение головы вообще: на чужих
        /// экранах его голова стоит неподвижно, куда бы игрок ни смотрел (VR-07).
        ///
        /// <para>
        /// Смещение пола к этому каналу отношения не имеет: с VR-08 оно едет
        /// <c>SyncVar</c>-ом <c>PlayerSession.CalibrationHeightOffset</c> и применяется
        /// на каждой машине локально. Раньше эта проверка объяснялась именно высотой —
        /// объяснение было неверным, отказ настоящий.
        /// </para>
        /// </summary>
        [Test]
        public void У_каждого_аватара_камера_несёт_NetworkTransform()
        {
            List<string> problems = new List<string>();

            foreach (GameObject avatar in AvatarPrefabs())
            {
                Camera camera = avatar.GetComponentInChildren<Camera>(true);
                if (camera == null) continue; // отдельная проверка выше

                Transform pivot = camera.transform.parent;

                bool onCamera = camera.GetComponent<NetworkTransformBase>() != null;
                bool onPivot = pivot != null && pivot.GetComponent<NetworkTransformBase>() != null;

                if (!onCamera && !onPivot)
                {
                    int total = avatar.GetComponentsInChildren<NetworkTransformBase>(true).Length;
                    problems.Add($"{avatar.name}: NetworkTransform нет ни на камере '{camera.name}', " +
                                 $"ни на её пивоте '{pivot?.name ?? "(нет)"}'. Всего NetworkTransform " +
                                 $"в префабе: {total}");
                }
            }

            Assert.IsEmpty(problems,
                "Смещение высоты не реплицируется:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Remote-аватар строит руки из поз кистей, которые приходят <c>NetworkTransform</c>-ом:
        /// у каждой кости кисти (<c>UxrAvatar.GetHandBone</c>) должен быть свой, чьим
        /// <c>target</c> она служит, и в <b>мировых</b> координатах.
        ///
        /// <para>
        /// <b>Почему не Local.</b> IK руки (<c>UxrArmIKSolver</c>, конец <c>SolveIKPass</c>)
        /// ставит предплечье так, чтобы дотянуться до кисти, и возвращает кисти её мировую позу —
        /// после IK локальная позиция кисти относительно предплечья всегда равна длине кости.
        /// <c>Local</c> передаёт по сети эту константу; на принимающей стороне кисть встаёт туда,
        /// где уже стоит предплечье, IK считает цель достигнутой — рука замирает, шевелятся только
        /// пальцы и поворот запястья. Так было у всех аватаров в сетевой игре (на хосте незаметно:
        /// сети нет). Нашёл стресс-тест. Сам UltimateXR (<c>UxrMirrorNetwork.SetupAvatar</c>)
        /// вешает эти компоненты с <c>worldSpace = true</c>.
        /// </para>
        ///
        /// <para>
        /// У <c>Heavy_Soldier_Base_Avatar</c> компонентов на кистях не было вовсе.
        /// </para>
        /// </summary>
        [Test]
        public void У_каждого_аватара_кисти_несут_NetworkTransform()
        {
            List<string> problems = new List<string>();

            foreach (GameObject avatar in AvatarPrefabs())
            {
                UxrAvatar uxrAvatar = avatar.GetComponent<UxrAvatar>();
                if (uxrAvatar == null) continue; // отдельная проверка выше

                NetworkTransformBase[] transforms = avatar.GetComponentsInChildren<NetworkTransformBase>(true);

                foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
                {
                    Transform hand = uxrAvatar.GetHandBone(side);
                    if (hand == null)
                    {
                        problems.Add($"{avatar.name}: в риге нет кости кисти ({side})");
                        continue;
                    }

                    NetworkTransformBase synced = null;
                    foreach (NetworkTransformBase nt in transforms)
                    {
                        Transform target = nt.target != null ? nt.target : nt.transform;
                        if (target == hand) synced = nt;
                    }

                    if (synced == null)
                    {
                        problems.Add($"{avatar.name}: у кисти '{hand.name}' ({side}) нет NetworkTransform. " +
                                     $"Всего NetworkTransform в префабе: {transforms.Length}");
                    }
                    else if (synced.coordinateSpace != CoordinateSpace.World)
                    {
                        problems.Add($"{avatar.name}: NetworkTransform кисти '{hand.name}' ({side}) в пространстве " +
                                     $"{synced.coordinateSpace} — после IK локальная поза кисти постоянна, рука замрёт");
                    }
                }
            }

            Assert.IsEmpty(problems,
                "Руки аватара не реплицируются — на чужих шлемах они стоят на месте:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// <c>UxrDummyControllerInput</c> обязан лежать на корне аватара в самом префабе,
        /// с сериализованным <c>UniqueId</c> (NET-24).
        ///
        /// <para>
        /// Когда у аватара нет активного контроллера (редактор без шлема, удалённый аватар),
        /// <c>UxrAvatar.ControllerInput</c> добавляет dummy через <c>GetOrAddComponent</c>.
        /// Созданный в рантайме компонент получает id, свой на каждой машине, а
        /// <c>SetAvatarRenderMode</c> передаёт по сети список включённых контроллеров
        /// ссылками по id — принимающая сторона такой ссылки не находит и отвергает
        /// событие целиком (<c>UxrComponentNotFoundException</c>). Dummy из префаба
        /// получает общий id через <c>CombineUniqueId(netId)</c>, как и остальные компоненты.
        /// </para>
        /// </summary>
        [Test]
        public void У_каждого_аватара_на_корне_лежит_dummy_ввод_с_id()
        {
            List<string> problems = new List<string>();

            foreach (GameObject avatar in AvatarPrefabs())
            {
                UltimateXR.Devices.Integrations.UxrDummyControllerInput dummy =
                    avatar.GetComponent<UltimateXR.Devices.Integrations.UxrDummyControllerInput>();

                if (dummy == null)
                    problems.Add($"{avatar.name}: нет UxrDummyControllerInput на корне");
                else if (dummy.UniqueId == Guid.Empty)
                    problems.Add($"{avatar.name}: у UxrDummyControllerInput пустой UniqueId");
            }

            Assert.IsEmpty(problems,
                "SDK создаст dummy-ввод в рантайме с несовпадающим id, и SetAvatarRenderMode " +
                "будет отвергаться другой стороной (NET-24):\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Процедурные ноги: у каждого аватара ровно один <c>LegsAnimator</c> и рядом с ним один
        /// <c>LegsAnimatorUxrBridge</c> — на объекте рига с humanoid-<c>Animator</c>, внутри аватара, но
        /// не на его корне. Ноги считает каждая машина сама (сеть их не синхронизирует), поэтому
        /// мост ищет свой <c>UxrAvatar</c> вверх по иерархии и его <c>Dummy Forward</c>; на корне
        /// плагин взял бы корень аватара за риг, а два плагина тянули бы одни кости каждый к своему
        /// полу. Подробные настройки — <c>AvatarLoadoutTests.Legs_Animator_настроен_на_своих_костях</c>.
        /// Добавлено вместе с ногами киборга: до них у киборга не было ни рига, ни ног.
        /// </summary>
        [Test]
        public void У_каждого_аватара_один_Legs_Animator_на_humanoid_риге()
        {
            var problems = new List<string>();

            foreach (GameObject avatar in AvatarPrefabs())
            {
                // Сборки плагина и моста (Assembly-CSharp) тестам недоступны — по имени типа.
                Component[] legs = avatar.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "LegsAnimator").ToArray();
                Component[] bridges = avatar.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "LegsAnimatorUxrBridge").ToArray();

                if (legs.Length != 1 || bridges.Length != 1)
                {
                    problems.Add($"{avatar.name}: LegsAnimator {legs.Length}, мостов {bridges.Length} — нужно по одному");
                    continue;
                }

                GameObject host = legs[0].gameObject;
                Animator animator = host.GetComponent<Animator>();
                if (bridges[0].gameObject != host)
                    problems.Add($"{avatar.name}: мост на '{bridges[0].name}', плагин на '{host.name}' — должны быть на одном объекте");
                if (host == avatar)
                    problems.Add($"{avatar.name}: Legs Animator на корне аватара, а не на риге");
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                    problems.Add($"{avatar.name}: на '{host.name}' нет humanoid-Animator — у плагина нет рига");
            }

            Assert.IsEmpty(problems, "Процедурные ноги аватаров собраны не там:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// <c>NetworkBehaviour</c> без <c>NetworkIdentity</c> на себе или у родителя Mirror
        /// не заспавнит и не синхронизирует; в редакторе это красная ошибка из
        /// <c>OnValidate</c> при каждой загрузке префаба. Так было с заброшенной заготовкой
        /// <c>Arsenal/ArsenalWall.prefab</c> (NET-25).
        /// </summary>
        [Test]
        public void У_каждого_NetworkBehaviour_в_префабах_есть_NetworkIdentity()
        {
            List<string> problems = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/ThirdParty/")) continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (NetworkBehaviour behaviour in prefab.GetComponentsInChildren<NetworkBehaviour>(true))
                {
                    if (behaviour.GetComponentInParent<NetworkIdentity>(true) == null)
                        problems.Add($"{path} / {behaviour.name}: {behaviour.GetType().Name}");
                }
            }

            Assert.IsEmpty(problems,
                "NetworkBehaviour без NetworkIdentity на себе или у родителя:\n  " + string.Join("\n  ", problems));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static GameObject LoadPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"Префаб не найден: {path}");
            return prefab;
        }

        /// <summary>
        /// Повторяет подъём из <c>UxrAvatar.InitializeCamera</c>: от камеры вверх,
        /// пока родитель не станет корнем аватара. Нужен, чтобы в сообщении об ошибке
        /// стояло не «что-то не так», а конкретный объект, который SDK примет
        /// за пивот камеры.
        /// </summary>
        private static Transform ResolveCameraController(Transform avatarRoot, Transform camera)
        {
            Transform current = camera.parent;

            while (current != null && current.parent != avatarRoot)
                current = current.parent;

            return current != null ? current : camera;
        }
    }
}
