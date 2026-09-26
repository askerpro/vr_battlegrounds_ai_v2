using Mirror;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Удаляет предмет, выпавший за пределы мира (ниже <see cref="_killY" />).
    ///
    /// <para>
    /// Страховка от любой дыры в коллизиях (PHY-01). Предмет, прошедший сквозь пол, падает
    /// вечно: rigidbody не засыпает, и <c>UxrGrabbableObject.RegularPhysicsSyncCoroutine</c>
    /// у отпустившего игрока без конца рассылает <c>UpdateRigidbody</c> по сети.
    /// Удаление объекта останавливает и падение, и рассылку.
    /// </para>
    ///
    /// <para>
    /// Решает сервер: в сети предмет удаляется через <see cref="NetworkServer.Destroy" />, и
    /// удаление расходится на клиенты. Сервер видит падение и тогда, когда физику считает
    /// клиент, — позиция приходит ему теми же <c>UpdateRigidbody</c>. Чистый клиент ничего не
    /// делает и ждёт сервера. Вне сессии предмет удаляется локально.
    /// </para>
    ///
    /// <para>
    /// Исключение — предмет без собственного <c>netId</c>: встроенный магазин
    /// <c>Machinegun</c>/<c>Shotgun</c>/<c>M16</c> не спавнится отдельно, и сетевое удаление до
    /// клиентов не дойдёт. Такой предмет каждая машина удаляет у себя сама.
    /// </para>
    /// </summary>
    public sealed class OutOfWorldGuard : MonoBehaviour
    {
        [Tooltip("Высота, ниже которой предмет считается выпавшим из мира. Карты стоят на Y≈0.")]
        [SerializeField] private float _killY = -50f;

        public float KillY => _killY;

        private UxrGrabbableObject _grabbable;
        private NetworkIdentity    _identity;
        private bool               _removed;

        private void Awake()
        {
            _grabbable = GetComponent<UxrGrabbableObject>();
            _identity  = GetComponent<NetworkIdentity>();
        }

        private void FixedUpdate()
        {
            CheckNow();
        }

        /// <summary>
        /// Проверяет высоту и удаляет предмет, если он за пределами мира.
        /// Публичный — чтобы EditMode-тест мог вызвать проверку без игрового цикла.
        /// </summary>
        /// <returns>true, если предмет удалён</returns>
        public bool CheckNow()
        {
            if (_removed || transform.position.y >= _killY) return false;

            if (_grabbable == null) Awake();

            bool grabbed     = _grabbable != null && UxrGrabManager.HasInstance && _grabbable.IsBeingGrabbed;
            bool hasOwnNetId = _identity != null && _identity.netId != 0;

            Removal removal = Decide(grabbed, NetworkServer.active, NetworkClient.active, hasOwnNetId);
            if (removal == Removal.Keep || removal == Removal.WaitForServer) return false;

            _removed = true;
            GameLog.WeaponSystem.Warning($"[OutOfWorldGuard] '{name}' выпал из мира (y={transform.position.y:F0} < {_killY:F0}) — {removal}.", this);

            if (removal == Removal.ServerDestroy)
                NetworkServer.Destroy(gameObject);
            else if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);

            return true;
        }

        /// <summary>Что делать с предметом, уже оказавшимся ниже порога.</summary>
        public enum Removal
        {
            /// <summary>Не трогать.</summary>
            Keep,
            /// <summary>Ничего не делать: удалит сервер, удаление приедет сообщением Mirror.</summary>
            WaitForServer,
            /// <summary><see cref="NetworkServer.Destroy" /> — удаление расходится на клиенты.</summary>
            ServerDestroy,
            /// <summary>Удалить только у себя.</summary>
            LocalDestroy
        }

        /// <summary>
        /// Выбор способа удаления. Чистая функция — чтобы сетевые ветки проверялись
        /// тестом без сессии Mirror.
        /// </summary>
        public static Removal Decide(bool grabbed, bool serverActive, bool clientActive, bool hasOwnNetId)
        {
            // Предмет в руке не трогаем: удаление ломает учёт UltimateXR у держащего игрока.
            // Выпал вместе с игроком — это уже другая беда, не этого компонента.
            if (grabbed) return Removal.Keep;

            // Без своего netId (встроенный в префаб оружия магазин, извлечённый рукой)
            // NetworkServer.Destroy до клиентов не дойдёт — каждая машина удаляет сама.
            // Копии не разойдутся: позиция синхронизирована, падение видят все.
            if (!hasOwnNetId) return Removal.LocalDestroy;

            // Чистый клиент ждёт решения сервера.
            if (clientActive && !serverActive) return Removal.WaitForServer;

            return serverActive ? Removal.ServerDestroy : Removal.LocalDestroy;
        }
    }
}
