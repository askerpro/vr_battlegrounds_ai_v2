using Mirror;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Названия команд, заданные админом, — сетевое состояние (<c>SyncDictionary</c>): поздний клиент
    /// получает их начальным значением спавна. Живёт на <c>SessionContext</c> — объекте, который
    /// сервер спавнит при старте и держит до остановки, поэтому названия переживают смену карт серии.
    /// На каждой машине раздаёт словарь в <see cref="TeamNames"/>, откуда их читает весь UI
    /// (<see cref="TeamData.Name"/>).
    /// </summary>
    public class TeamNameService : NetworkBehaviour
    {
        public static TeamNameService Instance { get; private set; }

        /// <summary>teamIndex → название. Нет записи — имя ассета.</summary>
        private readonly SyncDictionary<int, string> _names = new SyncDictionary<int, string>();

        private void Awake()
        {
            _names.OnChange += (op, key, value) => Publish();
        }

        public override void OnStartServer()
        {
            Instance = this;
            Publish();
        }

        public override void OnStartClient()
        {
            Instance = this;
            Publish();
        }

        public override void OnStopClient() => Stop();

        public override void OnStopServer() => Stop();

        private void Stop()
        {
            if (Instance == this) Instance = null;
            TeamNames.Clear();
        }

        /// <summary>Задаёт название команды. Пустое — вернуть имя ассета. Права проверяет вызывающий.</summary>
        [Server]
        public void ServerSetName(int teamIndex, string name)
        {
            if (string.IsNullOrEmpty(name)) _names.Remove(teamIndex);
            else _names[teamIndex] = name;

            // На выделенном сервере колбэк SyncDictionary тоже срабатывает, но раздаём явно —
            // порядок не должен зависеть от Mirror.
            Publish();
        }

        private void Publish() => TeamNames.SetAll(_names);
    }
}
