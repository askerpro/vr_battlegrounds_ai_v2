using Mirror;
using Mirror.Discovery;

using UnityEngine;
using VrBattlegrounds.Core;

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

            // В редакторе без тега — роль из отладочных настроек разработчика (Bootstrap Settings) или ручной выбор через UI
            if (Application.isEditor)
            {
                AppRole? editorRole = VrBattlegrounds.DevTools.DebugBootstrapGate.EditorRoleOverride?.Invoke();
                if (editorRole.HasValue)
                {
                    ApplyRole(editorRole.Value);
                    return;
                }

                if (_useEditorUI)
                {
                    CreateRuntimeUI();
                    UpdateRuntimeUI();
                }
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
            foreach (string tag in Unity.Multiplayer.PlayMode.CurrentPlayer.ReadOnlyTags())
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



        #endregion

        #region Public Methods

        /// <summary>
        /// Бросает текущую роль и поднимает хост. Нужен шлему без ПК: в билде он всегда
        /// стартует клиентом и ждёт сервер — режим отладки (<c>DebugGestureInput</c>) делает
        /// из него хост жестом «оба стика 2 с».
        /// </summary>
        public void RestartAsHost()
        {
            StopCurrent();
            ApplyRole(AppRole.Host);
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
                GameLog.Network.Warning($"[GameNetworkDiscovery] Роль уже выбрана: {CurrentRole}. Повторный вызов игнорируется.");
                return;
            }

            CurrentRole = role;
            GameLog.Network.Info($"[GameNetworkDiscovery] Запуск в режиме: {role}");

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
            UpdateRuntimeUI();
        }

        /// <summary>
        /// Вызывается когда клиент обнаружил сервер в локальной сети.
        /// </summary>
        private void OnServerFound(ServerResponse response)
        {
            GameLog.Network.Info($"[GameNetworkDiscovery] Сервер найден: {response.serverId} | {response.EndPoint} | {response.uri}");

            _discovery.StopDiscovery();
            _discovery.OnServerFound.RemoveListener(OnServerFound);

            NetworkManager.singleton.StartClient(response.uri);
        }

        #endregion

        #region Editor UI (Runtime Canvas)

        private GameObject _uiCanvas;
        private GameObject _selectionPanel;
        private GameObject _stopPanel;
        private UnityEngine.UI.Text _statusText;

        private void CreateRuntimeUI()
        {
            if (_uiCanvas != null) return;

            // Canvas
            _uiCanvas = new GameObject("GameNetworkDiscovery_UI");
            DontDestroyOnLoad(_uiCanvas);

            var canvas = _uiCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            
            _uiCanvas.AddComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            _uiCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // Базовые панели
            _selectionPanel = CreatePanel("SelectionPanel");
            _stopPanel = CreatePanel("StopPanel");
            _stopPanel.SetActive(false);

            // Кнопки выбора
            CreateButton(_selectionPanel.transform, "Запустить как Server", 0, () => ApplyRoleSafe(AppRole.Server));
            CreateButton(_selectionPanel.transform, "Запустить как Host",   1, () => ApplyRoleSafe(AppRole.Host));
            CreateButton(_selectionPanel.transform, "Запустить как Client", 2, () => ApplyRoleSafe(AppRole.Client));

            // Статус текст
            var textObj = new GameObject("StatusText");
            textObj.transform.SetParent(_selectionPanel.transform, false);
            _statusText = textObj.AddComponent<UnityEngine.UI.Text>();
            _statusText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _statusText.fontSize = 14;
            _statusText.color = Color.yellow;
            _statusText.alignment = TextAnchor.MiddleCenter;
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0, 1);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.pivot = new Vector2(0.5f, 1);
            textRect.anchoredPosition = new Vector2(0, -140);
            textRect.sizeDelta = new Vector2(0, 30);

            // Панель стопа - статус
            var stopLabelObj = new GameObject("RoleText");
            stopLabelObj.transform.SetParent(_stopPanel.transform, false);
            var roleText = stopLabelObj.AddComponent<UnityEngine.UI.Text>();
            roleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            roleText.fontSize = 16;
            roleText.color = Color.white;
            roleText.alignment = TextAnchor.MiddleCenter;
            var rRect = stopLabelObj.GetComponent<RectTransform>();
            rRect.anchorMin = new Vector2(0, 1);
            rRect.anchorMax = new Vector2(1, 1);
            rRect.pivot = new Vector2(0.5f, 1);
            rRect.anchoredPosition = new Vector2(0, -10);
            rRect.sizeDelta = new Vector2(0, 40);

            CreateButton(_stopPanel.transform, "Остановить", 1, StopCurrent);
        }

        private GameObject CreatePanel(string name)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(_uiCanvas.transform, false);
            var panelImage = panel.AddComponent<UnityEngine.UI.Image>();
            panelImage.color = new Color(0, 0, 0, 0.85f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 1);
            panelRect.anchorMax = new Vector2(0, 1);
            panelRect.pivot = new Vector2(0, 1);
            panelRect.anchoredPosition = new Vector2(10, -10);
            panelRect.sizeDelta = new Vector2(250, 180);
            return panel;
        }

        private void CreateButton(Transform parent, string label, int index, UnityEngine.Events.UnityAction onClick)
        {
            var btnObj = new GameObject($"Btn_{index}");
            btnObj.transform.SetParent(parent, false);
            var img = btnObj.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            var btn = btnObj.AddComponent<UnityEngine.UI.Button>();
            btn.onClick.AddListener(onClick);

            var cb = btn.colors;
            cb.highlightedColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            btn.colors = cb;

            var rect = btnObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -10 - (index * 45));
            rect.sizeDelta = new Vector2(-20, 40);

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            var text = textObj.AddComponent<UnityEngine.UI.Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 16;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        private void UpdateRuntimeUI()
        {
            if (_uiCanvas == null) return;

            if (CurrentRole == null)
            {
                _selectionPanel.SetActive(true);
                _stopPanel.SetActive(false);
            }
            else
            {
                _selectionPanel.SetActive(false);
                _stopPanel.SetActive(true);

                var roleText = _stopPanel.transform.GetChild(0).GetComponent<UnityEngine.UI.Text>();
                roleText.text = $"Режим: {CurrentRole}";

                var stopBtnText = _stopPanel.transform.GetChild(1).GetChild(0).GetComponent<UnityEngine.UI.Text>();
                stopBtnText.text = CurrentRole switch
                {
                    AppRole.Server => "Остановить Server",
                    AppRole.Host   => "Остановить Host",
                    AppRole.Client => "Отключить Client",
                    _              => "Остановить",
                };
            }
        }

        private void ApplyRoleSafe(AppRole role)
        {
            try
            {
                ApplyRole(role);
                UpdateRuntimeUI();
            }
            catch (System.Exception e)
            {
                if (_statusText != null) _statusText.text = "Ошибка! См. консоль";
                GameLog.Error($"[GameNetworkDiscovery] Ошибка при ApplyRole({role}):\n{e}");
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
