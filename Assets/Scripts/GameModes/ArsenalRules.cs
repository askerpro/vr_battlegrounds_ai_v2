namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Что активный режим требует от стены арсенала прямо сейчас. Объявляет режим
    /// (<see cref="GameMode.ArsenalRules"/>), исполняет стена — и конкретных режимов
    /// при этом не знает: Elimination отвечает по фазе раунда, лобби — «открыт всегда».
    ///
    /// <para>
    /// Это состояние, а не событие: стена сверяется с ним каждый кадр. Поэтому поздний
    /// клиент, стена, проснувшаяся позже режима, и стена, которую закрыли извне, приходят
    /// к правильному виду сами, без отдельной раздачи. Разовое действие у стены одно —
    /// пополнение пустых слотов к новому раунду, — и оно идёт событием
    /// <see cref="GameMode.ArsenalRefillRequestedServer"/>: фаза <c>Setup</c> бывает
    /// короче кадра, и опрос её бы пропустил.
    /// </para>
    /// </summary>
    public readonly struct ArsenalRules
    {
        /// <summary>Стена открыта: слоты отдают и принимают оружие.</summary>
        public readonly bool IsOpen;

        /// <summary>Содержимое предоставлено игроку; доступ к покупке отдельно задаёт IsOpen.</summary>
        public readonly bool IsDeployed;

        /// <summary>На стене нужен жетон готовности к раунду.</summary>
        public readonly bool UsesReadinessTag;

        /// <summary>
        /// Слот, чьё оружие пропало совсем (уничтожено, выпало из мира), получает новое
        /// спустя задержку стены. Унесённое оружие слот ждёт обратно.
        /// </summary>
        public readonly bool ReplacesLostWeapons;

        public ArsenalRules(bool isOpen, bool usesReadinessTag, bool replacesLostWeapons, bool isDeployed = true)
        {
            IsOpen = isOpen;
            IsDeployed = isDeployed;
            UsesReadinessTag = usesReadinessTag;
            ReplacesLostWeapons = replacesLostWeapons;
        }

        /// <summary>Закрыта, без жетона, без замены — правило по умолчанию.</summary>
        public static ArsenalRules Closed => new ArsenalRules(false, false, false, isDeployed: false);

        public override string ToString() =>
            $"открыт={IsOpen}, жетон={UsesReadinessTag}, замена пропавшего={ReplacesLostWeapons}";
    }
}
