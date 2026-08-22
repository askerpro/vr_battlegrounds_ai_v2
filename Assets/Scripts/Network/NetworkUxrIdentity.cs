using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Core.Unique;
using UltimateXR.Extensions.System;
using UltimateXR.Extensions.Unity;
using UltimateXR.Manipulation;
using UltimateXR.Networking;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Network
{
    /// <summary>
    ///     Идентичность объектов UltimateXR, созданных в рантайме и заспавненных Mirror.
    ///
    ///     <para>
    ///     <b>Зачем это отдельный класс.</b> У UltimateXR своя адресация: каждое событие
    ///     канала состояния (захват, выстрел, вставка магазина) ссылается на компоненты
    ///     по <c>UniqueId</c>. У объекта сцены этот идентификатор сериализован в сцене
    ///     и потому одинаков во всех процессах, а у объекта, созданного в рантайме, он
    ///     свой на каждой машине — SDK для этого и предусмотрел
    ///     <c>IUxrUniqueId.CombineUniqueId</c>: скомбинировать исходный идентификатор
    ///     префаба с общим для всех машин числом. Общее число здесь — <c>netId</c> Mirror.
    ///     </para>
    ///
    ///     <para>
    ///     Пока идентификаторы не выровнены, <b>ни одно</b> событие манипуляции не может
    ///     быть применено на другой машине: <c>ExecuteStateSyncEvent</c> отвергает его
    ///     с <c>UxrComponentNotFoundException</c>. Это находка NET-16, и арсенал в ней —
    ///     лишь первое место, где отказ заметили.
    ///     </para>
    ///
    ///     <para>
    ///     <b>Почему не внутри арсенала.</b> Идентичность объекта — свойство самого объекта,
    ///     а не того, кто его выдал. Если бы её выравнивал <c>ArsenalWallController</c>,
    ///     у объектов появился бы второй владелец идентичности, и следующий, кто научится
    ///     спавнить оружие, повторил бы отказ с нуля. Здесь единственная точка на обе
    ///     стороны: сервер зовёт <see cref="SpawnServerObject" />, клиент получает
    ///     обработчик спавна (<see cref="InstallClientSpawnHandlers" />).
    ///     </para>
    ///
    ///     <para>
    ///     Аватары сюда не попадают: их идентичность ведёт сам SDK — <c>UxrMirrorAvatar</c>
    ///     зовёт <c>CombineUniqueId</c> в <c>OnStartServer</c>/<c>OnStartClient</c>. Второе
    ///     выравнивание поверх первого дало бы на клиенте другой идентификатор, чем
    ///     на сервере, — ровно тот отказ, который этот класс и лечит.
    ///     </para>
    /// </summary>
    public static class NetworkUxrIdentity
    {
        /// <summary>Префабы, у которых мы подменили обработчик спавна на клиенте.</summary>
        private static readonly List<GameObject> HandledPrefabs = new List<GameObject>();

        /// <summary>
        ///     Выключенный контейнер, под которым рождаются инстансы сетевых префабов.
        ///
        ///     У ребёнка выключенного родителя <c>Awake</c> не срабатывает — это
        ///     единственный способ настроить компоненты до их пробуждения, не трогая сам
        ///     префаб: он ассет, и выключение ассета метит его грязным (VR-04).
        ///
        ///     Живёт в активной сцене и уезжает вместе с ней; пересоздаётся по требованию.
        ///     Между вызовами он всегда пуст — инстанс выходит наружу в том же кадре.
        /// </summary>
        private static Transform _dormitory;

        private static Transform Dormitory
        {
            get
            {
                if (_dormitory == null)
                {
                    GameObject root = new GameObject("UXR Network Spawn Dormitory");

                    // Не сохранять ни в сцену редактора, ни в билд: это служебный объект,
                    // а EditMode-тесты гоняют этот код в открытой сцене проекта.
                    root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                    root.SetActive(false);
                    _dormitory = root.transform;
                }

                return _dormitory;
            }
        }

        // ── Создание и спавн ──────────────────────────────────────────────

        /// <summary>
        ///     Создаёт инстанс сетевого префаба UltimateXR и возвращает его
        ///     <b>выключенным</b>: вызывающий вправе донастроить объект до того, как
        ///     отработает <c>Awake</c>, и включить его сам.
        /// </summary>
        /// <param name="prefab">Префаб</param>
        /// <returns>Выключенный инстанс без родителя или null, если префаб пуст</returns>
        public static GameObject CreateInstance(GameObject prefab)
        {
            if (prefab == null)
            {
                GameLog.Network.Warning("[NetworkUxrIdentity] Попытка создать инстанс из пустого префаба.");
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, Dormitory, false);

            // Пока объект спит под выключенным родителем, гасим то, что иначе
            // разъедется между машинами, и делаем его собственный activeSelf ложным —
            // тогда выход из контейнера его не разбудит.
            SuppressAutoAnchor(instance);
            instance.SetActive(false);
            instance.transform.SetParent(null, false);

            return instance;
        }

        /// <summary>
        ///     Спавнит уже настроенный объект и сразу выравнивает его <c>UniqueId</c>.
        ///     Единственный способ спавнить объекты UltimateXR с сервера: сырой
        ///     <c>NetworkServer.Spawn</c> оставит идентичность невыровненной.
        /// </summary>
        /// <param name="instance">Объект со <c>NetworkIdentity</c></param>
        /// <param name="owner">Кому отдать владение, или null</param>
        public static void SpawnServerObject(GameObject instance, NetworkConnectionToClient owner = null)
        {
            if (instance == null)
            {
                return;
            }

            if (owner != null)
            {
                NetworkServer.Spawn(instance, owner);
            }
            else
            {
                NetworkServer.Spawn(instance);
            }

            Align(instance);
        }

        // ── Выравнивание идентичности ─────────────────────────────────────

        /// <summary>
        ///     Выравнивает <c>UniqueId</c> объекта по его <c>netId</c>. Звать строго после
        ///     того, как <c>netId</c> назначен: до спавна он равен нулю и комбинировать
        ///     не с чем.
        /// </summary>
        public static void Align(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            NetworkIdentity identity = instance.GetComponent<NetworkIdentity>();

            // Объект вне сети выравнивать не с чем и незачем — про это уже ругнулся
            // сам Mirror, если его пытались заспавнить.
            if (identity == null)
            {
                return;
            }

            if (identity.netId == 0)
            {
                GameLog.Network.Error(
                    $"[NetworkUxrIdentity] У объекта '{instance.name}' netId равен нулю — выравнивать UniqueId не по чему. " +
                    "Значит, спавн не состоялся: все манипуляции с этим объектом останутся локальными (NET-16).");
                return;
            }

            Align(instance, identity.netId);
        }

        /// <summary>
        ///     Выравнивает <c>UniqueId</c> всех компонентов UltimateXR объекта и его детей,
        ///     комбинируя их с общим для всех машин <c>netId</c>.
        ///
        ///     Форма ключа (<c>netId.ToString().GetGuid()</c>) взята у <c>UxrMirrorAvatar</c>,
        ///     чтобы аватары и предметы шли одним правилом.
        /// </summary>
        public static void Align(GameObject instance, uint netId)
        {
            if (instance == null || netId == 0)
            {
                return;
            }

            Guid combineSource = netId.ToString().GetGuid();

            // Повторное выравнивание сдвинуло бы идентификаторы ещё раз и развело бы
            // машины сильнее, чем нетронутый префаб: комбинация не идемпотентна.
            IUxrUniqueId root = instance.GetComponent<IUxrUniqueId>();

            if (root != null && root.CombineIdSource == combineSource)
            {
                return;
            }

            instance.CombineUniqueIds(combineSource);
        }

        // ── Расхождение по «Auto Anchor» ──────────────────────────────────

        /// <summary>
        ///     Гасит у всех хватаемых компонентов объекта создание собственного
        ///     «Auto Anchor». Звать строго до активации объекта: якорь создаётся
        ///     в <c>Awake</c>.
        ///
        ///     Иначе объект рождается внутри лишнего родителя, которого на другой машине
        ///     нет, событие захвата ссылается на этот родитель, и на принимающей стороне
        ///     ссылка не разрешается. Отдельно якорь ещё и вытаскивает оружие из слота
        ///     стены арсенала.
        /// </summary>
        private static void SuppressAutoAnchor(GameObject instance)
        {
            foreach (UxrGrabbableObject grabbable in instance.GetComponentsInChildren<UxrGrabbableObject>(true))
            {
                grabbable.AutoCreateStartAnchor = false;
            }
        }

        // ── Клиентская сторона ────────────────────────────────────────────

        /// <summary>
        ///     Подменяет обработчик спавна у сетевых префабов с компонентами UltimateXR.
        ///     Звать из <c>NetworkManager.OnStartClient</c>: к этому моменту Mirror уже
        ///     зарегистрировал префабы обычным способом, и подменять надо поверх.
        ///
        ///     Штатный путь Mirror (<c>Instantiate</c> сразу активным) не оставляет ни одной
        ///     точки между созданием объекта и его <c>Awake</c> — а нам нужны обе: погасить
        ///     «Auto Anchor» <b>до</b> пробуждения и выровнять <c>UniqueId</c> по <c>netId</c>,
        ///     который приезжает в том же спавн-сообщении.
        ///
        ///     На хосте обработчики не задействуются: там клиентская половина берёт
        ///     готовый объект сервера (<c>NetworkClient.OnHostClientSpawn</c>).
        /// </summary>
        public static void InstallClientSpawnHandlers()
        {
            NetworkManager manager = NetworkManager.singleton;

            if (manager == null)
            {
                return;
            }

            UninstallClientSpawnHandlers();

            foreach (GameObject prefab in manager.spawnPrefabs)
            {
                if (prefab == null || !NeedsIdentityAlignment(prefab))
                {
                    continue;
                }

                GameObject captured = prefab;

                // Сначала снимаем штатную регистрацию: Mirror ругается ошибкой, если
                // обработчик добавляют к префабу, который уже зарегистрирован обычным путём.
                NetworkClient.UnregisterPrefab(captured);
                NetworkClient.RegisterPrefab(captured,
                                             message => SpawnClientObject(message, captured),
                                             UnspawnClientObject);

                HandledPrefabs.Add(captured);
            }

            if (HandledPrefabs.Count > 0)
            {
                GameLog.Network.Info(
                    $"[NetworkUxrIdentity] Обработчик спавна поставлен на {HandledPrefabs.Count} префаб(ов) " +
                    "с компонентами UltimateXR: их UniqueId будут выровнены по netId.");
            }
        }

        /// <summary>Возвращает префабам штатный спавн Mirror.</summary>
        public static void UninstallClientSpawnHandlers()
        {
            foreach (GameObject prefab in HandledPrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                NetworkClient.UnregisterPrefab(prefab);
                NetworkClient.RegisterPrefab(prefab);
            }

            HandledPrefabs.Clear();
        }

        /// <summary>
        ///     Нужно ли префабу выравнивание. Условий два: в нём есть компоненты
        ///     UltimateXR, и его идентичность не ведёт сам SDK.
        /// </summary>
        private static bool NeedsIdentityAlignment(GameObject prefab)
        {
            if (prefab.GetComponentInChildren<IUxrUniqueId>(true) == null)
            {
                return false;
            }

            // Аватар выравнивает себя сам, в UxrMirrorAvatar.InitializeNetworkAvatar.
            return prefab.GetComponentInChildren<IUxrNetworkAvatar>(true) == null;
        }

        /// <summary>
        ///     Создаёт объект по спавн-сообщению. Возвращает его выключенным: Mirror
        ///     включит объект сам в <c>ApplySpawnPayload</c>, уже после того, как выставит
        ///     трансформ, — и <c>Awake</c> компонентов UltimateXR отработает с погашенным
        ///     «Auto Anchor» и с уже выровненными идентификаторами.
        /// </summary>
        private static GameObject SpawnClientObject(SpawnMessage message, GameObject prefab)
        {
            GameObject instance = CreateInstance(prefab);

            if (instance == null)
            {
                return null;
            }

            Align(instance, message.netId);

            return instance;
        }

        private static void UnspawnClientObject(GameObject spawned)
        {
            if (spawned != null)
            {
                UnityEngine.Object.Destroy(spawned);
            }
        }
    }
}
