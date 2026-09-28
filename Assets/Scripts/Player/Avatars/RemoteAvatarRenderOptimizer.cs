using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Держит отрисовку аватара в соответствии с его режимом: в
    /// <see cref="UxrAvatarMode.UpdateExternally"/> (чужой игрок) — облегчённая
    /// (<see cref="RemoteAvatarRenderPolicy"/>), в <see cref="UxrAvatarMode.Local"/> — как в
    /// префабе.
    ///
    /// <para>
    /// <b>Как попадает на аватар.</b> Сам, без правки префабов: при загрузке подписывается
    /// на типизированное <c>UxrAvatar.GlobalEnabled</c> и добавляется к каждому
    /// <see cref="UxrAvatar"/> в его <c>OnEnable</c> — уже после <c>UxrAvatar.Awake</c>
    /// (он включает <c>updateWhenOffscreen</c>), но до первого кадра. Префабы аватаров —
    /// общий ресурс, и новый скин получает поведение без шага в <c>/setup-avatar</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Почему опрос режима, а не событие.</b> Режим меняет
    /// <c>UxrMirrorAvatar.InitializeNetworkAvatar</c>, причём повторная инициализация
    /// (владение сменилось, <c>OnStartLocalPlayer</c> после <c>OnStartClient</c>) не
    /// поднимает <c>AvatarSpawned</c>, а <c>UxrAvatar.LocalAvatarChanged</c> срабатывает не на
    /// каждую смену. Сравнение одного enum в <c>LateUpdate</c> ловит любой путь и успевает
    /// до отрисовки кадра, в котором режим сменился.
    /// </para>
    ///
    /// <para>
    /// На сервере без графики (<c>-batchmode</c>) не ставится: там нечего рисовать.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RemoteAvatarRenderOptimizer : MonoBehaviour
    {
        private UxrAvatar _avatar;
        private RemoteAvatarRenderSnapshot _snapshot;
        private bool _loggedApply;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            UxrAvatar.GlobalEnabled -= OnAvatarEnabled;
            if (Application.isBatchMode) return;

            UxrAvatar.GlobalEnabled += OnAvatarEnabled;
        }

        private static void OnAvatarEnabled(UxrAvatar avatar)
        {
            // Подписка статическая: без перезагрузки домена она пережила бы выход из Play Mode
            // и дописала бы компонент в аватар открытой сцены или префаба.
            if (!Application.isPlaying) return;
            if (avatar == null) return;
            if (avatar.GetComponent<RemoteAvatarRenderOptimizer>() != null) return;

            avatar.gameObject.AddComponent<RemoteAvatarRenderOptimizer>();
        }

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
            _snapshot = RemoteAvatarRenderSnapshot.Capture(RemoteAvatarRenderPolicy.CollectBodyRenderers(transform));
        }

        private void LateUpdate()
        {
            if (_avatar == null) return;

            bool shouldOptimize = RemoteAvatarRenderPolicy.ShouldOptimize(_avatar.AvatarMode);
            if (shouldOptimize == _snapshot.IsApplied) return;

            if (shouldOptimize)
            {
                _snapshot.ApplyRemote();

                if (!_loggedApply)
                {
                    _loggedApply = true;
                    GameLog.Player.Verbose(
                        $"[RemoteAvatarRenderOptimizer] '{name}': отрисовка чужого аватара облегчена — " +
                        $"рендереров {_snapshot.RendererCount} (скин {_snapshot.SkinCount}) без тени, " +
                        $"скин без offscreen-обновления и motion vectors, скрыто под снаряжением {_snapshot.HiddenCount}.",
                        this);
                }
            }
            else
            {
                _snapshot.Restore();
                GameLog.Player.Verbose(
                    $"[RemoteAvatarRenderOptimizer] '{name}': аватар стал локальным — настройки рендереров возвращены к префабным.",
                    this);
            }
        }
    }
}
