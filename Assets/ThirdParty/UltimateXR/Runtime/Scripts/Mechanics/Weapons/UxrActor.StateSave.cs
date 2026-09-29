// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrActor.StateSave.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch (Патч 29, Docs/UltimateXR/sdk-patches.md, T-34).
using UltimateXR.Core.StateSave;

namespace UltimateXR.Mechanics.Weapons
{
    public partial class UxrActor
    {
        #region Protected Overrides UxrComponent

        /// <summary>
        ///     Жизнь входит в снимок состояния. В оригинальном SDK <c>Life</c> синхронизировался только
        ///     событием изменения, а <c>SerializeState</c> у актора не было вовсе — UltimateXR такой компонент
        ///     даже не регистрирует для сохранения. Клиент, получивший снимок позже изменения (вход посреди
        ///     матча, переподключение, смена карты), видел жизнь из префаба: раненых целыми, мёртвых живыми.
        ///
        ///     Как у <c>UxrFirearmMag</c>: в инкрементальных снимках не пишется — там жизнь едет событием.
        ///     Чтение меняет только поле: <c>Died</c>, анимация и звук не поднимаются — смерть была до получателя.
        /// </summary>
        protected override void SerializeState(bool isReading, int stateSerializationVersion, UxrStateSaveLevel level, UxrStateSaveOptions options)
        {
            base.SerializeState(isReading, stateSerializationVersion, level, options);

            if (level > UxrStateSaveLevel.ChangesSincePreviousSave)
            {
                SerializeStateValue(level, options, nameof(_life), ref _life);
            }
        }

        #endregion
    }
}
