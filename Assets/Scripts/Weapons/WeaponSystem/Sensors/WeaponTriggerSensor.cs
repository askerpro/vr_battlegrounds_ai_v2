using System.Collections.Generic;
using UltimateXR.Core;
using UltimateXR.Core.StateSync;

namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>
    /// Датчик спуска автора на этапе C: фронты нажатия/отпускания из синхронизируемого вызова SDK
    /// <c>SyncTriggerPressStates(trigger, pressed, down, up)</c> (событие <c>StateChanged</c> на машине стрелка).
    /// Порядок фронтов сохраняется очередью; уровень «нажат» — для <c>TriggerHeld</c>. Replay игнорируется:
    /// спуск — только у автора. На этапе E датчик заменит порт SDK <c>DecideLocalTrigger</c>.
    /// </summary>
    internal sealed class WeaponTriggerSensor
    {
        public enum Edge { Pressed, Released }

        private readonly int _trigger;
        private readonly Queue<Edge> _edges = new Queue<Edge>(4);

        public WeaponTriggerSensor(int trigger) { _trigger = trigger; }

        /// <summary>Спуск сейчас нажат (по последнему фронту).</summary>
        public bool Pressed { get; private set; }

        public int PendingEdges => _edges.Count;
        public bool HasPendingPress { get { foreach (Edge edge in _edges) if (edge == Edge.Pressed) return true; return false; } }

        public bool TryParse(UxrSyncEventArgs args)
        {
            if (!(args is UxrMethodInvokedSyncEventArgs method) || method.MethodName != "SyncTriggerPressStates" ||
                method.Parameters == null || method.Parameters.Length < 4 || !(method.Parameters[0] is int index) || index != _trigger) return false;
            if (UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync) return true;
            bool pressed = method.Parameters[1] is bool p && p;
            bool down = method.Parameters[2] is bool d && d;
            bool up = method.Parameters[3] is bool u && u;
            bool now = pressed && !up;
            if (Pressed && (!now || down)) _edges.Enqueue(Edge.Released);
            if (now && (!Pressed || down)) _edges.Enqueue(Edge.Pressed);
            Pressed = now;
            return true;
        }

        public bool TryDequeue(out Edge edge)
        {
            if (_edges.Count == 0) { edge = default; return false; }
            edge = _edges.Dequeue();
            return true;
        }

        /// <summary>Потеря контекста: SDK тоже сбрасывает эпизод.</summary>
        public void Reset()
        {
            _edges.Clear();
            Pressed = false;
        }
    }
}
