namespace UltimateXR.Mechanics.Weapons
{
    /// <summary>
    /// Политика отдачи на корневом grabbable огнестрела. Любой запрет исключает только запись позы корня.
    /// Политика учитывается и у выключенного компонента; чтение свойства не должно менять состояние.
    /// Отсутствие политик сохраняет штатную отдачу SDK.
    /// </summary>
    public interface IUxrFirearmRootRecoilPolicy
    {
        /// <summary>Разрешена ли отдача положения и вращения корня огнестрела.</summary>
        bool AllowsRootRecoil { get; }
    }
}
