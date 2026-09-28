using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Networking.Integrations.Net.Mirror;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Голова своего аватара видна зеркалам, но не своей камере.
    ///
    /// <para>
    /// <b>Зачем.</b> Камера стоит в глазах модели, то есть внутри головы, и UltimateXR прячет
    /// меши головы своего аватара: <c>UxrMirrorAvatar.InitializeNetworkAvatar</c> выключает
    /// объекты из <c>Local Disabled Game Objects</c>. Выключенный объект не рисует никто — в
    /// зеркале игрок стоял без головы. Здесь те же объекты включаются обратно и переносятся на
    /// слой <see cref="LayerName"/>, который вычеркнут из маски своей камеры. Камера зеркала
    /// (<c>UxrPlanarReflectionUrp</c>) рисует все слои, кроме Water, — голова в отражении есть.
    /// Камеры зрителей и чужих машин слой тоже рисуют.
    /// </para>
    ///
    /// <para>
    /// <b>Как попадает на аватар.</b> Как <see cref="RemoteAvatarIKThrottle"/>: сам, по
    /// <c>UxrAvatar.GlobalEnabled</c>, без правки префабов, — только туда, где список головы не
    /// пуст. Список заполняет <c>/setup-avatar</c> (раздел 3а).
    /// </para>
    ///
    /// <para>
    /// <b>Когда.</b> SDK выключает голову при инициализации и повторно при смене владельца,
    /// оба раза вместе с переводом аватара в <c>UxrAvatarMode.Local</c>. Поэтому решение
    /// принимается по смене режима в <c>LateUpdate</c>: стал своим — показать зеркалам, стал
    /// чужим — вернуть слои как были.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalHeadMirrorVisibility : MonoBehaviour
    {
        /// <summary>Слой головы своего аватара. Заведён в Tags and Layers.</summary>
        public const string LayerName = "LocalHead";

        private readonly Dictionary<GameObject, int> _originalLayers = new Dictionary<GameObject, int>();
        private bool _shownToMirrorsOnly;
        private int _removedCameraBit;
        private static bool s_loggedMissingLayer;

        private UxrAvatar _avatar;

        // Лениво, а не в Awake: тесты добавляют компонент в редакторе, где Awake не вызывается.
        private UxrAvatar Avatar => _avatar != null ? _avatar : _avatar = GetComponent<UxrAvatar>();

        private Camera OwnCamera => Avatar != null ? Avatar.CameraComponent : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            UxrAvatar.GlobalEnabled -= OnAvatarEnabled;
            if (Application.isBatchMode) return;

            UxrAvatar.GlobalEnabled += OnAvatarEnabled;
        }

        private static void OnAvatarEnabled(UxrAvatar avatar)
        {
            // Подписка статическая: без перезагрузки домена она пережила бы выход из Play Mode.
            if (!Application.isPlaying) return;
            if (avatar == null || avatar.GetComponent<LocalHeadMirrorVisibility>() != null) return;

            UxrMirrorAvatar net = avatar.GetComponent<UxrMirrorAvatar>();
            if (net == null || net.LocalDisabledGameObjects == null || net.LocalDisabledGameObjects.Count == 0) return;

            avatar.gameObject.AddComponent<LocalHeadMirrorVisibility>();
        }

        private void LateUpdate()
        {
            bool isLocal = Avatar != null && Avatar.AvatarMode == UxrAvatarMode.Local;

            if (isLocal == _shownToMirrorsOnly) return;

            if (isLocal)
                ShowToMirrorsOnly();
            else
                Restore();
        }

        /// <summary>
        /// Включает голову на слое <see cref="LayerName"/> и вычёркивает слой из маски своей
        /// камеры. Нет слоя в проекте — голова остаётся выключенной, как у SDK.
        /// </summary>
        public void ShowToMirrorsOnly()
        {
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer < 0)
            {
                if (!s_loggedMissingLayer)
                {
                    s_loggedMissingLayer = true;
                    GameLog.Player.Error($"[LocalHeadMirrorVisibility] Нет слоя '{LayerName}' — голова своего аватара в зеркале не видна.", this);
                }

                return;
            }

            if (_shownToMirrorsOnly) return;

            foreach (GameObject part in HeadParts())
            {
                foreach (Transform t in part.GetComponentsInChildren<Transform>(true))
                {
                    if (!_originalLayers.ContainsKey(t.gameObject)) _originalLayers.Add(t.gameObject, t.gameObject.layer);
                    t.gameObject.layer = layer;
                }

                part.SetActive(true);
            }

            Camera cam = OwnCamera;
            if (cam != null)
            {
                _removedCameraBit = cam.cullingMask & (1 << layer);
                cam.cullingMask &= ~(1 << layer);
            }

            _shownToMirrorsOnly = true;
            GameLog.Player.Verbose($"[LocalHeadMirrorVisibility] '{name}': голова своего аватара — только для зеркал ({_originalLayers.Count} объектов).", this);
        }

        /// <summary>
        /// Возвращает голове исходные слои, а камере — маску. Голова остаётся включённой:
        /// чужой аватар показывает её всем.
        /// </summary>
        public void Restore()
        {
            if (!_shownToMirrorsOnly) return;

            foreach (KeyValuePair<GameObject, int> pair in _originalLayers)
            {
                if (pair.Key != null) pair.Key.layer = pair.Value;
            }

            _originalLayers.Clear();

            Camera cam = OwnCamera;
            if (cam != null) cam.cullingMask |= _removedCameraBit;

            _removedCameraBit = 0;
            _shownToMirrorsOnly = false;
        }

        private IEnumerable<GameObject> HeadParts()
        {
            UxrMirrorAvatar net = GetComponent<UxrMirrorAvatar>();
            if (net == null || net.LocalDisabledGameObjects == null) yield break;

            foreach (GameObject part in net.LocalDisabledGameObjects)
            {
                if (part != null) yield return part;
            }
        }
    }
}
