using System;

namespace VrBattlegrounds.DevTools
{
    /// <summary>Сериализуемый вход запуска. В runtime попадает замороженная копия, а не профиль окна.</summary>
    [Serializable]
    public sealed class PlayLaunchConfiguration
    {
        public int SchemaVersion = 1;
        public bool Enabled = true;
        public string SceneSource = "active";
        public string ScenePath = "";
        public string Role = "host";
        public string ClientAddress = "";
        public string NetworkPortPolicy = "default";
        public int NetworkPort;
        public int DiscoveryPort;
        public int ClientCount;
        public string[] AdditionalPlayerRoles;
        public bool HostIsAdmin = true;
        public string DeviceType = "VR";
        public string GameRole = "Player";
        public string ModeId = "elimination";
        public bool AutoGoLive = true;
        public int MinPlayers = 1;
        public int BotCount;
        public bool PauseOnFocusLoss = true;
        public bool ScreenshotOnButtonB;
        public bool AllowUnmarked;
        public string Readiness = "map-playable";
        public int TimeoutSeconds = 90;

        public PlayLaunchConfiguration Copy()
        {
            var copy = (PlayLaunchConfiguration)MemberwiseClone();
            copy.AdditionalPlayerRoles = AdditionalPlayerRoles == null ? null : (string[])AdditionalPlayerRoles.Clone();
            return copy;
        }
        // Unity JsonUtility восстанавливает null-массив как пустой: оба означают штатные роли client.
        public string RoleForParticipant(int index) => index == 0 ? Role : AdditionalPlayerRoles == null || AdditionalPlayerRoles.Length == 0 ? "client" : AdditionalPlayerRoles[index - 1];
        public bool HasOwnedServer
        {
            get { if (Role == "server" || Role == "host") return true; if (AdditionalPlayerRoles == null) return false;
                foreach (string role in AdditionalPlayerRoles) if (role == "server" || role == "host") return true; return false; }
        }

        public void Validate()
        {
            if (SchemaVersion != 1) throw new PlayLaunchException("SchemaUnsupported", "Поддерживается schemaVersion 1.");
            if (NetworkPortPolicy != "default" && NetworkPortPolicy != "fixed" && NetworkPortPolicy != "auto")
                throw new PlayLaunchException("PortPolicyInvalid", "Режим портов: default, fixed или auto.");
            if (NetworkPortPolicy == "fixed" && (NetworkPort < 1 || NetworkPort > 65535 || DiscoveryPort < 1 || DiscoveryPort > 65535 || NetworkPort == DiscoveryPort))
                throw new PlayLaunchException("PortInvalid", "Нужны разные игровые/discovery порты в диапазоне 1..65535.");
            if (Role != "host" && Role != "server" && Role != "client" && Role != "ask")
                throw new PlayLaunchException("RoleInvalid", "Роль: host, server, client или ask.");
            if (ClientCount < 0 || ClientCount > 16 || BotCount < 0 || MinPlayers < 0)
                throw new PlayLaunchException("CountInvalid", "Число клиентов 0..16; число ботов/минимум игроков неотрицательны.");
            if (AdditionalPlayerRoles != null && AdditionalPlayerRoles.Length > 0 && AdditionalPlayerRoles.Length != ClientCount) throw new PlayLaunchException("TopologyInvalid", "Число ролей не совпадает с числом дополнительных игроков.");
            int servers = Role == "server" || Role == "host" ? 1 : 0;
            if (AdditionalPlayerRoles != null)
                foreach (string role in AdditionalPlayerRoles) {
                    if (role != "server" && role != "host" && role != "client") throw new PlayLaunchException("RoleInvalid", "Неизвестная роль дополнительного игрока.");
                    if (role == "server" || role == "host") servers++;
                }
            if (servers > 1) throw new PlayLaunchException("TopologyInvalid", "Один локальный запуск не создаёт два сервера на одном порту.");
            if (TimeoutSeconds < 1 || TimeoutSeconds > 3600) throw new PlayLaunchException("TimeoutInvalid", "Таймаут 1..3600 секунд.");
            if (SceneSource != "active" && SceneSource != "lobby" && SceneSource != "scene")
                throw new PlayLaunchException("SceneSourceInvalid", "Источник сцены: active, lobby или scene.");
            if (SceneSource == "scene" && (string.IsNullOrEmpty(ScenePath) || !ScenePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                ScenePath.Contains("..") || ScenePath.Contains("\\") || !ScenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)))
                throw new PlayLaunchException("ScenePathInvalid", "Нужен project-relative путь Assets/...unity.");
            if (Readiness != "map-playable" && Readiness != "connected" && Readiness != "standalone")
                throw new PlayLaunchException("ReadinessInvalid", "Неизвестный критерий готовности.");
            if (DeviceType != "VR" && DeviceType != "PC" && DeviceType != "Tablet")
                throw new PlayLaunchException("ProfileInvalid", "Неизвестный тип устройства.");
            if (GameRole != "Player" && GameRole != "Spectator") throw new PlayLaunchException("ProfileInvalid", "Неизвестная игровая роль.");
        }
    }

    public sealed class PlayLaunchException : InvalidOperationException
    {
        public string Code { get; }
        public PlayLaunchException(string code, string message) : base(message) { Code = code; }
    }

    /// <summary>Единственный владелец разового запроса в процессе. Dispose старой lease не отзывает новую.</summary>
    public sealed class PlayLaunchRequestOwner
    {
        private Lease _lease;
        private PlayLaunchConfiguration _configuration;
        public bool IsActive => _lease != null;
        public string Owner => _lease?.Owner;
        public string RunId => _lease?.RunId;
        public PlayLaunchConfiguration Configuration => _configuration?.Copy();

        public IDisposable Acquire(string owner, PlayLaunchConfiguration configuration, string runId = null)
        {
            if (IsActive) throw new PlayLaunchException("RequestOwnerBusy", "Запросом владеет " + Owner);
            if (string.IsNullOrWhiteSpace(owner) || owner.Length > 128) throw new PlayLaunchException("OwnerInvalid", "Нужен owner длиной 1..128.");
            if (configuration == null) throw new PlayLaunchException("ConfigurationInvalid", "Нет конфигурации.");
            configuration.Validate();
            if (runId != null && (!Guid.TryParseExact(runId, "N", out _))) throw new PlayLaunchException("RunInvalid", "Некорректный run id.");
            _configuration = configuration.Copy();
            _lease = new Lease(this, owner, runId ?? Guid.NewGuid().ToString("N"));
            return _lease;
        }

        private sealed class Lease : IDisposable
        {
            private readonly PlayLaunchRequestOwner _parent;
            internal readonly string Owner;
            internal readonly string RunId;
            internal Lease(PlayLaunchRequestOwner parent, string owner, string runId) { _parent = parent; Owner = owner; RunId = runId; }
            public void Dispose()
            {
                if (!ReferenceEquals(_parent._lease, this)) return;
                _parent._configuration = null;
                _parent._lease = null;
            }
        }
    }
}
