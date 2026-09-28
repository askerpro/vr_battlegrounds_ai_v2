using System;
using UnityEngine;

namespace UltimateXR.Avatar.Controllers
{
    public sealed partial class UxrStandardAvatarController
    {
        #region Public Types & Data (VR Battlegrounds patch 24)

        /// <summary>
        ///     VR Battlegrounds patch 24: «режим экономии» IK чужих аватаров. Спрашивается в
        ///     <see cref="UpdateAvatarPostProcess" /> только для аватаров в режиме
        ///     <see cref="UxrAvatarMode.UpdateExternally" />: вернул false — тело и руки в этом кадре
        ///     не решаются, кости остаются в позе последнего решения. По умолчанию null — решается
        ///     каждый кадр, как в оригинальном SDK. Локальный аватар хук не видит никогда.
        ///     <para>
        ///         SDK не ссылается на код игры (зависимость обратная), поэтому политику — кто
        ///         невидим, в каком кадре решать, где живёт авторитет — ставит игра при старте
        ///         (<c>RemoteAvatarIKThrottle</c>). Исключение внутри хука не ломает кадр: аватар
        ///         решается, ошибка пишется один раз.
        ///     </para>
        /// </summary>
        public static Func<UxrAvatar, bool> ShouldSolveRemoteAvatarThisFrame { get; set; }

        #endregion

        #region Private Methods (VR Battlegrounds patch 24)

        /// <summary>
        ///     VR Battlegrounds patch 24: решать ли IK этого аватара в текущем кадре.
        ///     Локальный аватар, аватар без хука и отказ хука — всегда «решать».
        /// </summary>
        private bool ShouldSolveIKThisFrame()
        {
            Func<UxrAvatar, bool> hook   = ShouldSolveRemoteAvatarThisFrame;
            UxrAvatar             avatar = Avatar;

            if (hook == null || avatar == null || avatar.AvatarMode != UxrAvatarMode.UpdateExternally)
            {
                return true;
            }

            try
            {
                return hook(avatar);
            }
            catch (Exception e)
            {
                if (!s_loggedHookError)
                {
                    s_loggedHookError = true;
                    Debug.LogException(e);
                }

                return true;
            }
        }

        #endregion

        #region Private Types & Data (VR Battlegrounds patch 24)

        private static bool s_loggedHookError;

        #endregion
    }
}
