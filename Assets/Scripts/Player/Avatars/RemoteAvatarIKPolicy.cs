namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Решение «считать ли IK чужого аватара в этом кадре» — только логика, без Unity.
    /// Применяет его <see cref="RemoteAvatarIKThrottle"/> через хук SDK
    /// <c>UxrStandardAvatarController.ShouldSolveRemoteAvatarThisFrame</c> (патч 24).
    ///
    /// <para>
    /// <b>Зачем.</b> UltimateXR каждый кадр решает IK тела и рук у всех аватаров, включая
    /// чужих игроков за стеной и за спиной: на Quest 3 с девятью чужими это ~1,4 мс. Кого не
    /// видно, тому достаточно позы «примерно»: кисти и голову по-прежнему каждый кадр ставит
    /// сеть (NetworkTransform), отстают только плечи, предплечья и корпус.
    /// </para>
    ///
    /// <para>
    /// <b>Где нельзя.</b> На машине, где живёт авторитет, — выделенный сервер, хост, игра без
    /// сети: там урон считается по позам (<c>UxrActor</c> вычитает жизнь только у владельца
    /// сессии), и позы должны быть точными. Свой аватар — никогда.
    /// </para>
    /// </summary>
    public static class RemoteAvatarIKPolicy
    {
        /// <summary>
        /// Невидимый чужой аватар решается раз в столько кадров. На 90 Гц это ~22 решения в
        /// секунду; отставание костей рук — не больше трёх кадров (~33 мс).
        /// </summary>
        public const int InvisibleSolveInterval = 4;

        /// <summary>
        /// Экономить ли на этой машине нельзя: сервер (выделенный или хост), сборка без графики
        /// или сети нет вовсе. Экономия разрешена только чистому клиенту.
        /// </summary>
        public static bool IsAuthorityMachine(bool isBatchMode, bool serverActive, bool clientActive)
        {
            return isBatchMode || serverActive || !clientActive;
        }

        /// <summary>
        /// Решать ли IK аватара в кадре <paramref name="frame"/>.
        /// </summary>
        /// <param name="isRemote">Аватар чужой (<c>UxrAvatarMode.UpdateExternally</c>).</param>
        /// <param name="isVisible">Хоть один рендерер тела виден (по отрисовке прошлого кадра).</param>
        /// <param name="wasVisible">Был виден при прошлой проверке.</param>
        /// <param name="frame">Номер кадра.</param>
        /// <param name="stagger">Сдвиг аватара — чтобы невидимые решались в разных кадрах, а не пачкой.</param>
        /// <param name="isAuthorityMachine">См. <see cref="IsAuthorityMachine"/>.</param>
        /// <param name="interval">Раз в сколько кадров решать невидимого; ≤ 1 — каждый кадр.</param>
        public static bool ShouldSolve(bool isRemote, bool isVisible, bool wasVisible, int frame, int stagger,
            bool isAuthorityMachine, int interval)
        {
            if (!isRemote) return true;
            if (isAuthorityMachine) return true;

            // Видимый — каждый кадр. Сюда же попадает «только что появился»: решается сразу,
            // не дожидаясь своего кадра.
            if (isVisible) return true;

            // Только что пропал из вида: ещё одно решение. isVisible отстаёт на кадр, и
            // резкий поворот головы туда-обратно не должен показать застывшие руки.
            if (wasVisible) return true;

            if (interval <= 1) return true;

            return PositiveModulo((long)frame + stagger, interval) == 0;
        }

        private static long PositiveModulo(long value, int modulus)
        {
            long r = value % modulus;
            return r < 0 ? r + modulus : r;
        }
    }
}
