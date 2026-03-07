using Mirror;
using Mirror.Discovery;
using Unity.Multiplayer.Playmode;
using UnityEngine;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Управляет автоматическим определением роли приложения (сервер / клиент / хост)
    /// и запускает Mirror NetworkDiscovery в соответствии с этой ролью.
    ///
    /// Логика выбора роли при старте (вне редактора):
    ///   - Headless-билд (нет дисплея) ? сервер
    ///   - Обычный билд                ? клиент (ищет сервер через AutoDiscovery)
    ///
    /// В редакторе роль выбирается вручную через кнопки OnGUI.
    /// </summary>
    [RequireComponent(typeof(NetworkDiscovery))]
    public class GameNetworkDiscovery : MonoBehaviour
    {
        /// <summary>
        /// Роль, в которой работает данный экземпляр приложения.
        /// </summary>
        public enum AppRole
        {
            Server,
            Host,
            Client,
        }

        #region Inspector Properties/Serialized Fields

        [Tooltip("Показывать UI выбора роли в редакторе во время Play Mode")]
        [SerializeField] private bool _useEditorUI = true;

        #endregion

        #region Public Properties

        /// <summary>
        /// Текущая роль приложения. Null до момента выбора/определения роли.
        /// </summary>
        public AppRole? CurrentRole { get; private set; }

        #endregion

        #region Unity

        private void Awake()
        {
            _discovery = GetComponent<NetworkDiscovery>();
        }

        private void Start()
        {
            // Пробуем определить роль по тегу Multiplayer Play Mode
            AppRole? tagRole = TryGetRoleFromPlayerTag();
            if (tagRole.HasValue)
            {
                ApplyRole(tagRole.Value);
                return;
            }

            // В редакторе без тега — ручной выбор через UI
            if (Application.isEditor)
            {
                return;
            }

            // В билде роль определяется автоматически по наличию дисплея
            AppRole role = Mirror.Utils.IsHeadless() ? AppRole.Server : AppRole.Client;
            ApplyRole(role);
        }

        /// <summary>
        /// Читает теги Multiplayer Play Mode текущего экземпляра редактора.
        /// Возвращает роль если найден тег "Host", "Server" или "Client".
        /// </summary>
        private static AppRole? TryGetRoleFromPlayerTag()
        {
            foreach (string tag in CurrentPlayer.ReadOnlyTags())
            {
                if (string.Equals(tag, TagHost,   System.StringComparison.OrdinalIgnoreCase)) return AppRole.Host;
                if (string.Equals(tag, TagServer, System.StringComparison.OrdinalIgnoreCase)) return AppRole.Server;
                if (string.Equals(tag, TagClient, System.StringComparison.OrdinalIgnoreCase)) return AppRole.Client;
            }
            return null;
        }

        private void OnDestroy()
        {
            // Отписываемся от Discovery при уничтожении
            if (_discovery != null)
            {
                _discovery.OnServerFound.RemoveListener(OnServerFound);
            }
        }

        private void OnGUI()
        {
            if (!Application.isEditor || !_useEditorUI)
            {
                return;
            }

            _posY = 0;

            if (CurrentRole == null)
            {
                DrawRoleSelectionUI();
            }
            else
            {
                DrawStopUI();
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Применяет выбранную роль: запускает сеть и Discovery в нужном режиме.
        /// </summary>
        private void ApplyRole(AppRole role)
        {
            if (CurrentRole != null)
            {
                Debug.LogWarning($"[GameNetworkDiscovery] Роль уже выбрана: {CurrentRole}. Повторный вызов игнорируется.");
                return;
            }

            CurrentRole = role;
            Debug.Log($"[GameNetworkDiscovery] Запуск в режиме: {role}");

            switch (role)
            {
                case AppRole.Server:
                    NetworkManager.singleton.StartServer();
                    _discovery.AdvertiseServer();
                    break;

                case AppRole.Host:
                    NetworkManager.singleton.StartHost();
                    _discovery.AdvertiseServer();
                    break;

                case AppRole.Client:
                    _discovery.OnServerFound.AddListener(OnServerFound);
                    _discovery.StartDiscovery();
                    break;
            }
        }

        /// <summary>
        /// Останавливает сеть и сбрасывает роль.
        /// </summary>
        private void StopCurrent()
        {
            if (CurrentRole == null)
            {
                return;
            }

            _discovery.OnServerFound.RemoveListener(OnServerFound);
            _discovery.StopDiscovery();

            switch (CurrentRole)
            {
                case AppRole.Server:
                    NetworkManager.singleton.StopServer();
                    break;
                case AppRole.Host:
                    NetworkManager.singleton.StopHost();
                    break;
                case AppRole.Client:
                    NetworkManager.singleton.StopClient();
                    break;
            }

            CurrentRole = null;
        }

        /// <summary>
        /// Вызывается когда клиент обнаружил сервер в локальной сети.
        /// </summary>
        private void OnServerFound(ServerResponse response)
        {
            Debug.Log($"[GameNetworkDiscovery] Сервер найден: {response.serverId} | {response.EndPoint} | {response.uri}");

            _discovery.StopDiscovery();
            _discovery.OnServerFound.RemoveListener(OnServerFound);

            NetworkManager.singleton.StartClient(response.uri);
        }

        #endregion

        #region Editor UI

        private void DrawRoleSelectionUI()
        {
            GUI.Box(new Rect(0, _posY, ButtonWidth, ButtonHeight), "Выбор режима запуска");
            _posY += ButtonHeight;

            if (GUI.Button(new Rect(0, _posY, ButtonWidth, ButtonHeight), "Запустить как Server"))
            {
                ApplyRole(AppRole.Server);
            }

            _posY += ButtonHeight;

            if (GUI.Button(new Rect(0, _posY, ButtonWidth, ButtonHeight), "Запустить как Host"))
            {
                ApplyRole(AppRole.Host);
            }

            _posY += ButtonHeight;

            if (GUI.Button(new Rect(0, _posY, ButtonWidth, ButtonHeight), "Запустить как Client"))
            {
                ApplyRole(AppRole.Client);
            }
        }

        private void DrawStopUI()
        {
            string label = $"Режим: {CurrentRole}";
            GUI.Box(new Rect(0, _posY, ButtonWidth, ButtonHeight), label);
            _posY += ButtonHeight;

            string stopLabel = CurrentRole switch
            {
                AppRole.Server => "Остановить Server",
                AppRole.Host   => "Остановить Host",
                AppRole.Client => "Отключить Client",
                _              => "Остановить",
            };

            if (GUI.Button(new Rect(0, _posY, ButtonWidth, ButtonHeight), stopLabel))
            {
                StopCurrent();
            }
        }

        #endregion

        #region Private Data

        private const int ButtonWidth  = 220;
        private const int ButtonHeight = 40;

        // Теги Multiplayer Play Mode для автоматического выбора роли
        private const string TagHost   = "Host";
        private const string TagServer = "Server";
        private const string TagClient = "Client";

        private NetworkDiscovery _discovery;
        private int _posY;

        #endregion
    }
}
