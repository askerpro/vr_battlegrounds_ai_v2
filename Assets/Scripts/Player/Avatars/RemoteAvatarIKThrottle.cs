using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// «Режим экономии» IK для чужих аватаров, которых не видно: тело и руки решаются раз в
    /// <see cref="RemoteAvatarIKPolicy.InvisibleSolveInterval"/> кадров со сдвигом по аватару,
    /// а при появлении в кадре — сразу. Решение — <see cref="RemoteAvatarIKPolicy.ShouldSolve"/>,
    /// точка входа в SDK — хук <see cref="UxrStandardAvatarController.ShouldSolveRemoteAvatarThisFrame"/>
    /// (патч 24 UltimateXR).
    ///
    /// <para>
    /// <b>Как попадает на аватар.</b> Как и <see cref="RemoteAvatarRenderOptimizer"/>: сам, по
    /// <c>UxrAvatar.GlobalEnabled</c>, без правки префабов. Компонент есть и у своего аватара —
    /// SDK для локального хук не спрашивает, а политика всё равно ответит «решать».
    /// </para>
    ///
    /// <para>
    /// <b>Видимость.</b> Хоть один рендерер тела (<see cref="RemoteAvatarRenderPolicy.CollectBodyRenderers"/>
    /// — без оружия и предметов) с <see cref="Renderer.isVisible"/>. Флаг ставит отрисовка
    /// прошлого кадра, поэтому появление замечается с опозданием в кадр: в первом кадре
    /// видна поза не старше трёх кадров. Рендереров нет — аватар считается видимым.
    /// </para>
    ///
    /// <para>
    /// На сервере без графики (<c>-batchmode</c>) не ставится вовсе, а на хосте и без сети
    /// политика отвечает «решать каждый кадр»: урон считается по позам владельца сессии.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RemoteAvatarIKThrottle : MonoBehaviour
    {
        private static int s_nextStagger;

        private UxrAvatar _avatar;
        private Renderer[] _renderers;
        private int _stagger;
        private bool _wasVisible = true;
        private int _lastFrame = -1;
        private bool _lastDecision = true;
        private bool _loggedThrottle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            UxrAvatar.GlobalEnabled -= OnAvatarEnabled;
            UxrStandardAvatarController.ShouldSolveRemoteAvatarThisFrame = null;
            if (Application.isBatchMode) return;

            UxrAvatar.GlobalEnabled += OnAvatarEnabled;
            UxrStandardAvatarController.ShouldSolveRemoteAvatarThisFrame = ShouldSolveHook;
        }

        private static void OnAvatarEnabled(UxrAvatar avatar)
        {
            // Подписка статическая: без перезагрузки домена она пережила бы выход из Play Mode.
            if (!Application.isPlaying) return;
            if (avatar == null) return;
            if (avatar.GetComponent<RemoteAvatarIKThrottle>() != null) return;

            avatar.gameObject.AddComponent<RemoteAvatarIKThrottle>();
        }

        /// <summary>Хук SDK: вызывается в PostUpdate для каждого чужого аватара раз в кадр.</summary>
        private static bool ShouldSolveHook(UxrAvatar avatar)
        {
            if (!Application.isPlaying) return true;
            if (avatar == null || !avatar.TryGetComponent(out RemoteAvatarIKThrottle throttle)) return true;

            return throttle.Decide(Time.frameCount);
        }

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();

            List<Renderer> renderers = RemoteAvatarRenderPolicy.CollectBodyRenderers(transform);
            _renderers = renderers.ToArray();

            _stagger = s_nextStagger;
            s_nextStagger = (s_nextStagger + 1) % RemoteAvatarIKPolicy.InvisibleSolveInterval;
        }

        private bool Decide(int frame)
        {
            // Повторный вопрос в том же кадре не должен сдвигать «был виден».
            if (frame == _lastFrame) return _lastDecision;

            bool isVisible = IsAnyRendererVisible();
            bool isAuthority = RemoteAvatarIKPolicy.IsAuthorityMachine(
                Application.isBatchMode, NetworkServer.active, NetworkClient.active);

            bool decision = RemoteAvatarIKPolicy.ShouldSolve(
                _avatar != null && _avatar.AvatarMode == UxrAvatarMode.UpdateExternally,
                isVisible, _wasVisible, frame, _stagger, isAuthority,
                RemoteAvatarIKPolicy.InvisibleSolveInterval);

            if (!decision && !_loggedThrottle)
            {
                _loggedThrottle = true;
                GameLog.Player.Verbose(
                    $"[RemoteAvatarIKThrottle] '{name}': чужой аватар вне кадра — IK раз в " +
                    $"{RemoteAvatarIKPolicy.InvisibleSolveInterval} кадра (сдвиг {_stagger}, рендереров {_renderers.Length}).",
                    this);
            }

            _wasVisible = isVisible;
            _lastFrame = frame;
            _lastDecision = decision;
            return decision;
        }

        private bool IsAnyRendererVisible()
        {
            if (_renderers == null || _renderers.Length == 0) return true;

            bool anyAlive = false;

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;

                anyAlive = true;
                if (r.isVisible) return true;
            }

            // Все рендереры уничтожены — судить не по чему, решаем каждый кадр.
            return !anyAlive;
        }
    }
}
