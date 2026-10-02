using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Ноги бота (T-48): идёт к точке, которую назначил директор, шагом человека. Только сервер; висит рядом с
    /// <see cref="BotBody"/> и двигает его <see cref="BotBody.Feet"/> (голову), а не корень — как ходит игрок в арене.
    ///
    /// <para>
    /// Путь — <see cref="BotNavMesh"/> (сетка в памяти сервера), шаг по нему — чистая <see cref="BotRoute"/>.
    /// Цель, которая сама движется (враг), пересчитывается, когда отъехала дальше <see cref="RepathDistance"/>,
    /// но не чаще <see cref="RepathInterval"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BotBody))]
    public sealed class BotNavigator : MonoBehaviour
    {
        /// <summary>Шаг человека, м/с (физическая ходьба по арене — 0,5–1,5).</summary>
        public const float WalkSpeed = 1.3f;

        /// <summary>Ближе этого к цели — пришли (и больше не топчемся).</summary>
        public const float ArriveDistance = 0.25f;

        private const float RepathDistance = 1.5f;
        private const float RepathInterval = 1f;
        private const float StaleInterval = 4f;

        private BotBody _body;
        private readonly List<Vector3> _corners = new List<Vector3>();
        private int _next;
        private bool _hasDestination;
        private Vector3 _destination;
        private float _pathAt;
        private bool _arrived = true;

        /// <summary>Цели нет или пришли.</summary>
        public bool Arrived => !_hasDestination || _arrived;

        /// <summary>Путь найден по сетке (а не прямая).</summary>
        public bool OnNavMesh { get; private set; }

        private void Awake()
        {
            _body = GetComponent<BotBody>();
        }

        /// <summary>Идти к точке. Та же цель — путь не пересчитывается.</summary>
        public void GoTo(Vector3 destination)
        {
            // Та же цель (сдвинулась меньше RepathDistance): пришли — стоим; идём — путь прежний, раз в
            // StaleInterval пересчитываем (вдруг упёрлись).
            bool same = _hasDestination && (destination - _destination).sqrMagnitude < RepathDistance * RepathDistance;
            if (same && (_arrived || Time.time - _pathAt < StaleInterval)) return;
            if (!same && _hasDestination && !_arrived && Time.time - _pathAt < RepathInterval) return;

            _hasDestination = true;
            _destination = destination;
            _pathAt = Time.time;

            Vector3 feet = _body.Feet;
            if (Flat(destination - feet).sqrMagnitude < ArriveDistance * ArriveDistance)
            {
                _corners.Clear();
                _arrived = true;
                _body.StopWalking();
                return;
            }

            OnNavMesh = BotNavMesh.TryPath(feet, destination, _corners);
            _next = 1;
            _arrived = false;
        }

        /// <summary>Остановиться на месте.</summary>
        public void Stop()
        {
            _hasDestination = false;
            _corners.Clear();
            _arrived = true;
            _body.StopWalking();
        }

        private void Update()
        {
            if (!NetworkServer.active || !_hasDestination || _arrived) return;

            Vector3 from = _body.Feet;
            Vector3 to = BotRoute.Step(from, _corners, ref _next, WalkSpeed * Time.deltaTime, out bool arrived);

            _body.WalkTo(to, to - from);
            if (!arrived) return;

            _arrived = true;
            _body.StopWalking();
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
