namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>
    /// Датчик магазина: смена объекта в гнезде спуска между замерами (как прежний контроллер, опросом после SDK —
    /// SDK к этому моменту уже зафиксировал MagazineChanged). Снимок состояния — новая точка отсчёта, а не вставка.
    /// </summary>
    internal sealed class WeaponMagazineSensor
    {
        private readonly WeaponLedgerReader _ledger;
        private int _observed;

        public WeaponMagazineSensor(WeaponLedgerReader ledger)
        {
            _ledger = ledger;
            Rebase();
        }

        /// <summary>Новая точка отсчёта (подключение, снимок): текущий магазин не считается сменой.</summary>
        public void Rebase()
        {
            var magazine = _ledger.CurrentAnchorMagazine();
            _observed = magazine != null ? magazine.GetInstanceID() : 0;
        }

        /// <summary>Магазин в гнезде сменился с прошлого замера.</summary>
        public bool Poll()
        {
            var magazine = _ledger.CurrentAnchorMagazine();
            int token = magazine != null ? magazine.GetInstanceID() : 0;
            if (token == _observed) return false;
            _observed = token;
            return true;
        }
    }
}
