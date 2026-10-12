// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrStateSyncImplementer.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Text;
using UltimateXR.Core.Settings;
using UnityEngine;

namespace UltimateXR.Core.StateSync
{
    /// <summary>
    ///     Base class for <see cref="UxrStateSyncImplementer{T}" />.
    /// </summary>
    /// <remarks>
    ///     VR Battlegrounds patch 74 (tasks/state-sync-depth-guard): глубина синхронизации — размер одной общей
    ///     LIFO-стопки кадров «владелец + опции» вместо статического счётчика и отдельного стека опций у каждого
    ///     компонента. EndSync/CancelSync снимает только свой кадр; кадры, оставленные над ним (исключение в теле
    ///     вложенного BeginSync), снимаются с ошибкой в логе. Оставшиеся к началу следующего кадра Unity кадры
    ///     снимает <see cref="UxrManager" /> (<see cref="ResetLeakedFrames" />). Формат и момент доставки событий
    ///     не меняются.
    /// </remarks>
    public class UxrStateSyncImplementer
    {
        #region Public Types & Data

        /// <summary>
        ///     <para>
        ///         Gets the current call depth of BeginSync/EndSync calls, which are responsible for helping synchronize calls
        ///         over the network.
        ///         To avoid redundant synchronization, nested calls (where <see cref="SyncCallDepth" /> is greater than 1),
        ///         need to be ignored.
        ///     </para>
        ///     <para>
        ///         State synchronization, for networking or other functionality like saving gameplay replays, can be done
        ///         by subscribing to <see cref="UxrManager.ComponentStateChanged" />. By default, only top level calls will
        ///         trigger the event. This can be changed using <see cref="UxrManager.UseTopLevelStateChangesOnly" />.
        ///     </para>
        ///     <para>
        ///         In the following code, only PlayerShoot() needs to be synchronized. This will not only save bandwidth, but also
        ///         make sure that only a single particle system gets instantiated and the shot audio doesn't get played twice.
        ///     </para>
        ///     <code>
        ///         void PlayerShoot(int parameter1, bool parameter2)
        ///         {
        ///             BeginSync();
        ///             ShowParticles(parameter1);
        ///             PlayAudioShot(parameter2);
        ///             EndSyncMethod(new object[] {parameter1, parameter2});
        ///         }
        ///
        ///         void ShowParticles(int parameter);
        ///         {
        ///             BeginSync();
        ///             Instantiate(ParticleSystem);
        ///             EndSyncMethod(new object[] {parameter});
        ///         }
        ///
        ///         void PlayAudioShot(bool parameter);
        ///         {
        ///             BeginSync();
        ///             _audio.Play();
        ///             EndSyncMethod(new object[] {parameter});
        ///         }
        ///     </code>
        /// </summary>
        public static int SyncCallDepth => s_frames.Count;

        /// <summary>
        ///     VR Battlegrounds patch 74: сколько раз сторож на границе кадра находил и снимал оставленные кадры.
        ///     Ноль в нормальной работе; рост означает исключение внутри BeginSync … EndSync.
        /// </summary>
        public static int LeakedFrameResetCount { get; private set; }

        #endregion

        #region Public Methods

        /// <summary>
        ///     VR Battlegrounds patch 74: снимает кадры синхронизации, оставшиеся открытыми к точке, где открытых
        ///     кадров быть не может (граница кадра Unity, вне исполнения принятого события). Синхронизация
        ///     синхронна и не переживает кадр, поэтому любой такой кадр — утечка после исключения в теле
        ///     BeginSync … EndSync. Без сброса все дальнейшие события процесса считались бы вложенными и не уходили бы
        ///     в сеть. События не отправляются и не повторяются.
        /// </summary>
        /// <param name="checkpoint">Где выполнена проверка — для лога.</param>
        /// <returns>Сколько кадров снято.</returns>
        public static int ResetLeakedFrames(string checkpoint)
        {
            int count = s_frames.Count;

            if (count == 0)
            {
                return 0;
            }

            LeakedFrameResetCount++;

            if (UxrGlobalSettings.Instance.LogLevelCore >= UxrLogLevel.Errors)
            {
                Debug.LogError($"{UxrConstants.CoreModule} {checkpoint}: {count} StateSync frame(s) left open (BeginSync without EndSync/CancelSync, " +
                               $"most likely an exception inside the sync block). Cleared to restore top-level sync. Open frames, bottom to top: {DescribeFrames(0)}");
            }

            s_frames.Clear();
            return count;
        }

        #endregion

        #region Protected Types & Data

        /// <summary>
        ///     Описание владельца кадра для логов.
        /// </summary>
        protected virtual string FrameOwnerDescription => GetType().Name;

        #endregion

        #region Protected Methods

        /// <summary>
        ///     Открывает кадр этого реализатора.
        /// </summary>
        /// <param name="options">Опции синхронизации кадра.</param>
        protected void PushFrame(UxrStateSyncOptions options)
        {
            s_frames.Add(new SyncFrame(this, options));
        }

        /// <summary>
        ///     Находит верхний кадр этого реализатора и снимает чужие кадры, оставленные над ним.
        /// </summary>
        /// <param name="operation">Операция — для лога.</param>
        /// <returns>Индекс своего кадра (он верхний) или -1, если своего кадра нет.</returns>
        protected int TakeOwnFrame(string operation)
        {
            int index = FindOwnFrame();

            if (index >= 0)
            {
                DropFramesAbove(index, operation);
            }

            return index;
        }

        /// <summary>
        ///     Опции кадра по индексу из <see cref="TakeOwnFrame" />.
        /// </summary>
        protected static UxrStateSyncOptions FrameOptions(int index)
        {
            return s_frames[index].Options;
        }

        /// <summary>
        ///     Закрывает свой кадр. Кадры, оставленные над ним во время события (исключение в подписчике после
        ///     вложенного BeginSync), снимаются с ошибкой в логе.
        /// </summary>
        /// <param name="index">Индекс своего кадра из <see cref="TakeOwnFrame" />.</param>
        /// <param name="operation">Операция — для лога.</param>
        protected void CloseOwnFrame(int index, string operation)
        {
            if (index >= s_frames.Count || !ReferenceEquals(s_frames[index].Owner, this))
            {
                // Кадр уже снят (сторож или чужой разбор). Ищем свой заново, ничего чужого не трогаем.
                index = FindOwnFrame();

                if (index < 0)
                {
                    return;
                }
            }

            DropFramesAbove(index, operation);
            s_frames.RemoveAt(index);
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Сбрасывает стопку при входе в Play Mode без перезагрузки домена.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            s_frames.Clear();
            LeakedFrameResetCount = 0;
        }

        private int FindOwnFrame()
        {
            for (int i = s_frames.Count - 1; i >= 0; --i)
            {
                if (ReferenceEquals(s_frames[i].Owner, this))
                {
                    return i;
                }
            }

            return -1;
        }

        private void DropFramesAbove(int index, string operation)
        {
            int leaked = s_frames.Count - 1 - index;

            if (leaked <= 0)
            {
                return;
            }

            if (UxrGlobalSettings.Instance.LogLevelCore >= UxrLogLevel.Errors)
            {
                Debug.LogError($"{UxrConstants.CoreModule} {operation} of {FrameOwnerDescription}: {leaked} nested StateSync frame(s) above it were left open " +
                               $"(BeginSync without EndSync/CancelSync). Dropped: {DescribeFrames(index + 1)}");
            }

            s_frames.RemoveRange(index + 1, leaked);
        }

        private static string DescribeFrames(int from)
        {
            var builder = new StringBuilder();

            for (int i = from; i < s_frames.Count; ++i)
            {
                if (builder.Length > 0)
                {
                    builder.Append(" > ");
                }

                UxrStateSyncImplementer owner = s_frames[i].Owner;
                builder.Append(owner != null ? owner.FrameOwnerDescription : "(null)");
            }

            return builder.ToString();
        }

        #endregion

        #region Private Types & Data

        private readonly struct SyncFrame
        {
            public SyncFrame(UxrStateSyncImplementer owner, UxrStateSyncOptions options)
            {
                Owner   = owner;
                Options = options;
            }

            public UxrStateSyncImplementer Owner   { get; }
            public UxrStateSyncOptions     Options { get; }
        }

        private static readonly List<SyncFrame> s_frames = new List<SyncFrame>();

        #endregion
    }
}
