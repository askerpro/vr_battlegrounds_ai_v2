using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using kcp2k;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Базовый класс сетевых EditMode-тестов: поднимает Mirror сервером без сокета.
    ///
    /// Зачем. Почти вся серверная логика проекта помечена <c>[Server]</c>, а Mirror вне
    /// активного сервера такие методы молча заглушает — тест вызывает метод, тот ничего
    /// не делает, тест зеленеет и не проверяет ничего. Харнесс делает
    /// <c>NetworkServer.active == true</c>, и заглушка перестаёт срабатывать.
    ///
    /// Ярус A (по умолчанию) — только сервер.
    /// Ярус B — плюс локальный клиент, включается переопределением
    /// <see cref="NeedsLocalClient"/>.
    ///
    /// Подробный разбор рецепта и ограничений — в <c>Docs/testing.md</c>,
    /// раздел «Как тестировать сетевую логику».
    ///
    /// Ключевое отличие EditMode от рантайма: Unity не вызывает <c>Awake</c> ни при
    /// <c>AddComponent</c>, ни при <c>Instantiate</c>. Поэтому харнесс дёргает
    /// <c>Awake</c> вручную — иначе у <c>KcpTransport</c> не создан внутренний сервер
    /// (NRE в <c>ServerStop</c>), а у <c>NetworkIdentity</c> не заполнен массив
    /// <c>NetworkBehaviours</c> (NRE в любом обращении к <c>isServer</c>).
    /// </summary>
    public abstract class MirrorTestHarness
    {
        private const int MaxConnections = 4;

        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private const BindingFlags StaticMembers =
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        // ── Внутренние точки Mirror, к которым нет публичного доступа ────────

        private static readonly MethodInfo IdentityAwake =
            typeof(NetworkIdentity).GetMethod("Awake", InstanceMembers);

        private static readonly MethodInfo ServerEarlyUpdate =
            typeof(NetworkServer).GetMethod("NetworkEarlyUpdate", StaticMembers);

        private static readonly MethodInfo ServerLateUpdate =
            typeof(NetworkServer).GetMethod("NetworkLateUpdate", StaticMembers);

        private static readonly MethodInfo ClientEarlyUpdate =
            typeof(NetworkClient).GetMethod("NetworkEarlyUpdate", StaticMembers);

        private static readonly MethodInfo ClientLateUpdate =
            typeof(NetworkClient).GetMethod("NetworkLateUpdate", StaticMembers);

        private static readonly MethodInfo SerializeServer =
            typeof(NetworkIdentity).GetMethod("SerializeServer", InstanceMembers);

        private static readonly MethodInfo DeserializeClient =
            typeof(NetworkIdentity).GetMethod("DeserializeClient", InstanceMembers);

        /// <summary>
        /// Синглтоны проекта, которые харнесс обнуляет между тестами.
        /// Без этого менеджер, созданный одним тестом, доживает до следующего
        /// (Awake проверяет <c>Instance != null</c> и зовёт Destroy, запрещённый в EditMode).
        /// </summary>
        private static readonly Type[] ProjectSingletons =
        {
            typeof(PlayersManager),
            typeof(SessionRecoveryManager),
            typeof(AvatarManager),
            typeof(MapReferee),
            typeof(SessionManager),
            typeof(Series),
            typeof(MapLoader)
        };

        private GameObject _transportObject;
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        /// <summary>
        /// Ярус B: поднимать ли локального клиента (host-режим). По умолчанию — нет.
        /// </summary>
        protected virtual bool NeedsLocalClient => false;

        // ── Жизненный цикл ───────────────────────────────────────────────────

        [SetUp]
        public void StartMirror()
        {
            SilenceMirrorNoise();
            AssertReflectionIsIntact();
            ResetProjectStatics();
            _createdObjects.Clear();

            // Транспорт нужен как объект даже без сокета: NetworkServer.Initialize()
            // делает Debug.Assert(Transport.active != null) и подписывается на его события.
            _transportObject = new GameObject("TestTransport");
            KcpTransport transport = _transportObject.AddComponent<KcpTransport>();

            // Awake создаёт внутренние KcpServer/KcpClient. В EditMode Unity его не зовёт,
            // и тогда TearDown падает с NRE в KcpTransport.ServerStop().
            InvokeLifecycleMethod(transport, "Awake");

            // Отключённый компонент не тикает сокет в Server/ClientEarlyUpdate,
            // а прокрутка сети зовёт эти методы напрямую.
            transport.enabled = false;

            Transport.active = transport;

            NetworkServer.listen = false; // Listen() пропустит ServerStart(), сокет не откроется
            NetworkServer.Listen(MaxConnections);

            if (NeedsLocalClient)
            {
                NetworkClient.ConnectHost();

                // Одного ConnectHost() мало: он только создаёт пару локальных соединений.
                // Регистрацию соединения на сервере делает InvokeOnConnected — без неё
                // NetworkServer.connections пуст и очередь сообщений никогда не разбирается.
                HostMode.InvokeOnConnected();

                // В бою флаг ставит NetworkManager после аутентификации. Без него сервер
                // рвёт соединение на первом же сообщении: «required authentication».
                NetworkServer.localConnection.isAuthenticated = true;
                NetworkClient.connection.isAuthenticated = true;

                NetworkClient.Ready();
                PumpNetwork();
            }
        }

        [TearDown]
        public void StopMirror()
        {
            SilenceMirrorNoise();
            try
            {
                NetworkClient.Shutdown();
                NetworkServer.Shutdown();
            }
            finally
            {
                Transport.active = null;

                if (_transportObject != null) UnityEngine.Object.DestroyImmediate(_transportObject);
                _transportObject = null;

                // Часть объектов уже уничтожил NetworkServer.Shutdown() → CleanupSpawned().
                foreach (GameObject go in _createdObjects)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
                _createdObjects.Clear();

                ResetProjectStatics();
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>
        /// Глушит логи Mirror. Звать первой строкой каждого теста: тестовый фреймворк
        /// сбрасывает флаг после SetUp, и Error вида «called without an active server»
        /// валит тест ещё до проверки утверждений.
        /// </summary>
        protected static void SilenceMirrorNoise()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        // ── Создание объектов ────────────────────────────────────────────────

        /// <summary>Создаёт GameObject и ставит его в очередь на уборку в TearDown.</summary>
        protected GameObject CreateObject(string name)
        {
            GameObject go = new GameObject(name);
            _createdObjects.Add(go);
            return go;
        }

        /// <summary>
        /// Заготовка сетевого объекта: пустой GameObject с уже добавленной
        /// <see cref="NetworkIdentity"/>.
        ///
        /// Порядок компонентов важен: <c>NetworkBehaviour.OnValidate</c> пишет Error
        /// «requires a NetworkIdentity», если сетевой компонент добавлен раньше идентити.
        /// После того как все компоненты добавлены, звать <see cref="EnableNetworking"/>.
        /// </summary>
        protected GameObject CreateNetworkObject(string name)
        {
            GameObject go = CreateObject(name);
            go.AddComponent<NetworkIdentity>();
            return go;
        }

        /// <summary>
        /// Связывает <see cref="NetworkIdentity"/> со всеми висящими на объекте
        /// <see cref="NetworkBehaviour"/>. Звать после того, как все сетевые компоненты
        /// добавлены — Mirror собирает их массивом один раз, в Awake.
        /// </summary>
        protected NetworkIdentity EnableNetworking(GameObject go)
        {
            NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
            if (identity == null) identity = go.AddComponent<NetworkIdentity>();

            IdentityAwake.Invoke(identity, null);
            return identity;
        }

        /// <summary>Готовый сетевой объект с одним компонентом — частый случай.</summary>
        protected T CreateNetworkComponent<T>(string name) where T : NetworkBehaviour
        {
            GameObject go = CreateNetworkObject(name);
            T component = go.AddComponent<T>();
            EnableNetworking(go);
            return component;
        }

        /// <summary>
        /// Создаёт менеджер-синглтон и вручную вызывает его <c>Awake</c>,
        /// чтобы заполнилось статическое поле Instance.
        /// </summary>
        protected T CreateManager<T>(string name) where T : MonoBehaviour
        {
            GameObject go = CreateObject(name);
            T manager = go.AddComponent<T>();
            InvokeLifecycleMethod(manager, "Awake");
            return manager;
        }

        /// <summary>Спавнит объект на сервере (выдаёт netId, зовёт OnStartServer).</summary>
        protected static void SpawnOnServer(Component component)
        {
            NetworkServer.Spawn(component.gameObject);
        }

        // ── Прокрутка сети и репликация ──────────────────────────────────────

        /// <summary>
        /// Гоняет сетевой цикл Mirror руками: в EditMode PlayerLoop не крутится,
        /// а сообщения между локальными соединениями лежат в очередях до Update().
        /// Порядок и число проходов важны: сообщение проходит клиент → сервер
        /// за два прохода (сначала flush батча, потом разбор очереди).
        /// </summary>
        protected static void PumpNetwork(int frames = 3)
        {
            for (int i = 0; i < frames; i++)
            {
                ServerEarlyUpdate.Invoke(null, null);
                ClientEarlyUpdate.Invoke(null, null);
                ServerLateUpdate.Invoke(null, null);
                ClientLateUpdate.Invoke(null, null);
            }
        }

        /// <summary>
        /// Прогоняет состояние серверного объекта через настоящую сериализацию Mirror
        /// и применяет её к отдельному клиентскому объекту-двойнику.
        ///
        /// Зачем это, если есть ярус B: в host-режиме сервер и клиент делят один и тот же
        /// экземпляр объекта (<c>NetworkClient.OnHostClientSpawn</c> просто кладёт ссылку
        /// из <c>NetworkServer.spawned</c>), поэтому SyncVar там «долетают» сами собой
        /// и ничего не проверяют. Здесь данные реально пишутся в байты и читаются обратно —
        /// как у удалённого клиента.
        /// </summary>
        protected static void ReplicateToClient(NetworkBehaviour server, NetworkBehaviour client)
        {
            NetworkWriter ownerWriter = new NetworkWriter();
            NetworkWriter observersWriter = new NetworkWriter();

            SerializeServer.Invoke(server.netIdentity, new object[] { true, ownerWriter, observersWriter });

            byte[] payload = observersWriter.ToArray();
            Assert.Greater(payload.Length, 0,
                "Серверный объект не отдал ни одного байта — сериализовать нечего.");

            DeserializeClient.Invoke(client.netIdentity, new object[] { new NetworkReader(payload), true });
        }

        // ── Рефлексия ────────────────────────────────────────────────────────

        /// <summary>Вызывает приватный метод, поднимаясь по иерархии типов.</summary>
        protected static object InvokePrivateMethod(object target, string name, params object[] args)
        {
            Type type = target.GetType();
            while (type != null)
            {
                MethodInfo method = type.GetMethod(name, InstanceMembers | BindingFlags.DeclaredOnly);
                if (method != null)
                {
                    try
                    {
                        return method.Invoke(target, args);
                    }
                    catch (TargetInvocationException e) when (e.InnerException != null)
                    {
                        // Пробрасываем исходное исключение — иначе в отчёте теста
                        // видно только обёртку и непонятно, что упало.
                        ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                        throw;
                    }
                }
                type = type.BaseType;
            }

            throw new MissingMethodException(target.GetType().FullName, name);
        }

        /// <summary>
        /// Вызывает метод жизненного цикла (Awake / Start), если он есть.
        /// В EditMode Unity их не зовёт ни при AddComponent, ни при Instantiate.
        /// </summary>
        protected static void InvokeLifecycleMethod(object target, string name)
        {
            InvokePrivateMethod(target, name);
        }

        /// <summary>Пишет значение в приватное поле — обычно в сериализованное поле SDK.</summary>
        protected static void SetPrivateField(object target, string name, object value)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name, InstanceMembers | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
                type = type.BaseType;
            }

            throw new MissingFieldException(target.GetType().FullName, name);
        }

        // ── Сброс статики ────────────────────────────────────────────────────

        private static void ResetProjectStatics()
        {
            foreach (Type type in ProjectSingletons)
            {
                ClearStaticProperty(type, "Instance");
            }

            ClearStaticProperty(typeof(PlayerSession), "LocalSession");
        }

        private static void ClearStaticProperty(Type type, string propertyName)
        {
            PropertyInfo property = type.GetProperty(propertyName, StaticMembers);
            if (property == null) return;

            MethodInfo setter = property.GetSetMethod(true);
            if (setter != null) setter.Invoke(null, new object[] { null });
        }

        private static void AssertReflectionIsIntact()
        {
            Assert.IsNotNull(IdentityAwake, "Mirror: не найден NetworkIdentity.Awake — харнесс несовместим с этой версией SDK.");
            Assert.IsNotNull(ServerEarlyUpdate, "Mirror: не найден NetworkServer.NetworkEarlyUpdate.");
            Assert.IsNotNull(ServerLateUpdate, "Mirror: не найден NetworkServer.NetworkLateUpdate.");
            Assert.IsNotNull(ClientEarlyUpdate, "Mirror: не найден NetworkClient.NetworkEarlyUpdate.");
            Assert.IsNotNull(ClientLateUpdate, "Mirror: не найден NetworkClient.NetworkLateUpdate.");
            Assert.IsNotNull(SerializeServer, "Mirror: не найден NetworkIdentity.SerializeServer.");
            Assert.IsNotNull(DeserializeClient, "Mirror: не найден NetworkIdentity.DeserializeClient.");
        }
    }
}
