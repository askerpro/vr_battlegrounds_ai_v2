using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    public enum ArsenalCompositionFailureKind
    { InvalidInput, UnstyledPreset, DuplicateLogicalKey }
    public enum ArsenalDecorationFallback { Sized, Universal, Bare }
    public sealed class ArsenalCompositionFailure
    {
        public ArsenalCompositionFailureKind Kind { get; }
        public string StationKey { get; }
        public string LogicalSlotKey { get; }
        public string Detail { get; }
        public ArsenalCompositionFailure(ArsenalCompositionFailureKind kind, string station, string logical, string detail)
        { Kind=kind; StationKey=station; LogicalSlotKey=logical; Detail=detail; }
    }
    public readonly struct ArsenalVisualRequest
    {
        public string VisualSetId { get; }
        public string DecorationId { get; }
        public ArsenalVisualRequest(string set, string id=null) { VisualSetId=set??""; DecorationId=id??""; }
    }
    public readonly struct ArsenalPlacementInput
    {
        public ArsenalPresentationPose StationPose { get; }
        public Vector3 FrameScale { get; }
        public Bounds AvailableWorldBounds { get; }
        public Transform Zone { get; }
        public Transform StandingPoint { get; }
        public Transform ArenaFacing { get; }
        public ArsenalPlacementInput(ArsenalPresentationPose pose, Vector3 scale, Bounds bounds,
            Transform zone=null, Transform standing=null, Transform facing=null)
        { StationPose=pose; FrameScale=scale; AvailableWorldBounds=bounds; Zone=zone; StandingPoint=standing; ArenaFacing=facing; }
    }
    /// <summary>Значения card content; общий formatter будет принимать их при composer integration.</summary>
    public readonly struct ArsenalCardContent
    {
        public string DisplayName { get; }
        public int Price { get; }
        public bool HasBalance { get; }
        public int Pellets { get; }
        public float Damage { get; }
        public int MagazineSize { get; }
        public int Rpm { get; }
        public bool FullAuto { get; }
        public WeaponCategory Category { get; }
        public ArsenalCardContent(WeaponInfo weapon)
        { DisplayName=weapon.DisplayName; Price=weapon.Price; HasBalance=weapon.HasBalance; Pellets=weapon.Pellets;
            Damage=weapon.Damage; MagazineSize=weapon.MagazineSize; Rpm=WeaponInfo.ShotFrequency(weapon.FireRate)*60;
            FullAuto=weapon.FullAuto; Category=weapon.Category; }
    }
    /// <summary>Запись пресета: какое оружие, в какой ряд корпуса и что на его карточке.</summary>
    public sealed class ArsenalFrozenEntry
    {
        public int NetworkIndex { get; }
        public string LogicalSlotKey { get; }
        /// <summary>Ключ ряда корпуса (<see cref="ArsenalSlotRow.RowKey" />).</summary>
        public string RowKey { get; }
        public WeaponInfo WeaponResource { get; }
        public ArsenalCardContent Card { get; }
        public ArsenalFrozenEntry(int index, WeaponInfo weapon, string rowKey)
        { NetworkIndex=index; LogicalSlotKey=weapon.WeaponId; RowKey=rowKey??""; WeaponResource=weapon; Card=new ArsenalCardContent(weapon); }
    }
    /// <summary>Capture до run: defensive копии resources и source values, никаких runtime keys или Ready.</summary>
    public sealed class ArsenalStationBuildInput
    {
        public string StationKey { get; }
        public string PresetId { get; }
        public string CatalogId { get; }
        public int CompilerVersion { get; }
        /// <summary>Внешний вид арсенала: модуль опор и материал подсказки возврата.</summary>
        public ArsenalPresentationStyle Style { get; }
        public ArsenalVisualRequest Visual { get; }
        public ArsenalPlacementInput Placement { get; }
        public IReadOnlyList<ArsenalFrozenEntry> Entries { get; }
        public IReadOnlyList<ArsenalDecorationDescriptor> Decorations { get; }
        public IReadOnlyList<ArsenalSupportGeometry> Supports { get; }
        public IReadOnlyList<ArsenalCompositionFailure> Failures { get; }
        private ArsenalStationBuildInput(string station, string preset, string catalog, int version, ArsenalPresentationStyle style, ArsenalVisualRequest visual,
            ArsenalPlacementInput placement, IEnumerable<ArsenalFrozenEntry> entries,
            IEnumerable<ArsenalDecorationDescriptor> decorations, IEnumerable<ArsenalSupportGeometry> supports,
            IEnumerable<ArsenalCompositionFailure> failures)
        {
            StationKey=station; PresetId=preset; CatalogId=catalog; CompilerVersion=version; Style=style; Visual=visual; Placement=placement;
            Entries=Array.AsReadOnly(entries.ToArray());
            Decorations=Array.AsReadOnly(decorations.Select(d=>d.Freeze()).ToArray()); Supports=Array.AsReadOnly(supports.ToArray());
            Failures=Array.AsReadOnly(failures.ToArray());
        }
        public static ArsenalStationBuildInput Capture(string stationKey, ArsenalPreset preset, ArsenalCompositionCatalog catalog,
            ArsenalVisualRequest visual, ArsenalPlacementInput placement)
        {
            var errors=new List<ArsenalCompositionFailure>(); var entries=new List<ArsenalFrozenEntry>();
            Action<ArsenalCompositionFailureKind,string,string> fail=(kind,key,detail)=>errors.Add(new ArsenalCompositionFailure(kind,stationKey,key,detail));
            if(string.IsNullOrWhiteSpace(stationKey)||preset==null||catalog==null||string.IsNullOrWhiteSpace(preset.PresetId))
                fail(ArsenalCompositionFailureKind.InvalidInput,null,"Нужны StationKey, PresetId и catalog.");
            if(preset!=null&&preset.PresentationStyle==null)
                fail(ArsenalCompositionFailureKind.UnstyledPreset,null,"У пресета нет стиля арсенала (внешний вид станции).");
            if(preset!=null&&catalog!=null)
            {
                var keys=new HashSet<string>(StringComparer.Ordinal);
                for(int i=0;i<preset.Entries.Count;i++)
                {
                    var source=preset.Entries[i]; var weapon=source.Weapon;
                    if(weapon==null||string.IsNullOrWhiteSpace(weapon.WeaponId)) { fail(ArsenalCompositionFailureKind.InvalidInput,null,"Пустой WeaponInfo/WeaponId."); continue; }
                    if(!keys.Add(weapon.WeaponId)) { fail(ArsenalCompositionFailureKind.DuplicateLogicalKey,weapon.WeaponId,"Повтор WeaponId."); continue; }
                    if(string.IsNullOrWhiteSpace(source.Row)) { fail(ArsenalCompositionFailureKind.InvalidInput,weapon.WeaponId,"Запись пресета без ряда."); continue; }
                    // Позы оружия в слоте разрешает сборщик слота по виду ряда: ряд — часть корпуса на сцене.
                    entries.Add(new ArsenalFrozenEntry(i,weapon,source.Row));
                }
            }
            return new ArsenalStationBuildInput(stationKey,preset!=null?preset.PresetId:null,catalog!=null?catalog.CatalogId:null,
                catalog!=null?catalog.CompilerVersion:0,preset!=null?preset.PresentationStyle:null,visual,placement,entries,
                catalog!=null?catalog.Decorations:Array.Empty<ArsenalDecorationDescriptor>(),
                catalog!=null?catalog.Supports:Array.Empty<ArsenalSupportGeometry>(),errors);
        }
    }
    /// <summary>
    ///     Слот в порядке станции: что в нём и каким по счёту он стоит в своём ряду. Геометрии нет — где ряд и
    ///     какой у него шаг, знает ряд корпуса (<see cref="ArsenalSlotRow" />) на сцене.
    /// </summary>
    public sealed class ArsenalSlotManifest
    {
        public ArsenalFrozenEntry Entry { get; }
        public int RowIndex { get; }
        public ArsenalSlotManifest(ArsenalFrozenEntry entry, int row) { Entry=entry; RowIndex=row; }
    }
    public sealed class ArsenalDecorationSelection
    {
        public ArsenalDecorationFallback Kind { get; }
        public ArsenalDecorationDescriptor Decoration { get; }
        public string DecorationId => Decoration!=null?Decoration.DecorationId:"";
        public ArsenalPresentationPose SlotFrame => Decoration!=null?Decoration.SlotFrame:default;
        public IReadOnlyList<string> Diagnostics { get; }
        public ArsenalDecorationSelection(ArsenalDecorationFallback kind, ArsenalDecorationDescriptor decoration, IEnumerable<string> diagnostics)
        { Kind=kind; Decoration=decoration; Diagnostics=Array.AsReadOnly(diagnostics.ToArray()); }
    }
    /// <summary>Неизменный результат foundation; Success не является игровым admission.</summary>
    public sealed class ArsenalStationDescription
    {
        public string StationKey { get; }
        public string PresetId { get; }
        public ArsenalPresentationStyle Style { get; }
        public int LayoutVersion => ArsenalCompositionCatalog.CurrentLayoutVersion;
        public int IdentitySchemaVersion => ArsenalCompositionCatalog.CurrentIdentitySchemaVersion;
        public bool Success => Failures.Count==0;
        public IReadOnlyList<ArsenalCompositionFailure> Failures { get; }
        public IReadOnlyList<ArsenalSlotManifest> Slots { get; }
        public ArsenalDecorationSelection Selection { get; }
        public string LayoutFingerprint { get; }
        public string LogicalRoleFingerprint { get; }
        public ArsenalStationDescription(ArsenalStationBuildInput input, IEnumerable<ArsenalCompositionFailure> failures,
            IEnumerable<ArsenalSlotManifest> slots, ArsenalDecorationSelection selection, string layoutHash, string roleHash)
        {
            StationKey=input.StationKey; PresetId=input.PresetId; Style=input.Style; Failures=Array.AsReadOnly(failures.ToArray());
            Slots=Array.AsReadOnly(slots.ToArray()); Selection=selection;
            LayoutFingerprint=layoutHash; LogicalRoleFingerprint=roleHash;
        }
    }
}
