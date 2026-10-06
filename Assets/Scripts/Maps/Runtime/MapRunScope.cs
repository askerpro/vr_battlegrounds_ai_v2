using System;
using System.Collections.Generic;
using System.Threading;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>Владеет только ресурсами одной загрузки. Отмена раньше reverse-order teardown.</summary>
    public sealed class MapRunScope : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly List<Action> _releases = new List<Action>();
        private readonly CancellationToken _token;
        public MapRunKey Key { get; }
        public CancellationToken Cancellation => _token;
        public bool IsDisposed { get; private set; }

        /// <summary>Отмена объявлена: писатели запуска остановлены, ресурсы ещё живы до Dispose.</summary>
        public bool IsClosed => IsDisposed || _token.IsCancellationRequested;

        public MapRunScope(MapRunKey key)
        {
            if (!key.IsValid) throw new ArgumentException("RunKey.Invalid", nameof(key));
            Key = key;
            _token = _cancellation.Token;
        }

        public void Own(Action release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (IsDisposed) release();
            else _releases.Add(release);
        }

        public void Own(IDisposable resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            Own(resource.Dispose);
        }

        /// <summary>
        /// Закрыть запуск без teardown: отменить <see cref="Cancellation"/>, по которому владельцы
        /// прекращают пополнение и RPC. Reverse-order teardown остаётся за <see cref="Dispose"/>.
        /// </summary>
        public void Close()
        {
            if (IsDisposed || _token.IsCancellationRequested) return;
            _cancellation.Cancel();
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            var failures = new List<Exception>();
            try { if (!_token.IsCancellationRequested) _cancellation.Cancel(); }
            catch (Exception error) { failures.Add(error); }
            for (int i = _releases.Count - 1; i >= 0; i--)
            {
                try { _releases[i](); }
                catch (Exception error) { failures.Add(error); }
            }
            _releases.Clear();
            _cancellation.Dispose();
            if (failures.Count > 0) throw new AggregateException("MapRunScope.TeardownFailed", failures);
        }
    }
}
