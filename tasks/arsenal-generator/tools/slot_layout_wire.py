"""Одноразовая правка: представление слота из ассета ArsenalSlotLayout (оружие или умолчание слота), коробка приёма."""
import io

def edit(p, reps):
    s = io.open(p, encoding='utf-8').read()
    for a, b in reps:
        assert s.count(a) == 1, (p, a[:90])
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8').write(s)

A = 'Assets/Scripts/Arsenal/'
edit(A + 'ArsenalSlotController.cs', [
("""        public float SlotWidth => _slotWidth;""",
 """        public float SlotWidth => _slotWidth;
        [Tooltip("Раскладка слота по умолчанию: как лежит оружие без своей раскладки для этого вида слота.")]
        [SerializeField] private ArsenalSlotLayout _defaultLayout;
        public ArsenalSlotLayout DefaultLayout => _defaultLayout;"""),
])

edit(A + 'ArsenalPresentationResolver.cs', [
("""    /// <summary>
    ///     Представление одного слота: раскладка оружия для вида слота и внешний вид арсенала. Неизменно после
    ///     разрешения; слот читает его через <see cref="ArsenalStationPresetBinding" />.
    /// </summary>
    public sealed class ArsenalPresentationSnapshot
    {
        public readonly ArsenalPresentationStyle Style;
        public readonly ArsenalPresentationPose ItemTarget, MagazineTarget, CardTarget;
        public readonly Vector2 CardSize;
        public readonly float CardFontSize;
        private readonly ArsenalSupportPose[] _supports;
        public IReadOnlyList<ArsenalSupportPose> Supports => Array.AsReadOnly(_supports);
        internal ArsenalPresentationSnapshot(ArsenalPresentationStyle style, ArsenalWeaponSlotLayout layout)
        {
            Style = style; ItemTarget = layout.ItemTarget; MagazineTarget = layout.MagazineTarget; CardTarget = layout.CardTarget;
            CardSize = layout.CardSize; CardFontSize = layout.CardFontSize;
            _supports = layout.Supports != null ? layout.Supports.ToArray() : Array.Empty<ArsenalSupportPose>();
        }
    }""",
 """    /// <summary>
    ///     Представление одного слота: раскладка слота (своя у оружия или умолчание слота) и внешний вид арсенала.
    ///     Неизменно после разрешения; слот читает его через <see cref="ArsenalStationPresetBinding" />.
    /// </summary>
    public sealed class ArsenalPresentationSnapshot
    {
        public readonly ArsenalPresentationStyle Style;
        /// <summary>Ассет раскладки, из которого собрано представление.</summary>
        public readonly ArsenalSlotLayout Layout;
        /// <summary>Раскладка принесена оружием; ложь — умолчание слота.</summary>
        public readonly bool FromWeapon;
        public readonly ArsenalPresentationPose ItemTarget, MagazineTarget, CardTarget;
        public readonly Vector2 CardSize;
        public readonly float CardFontSize;
        public readonly ArsenalPlaceZone PlaceZone;
        private readonly ArsenalSupportPose[] _supports;
        public IReadOnlyList<ArsenalSupportPose> Supports => Array.AsReadOnly(_supports);
        internal ArsenalPresentationSnapshot(ArsenalPresentationStyle style, ArsenalSlotLayout layout, bool fromWeapon)
        {
            Style = style; Layout = layout; FromWeapon = fromWeapon;
            ItemTarget = layout.ItemTarget; MagazineTarget = layout.MagazineTarget; CardTarget = layout.CardTarget;
            CardSize = layout.CardSize; CardFontSize = layout.CardFontSize; PlaceZone = layout.PlaceZone;
            _supports = layout.Supports != null ? layout.Supports.ToArray() : Array.Empty<ArsenalSupportPose>();
        }
    }"""),
("""    /// <summary>Разрешение представления слота: раскладка оружия для вида слота плюс внешний вид арсенала.</summary>
    public static class ArsenalPresentationResolver
    {
        public static ArsenalPresentationSnapshot Resolve(WeaponInfo weapon, ArsenalPresentationZone slotKind, ArsenalPresentationStyle style)
        {
            if (weapon == null || style == null) throw new InvalidOperationException("Представление требует оружие и стиль арсенала.");
            if (!weapon.TryGetSlotLayout(slotKind, out ArsenalWeaponSlotLayout layout))
                throw new InvalidOperationException("У оружия " + weapon.WeaponId + " нет раскладки для слота " + slotKind + ".");
            ValidatePose(layout.ItemTarget); ValidatePose(layout.MagazineTarget); ValidatePose(layout.CardTarget);
            ValidateCard(layout.CardSize, layout.CardFontSize); ValidateSupports(layout.Supports);
            if (layout.Supports != null && layout.Supports.Count != 0 && (style.SupportModule == null || style.ReturnReadyMaterial == null))
                throw new InvalidOperationException("Опоры раскладки " + weapon.WeaponId + " требуют модуль опор и материал подсказки в стиле арсенала.");
            ValidateStyle(style);
            ValidatePrefabAlignment(weapon.WeaponPrefab);
            ValidatePrefabAlignment(weapon.MagazinePrefab);
            return new ArsenalPresentationSnapshot(style, layout);
        }""",
 """    /// <summary>
    ///     Разрешение представления слота: своя раскладка оружия для вида слота, иначе раскладка слота по умолчанию;
    ///     плюс внешний вид арсенала.
    /// </summary>
    public static class ArsenalPresentationResolver
    {
        public static ArsenalPresentationSnapshot Resolve(WeaponInfo weapon, ArsenalPresentationZone slotKind, ArsenalPresentationStyle style,
            ArsenalSlotLayout slotDefault)
        {
            if (weapon == null || style == null) throw new InvalidOperationException("Представление требует оружие и стиль арсенала.");
            bool fromWeapon = weapon.TryGetSlotLayout(slotKind, out ArsenalSlotLayout layout);
            if (!fromWeapon) layout = slotDefault;
            if (layout == null)
                throw new InvalidOperationException("Нет раскладки для слота " + slotKind + ": ни у оружия " + weapon.WeaponId + ", ни по умолчанию у слота.");
            if (layout.SlotKind != slotKind)
                throw new InvalidOperationException("Раскладка " + layout.name + " для слота " + layout.SlotKind + ", а слот " + slotKind + ".");
            ValidatePose(layout.ItemTarget); ValidatePose(layout.MagazineTarget); ValidatePose(layout.CardTarget);
            ValidateCard(layout.CardSize, layout.CardFontSize); ValidateSupports(layout.Supports);
            if (layout.Supports != null && layout.Supports.Count != 0 && (style.SupportModule == null || style.ReturnReadyMaterial == null))
                throw new InvalidOperationException("Опоры раскладки " + layout.name + " требуют модуль опор и материал подсказки в стиле арсенала.");
            ValidateStyle(style);
            ValidatePrefabAlignment(weapon.WeaponPrefab);
            ValidatePrefabAlignment(weapon.MagazinePrefab);
            return new ArsenalPresentationSnapshot(style, layout, fromWeapon);
        }"""),
])

edit(A + 'ArsenalStationComposer.cs', [
("""                    try { presentation = ArsenalPresentationResolver.Resolve(manifest.Entry.WeaponResource, row.Zone, description.Style); }""",
 """                    try { presentation = ArsenalPresentationResolver.Resolve(manifest.Entry.WeaponResource, row.Zone, description.Style, row.SlotPrefab.DefaultLayout); }"""),
("""                    // Раскладка оружия — для вида слота этого ряда; внешний вид — стиль арсенала.""",
 """                    // Раскладка слота — своя у оружия для вида слота этого ряда, иначе умолчание префаба слота;
                    // внешний вид — стиль арсенала."""),
])

edit(A + 'ArsenalSlotBuilder.cs', [
("""            firearm.ConfigurePresentationZone(slotKind);
            ArsenalSupportProjection.MaterializePresentation(firearm, presentation);""",
 """            firearm.ConfigurePresentationZone(slotKind);
            ArsenalSupportProjection.MaterializePresentation(firearm, presentation);

            // Коробка приёма оружия — из раскладки; не задана — приём сферой якоря как есть.
            if (presentation.PlaceZone.IsSet)
            {
                var zone = firearm.ItemAnchor.GetComponent<ArsenalAnchorPlaceZone>();
                if (zone == null) zone = firearm.ItemAnchor.gameObject.AddComponent<ArsenalAnchorPlaceZone>();
                zone.Configure(firearm.transform, presentation.PlaceZone);
            }"""),
("""    ///     Сборщик одного слота станции: префаб слота + оружие (карточка, раскладка оружия для вида слота) и внешний
    ///     вид арсенала → один настроенный слот. Где слот будет стоять и кто его соседи, не знает: это работа ряда
    ///     (<see cref="ArsenalSlotRow" />).""",
 """    ///     Сборщик одного слота станции: префаб слота + оружие (карточка, раскладка слота) и внешний вид арсенала →
    ///     один настроенный слот: позы оружия, магазина и карточки, опоры, коробка приёма. Где слот будет стоять и
    ///     кто его соседи, не знает: это работа ряда (<see cref="ArsenalSlotRow" />)."""),
])

edit(A + 'ArsenalStationResolver.cs', [
("""                    foreach(var p in e.WeaponResource.SlotLayouts.OrderBy(l=>(int)l.SlotKind))
                    {
                        hash.Add((int)p.SlotKind);hash.Add(p.ItemTarget);hash.Add(p.MagazineTarget);hash.Add(p.CardTarget);
                        hash.Add(p.CardSize.x);hash.Add(p.CardSize.y);hash.Add(p.CardFontSize);
                        foreach(var s in p.Supports){hash.Add(s.Role);hash.Add((int)s.AnchorKind);hash.Add(s.SlotPose);}
                    }""",
 """                    // Свои раскладки оружия; умолчания слотов — часть префабов, одинаковых на всех машинах.
                    foreach(var p in e.WeaponResource.SlotLayouts.Where(l=>l!=null).OrderBy(l=>(int)l.SlotKind))
                    {
                        hash.Add(p.name);hash.Add((int)p.SlotKind);hash.Add(p.ItemTarget);hash.Add(p.MagazineTarget);hash.Add(p.CardTarget);
                        hash.Add(p.CardSize.x);hash.Add(p.CardSize.y);hash.Add(p.CardFontSize);
                        foreach(var s in p.Supports){hash.Add(s.Role);hash.Add((int)s.AnchorKind);hash.Add(s.SlotPose);}
                        hash.Add(p.PlaceZone.Center);hash.Add(p.PlaceZone.Size);hash.Add(p.PlaceZone.EulerAngles);
                    }"""),
])
print('ok')
