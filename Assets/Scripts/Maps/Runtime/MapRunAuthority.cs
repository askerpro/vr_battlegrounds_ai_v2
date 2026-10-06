using System;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>Единственная публикация run descriptor на SessionContext; не начинает gameplay.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class MapRunAuthority : NetworkBehaviour
    {
        [SyncVar(hook = nameof(HandleSnapshotChanged))] private MapRunSnapshot _current;
        private Guid _sessionEpoch;
        private ulong _loadSequence;
        private MapRunScope _scope;
        private event Action<MapRunSnapshot> Changed;
        private bool _publishing;
        private bool _disposingScope;

        /// <summary>Экземпляр на SessionContext этой машины; null вне сети и в окне старта сессии.</summary>
        public static MapRunAuthority Instance { get; private set; }

        public MapRunSnapshot Current => _current;
        internal MapRunKey NextKey => _sessionEpoch == Guid.Empty || _loadSequence == ulong.MaxValue
            ? default : new MapRunKey(_sessionEpoch, _loadSequence + 1);

        /// <summary>Сервер готов принять новую загрузку: сессия поднята и не идёт teardown.</summary>
        internal bool CanBeginRun => CanWrite && NextKey.IsValid;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                GameLog.Network.Error("[MapRunAuthority] Второй экземпляр на SessionContext — игнорируется.", this);
                return;
            }
            Instance = this;
        }

        public void Subscribe(Action<MapRunSnapshot> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Changed -= handler;
            Changed += handler;
            Notify(handler, _current);
        }

        public void Unsubscribe(Action<MapRunSnapshot> handler) => Changed -= handler;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _sessionEpoch = Guid.NewGuid();
            _loadSequence = 0;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Host уже получил server commit; dedicated не зависит от client hook.
            if (!isServer) Publish(_current);
        }

        public override void OnStopServer()
        {
            _sessionEpoch = Guid.Empty;
            // Закрыть descriptor и право новых загрузок до пользовательских callbacks отмены.
            if (_scope != null && _current.Revision < ulong.MaxValue)
                CommitTerminal(MapBootstrapStatus.Retiring, string.Empty);
            else
                DisposeScope();
            base.OnStopServer();
        }

        private void OnDestroy()
        {
            DisposeScope();
            Changed = null;
            if (Instance == this) Instance = null;
        }

        internal bool BeginRun(MapRunResolution resolution, out MapRunScope scope)
        {
            scope = null;
            if (!CanWrite || resolution == null || !resolution.Passed || resolution.Config.Key != NextKey ||
                (_scope != null && _current.Status != MapBootstrapStatus.Retiring && _current.Status != MapBootstrapStatus.Failed)) return false;
            DisposeScope();
            _loadSequence = resolution.Config.Key.LoadSequence;
            _scope = new MapRunScope(resolution.Config.Key);
            scope = _scope;
            Commit(new MapRunSnapshot(resolution.Config, 1, MapBootstrapStatus.Preparing,
                MapState.Warmup, 0, string.Empty, 0, 0, 0, string.Empty));
            return true;
        }

        internal bool CommitPrepared(MapRunScope scope, ulong expectedRevision, uint refereeNetId, uint coordinatorNetId)
        {
            if (!Matches(scope, expectedRevision) || _current.Status != MapBootstrapStatus.Preparing ||
                refereeNetId == 0 || coordinatorNetId == 0 || refereeNetId == coordinatorNetId) return false;
            Commit(new MapRunSnapshot(_current.Config, expectedRevision + 1, MapBootstrapStatus.CompositionReady,
                MapState.Warmup, 0, string.Empty, 0, refereeNetId, coordinatorNetId, string.Empty));
            return true;
        }

        /// <summary>
        /// Закоммиченный режим карты: первый вызов после CompositionReady открывает server Ready,
        /// каждый следующий — новая эпоха режима. Пишет только MapReferee своего запуска.
        /// </summary>
        internal bool CommitMode(MapRunScope scope, ulong expectedRevision, MapState state, string modeId, uint modeNetId)
        {
            if (!Matches(scope, expectedRevision) || !MapRunResolver.Identifier(modeId) || modeNetId == 0 ||
                (_current.Status != MapBootstrapStatus.CompositionReady && _current.Status != MapBootstrapStatus.Ready) ||
                _current.ModeEpoch == ulong.MaxValue) return false;
            Commit(new MapRunSnapshot(_current.Config, expectedRevision + 1, MapBootstrapStatus.Ready, state,
                _current.ModeEpoch + 1, modeId, modeNetId, _current.RefereeNetId, _current.CoordinatorNetId, string.Empty));
            return true;
        }

        /// <summary>
        /// Принята загрузка следующей карты: descriptor получает Closing, scope отменяется, новые коммиты
        /// режима и допуск закрыты. Teardown — позже, <see cref="Retire"/> на выгрузке сцены.
        /// </summary>
        internal bool Close(MapRunScope scope, ulong expectedRevision)
        {
            if (!Matches(scope, expectedRevision) || _current.Status == MapBootstrapStatus.Closing) return false;
            Commit(new MapRunSnapshot(_current.Config, expectedRevision + 1, MapBootstrapStatus.Closing, _current.MapState,
                _current.ModeEpoch, _current.ActiveModeId, _current.ActiveModeNetId,
                _current.RefereeNetId, _current.CoordinatorNetId, string.Empty));
            try { scope.Close(); }
            catch (Exception error) { GameLog.Network.Error("[MapRunAuthority] Подписчик отмены запуска отказал: " + error); }
            return true;
        }

        internal bool Fail(MapRunScope scope, ulong expectedRevision, string code)
        {
            if (!Matches(scope, expectedRevision) || !MapRunResolver.Identifier(code)) return false;
            CommitTerminal(MapBootstrapStatus.Failed, code);
            return true;
        }

        internal bool Retire(MapRunScope scope, ulong expectedRevision)
        {
            if (!Matches(scope, expectedRevision)) return false;
            CommitTerminal(MapBootstrapStatus.Retiring, string.Empty);
            return true;
        }

        private bool CanWrite => NetworkServer.active && isServer && _sessionEpoch != Guid.Empty &&
            !_publishing && !_disposingScope;
        private bool Matches(MapRunScope scope, ulong revision) => CanWrite && scope != null &&
            ReferenceEquals(scope, _scope) && !scope.IsDisposed && scope.Key == _current.Key &&
            revision == _current.Revision && revision < ulong.MaxValue;

        private void CommitTerminal(MapBootstrapStatus status, string code)
        {
            Commit(new MapRunSnapshot(_current.Config, _current.Revision + 1, status, _current.MapState,
                _current.ModeEpoch, _current.ActiveModeId, _current.ActiveModeNetId,
                _current.RefereeNetId, _current.CoordinatorNetId, code));
            DisposeScope();
        }

        private void Commit(MapRunSnapshot snapshot)
        {
            _current = snapshot;
            // SyncVar hook на host вызывается Weaver-ом при assignment. Dedicated применяет явно.
            if (!NetworkServer.activeHost) Publish(snapshot);
        }

        private void HandleSnapshotChanged(MapRunSnapshot previous, MapRunSnapshot next)
        {
            Publish(next);
        }

        private void Publish(MapRunSnapshot snapshot)
        {
            if (Changed == null) return;
            _publishing = true;
            try
            {
                foreach (Action<MapRunSnapshot> handler in Changed.GetInvocationList()) Notify(handler, snapshot);
            }
            finally { _publishing = false; }
        }

        private static void Notify(Action<MapRunSnapshot> handler, MapRunSnapshot snapshot)
        {
            try { handler(snapshot); }
            catch (Exception error) { GameLog.Network.Error("[MapRunAuthority] Подписчик descriptor отказал: " + error); }
        }

        private void DisposeScope()
        {
            MapRunScope scope = _scope;
            _scope = null;
            if (scope == null) return;
            _disposingScope = true;
            try { scope.Dispose(); }
            catch (Exception error) { GameLog.Network.Error("[MapRunAuthority] Ошибка teardown: " + error); }
            finally { _disposingScope = false; }
        }
    }
}
