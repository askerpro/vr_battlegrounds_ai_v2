using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Сборщик одного слота станции: префаб слота + оружие (карточка, раскладка слота) и внешний вид арсенала →
    ///     один настроенный слот: позы оружия, магазина и карточки, опоры, коробка приёма. Где слот будет стоять и
    ///     кто его соседи, не знает: это работа ряда (<see cref="ArsenalSlotRow" />).
    ///     <para>
    ///     Слот создаётся под неактивным контейнером и остаётся выключенным: UniqueId ролей назначаются до
    ///     пробуждения, включает слот только <see cref="ArsenalStationComposer.Activate" />.
    ///     </para>
    /// </summary>
    public static class ArsenalSlotBuilder
    {
        public static ArsenalSlotController Build(ArsenalSlotController slotPrefab, ArsenalPresentationZone slotKind,
            ArsenalPresentationSnapshot presentation, Transform inactiveStaging, ArsenalFrozenEntry entry, MapRunKey run,
            string stationKey, List<NetworkUxrIdentityAssignment> assignments)
        {
            if (slotPrefab == null || presentation == null || inactiveStaging == null || entry == null || assignments == null)
                throw new InvalidOperationException("ArsenalSlotBuilder.NullInput");
            if (inactiveStaging.gameObject.activeInHierarchy)
                throw new InvalidOperationException("ArsenalSlotBuilder.StagingActive");

            GameObject instance = UnityEngine.Object.Instantiate(slotPrefab.gameObject, inactiveStaging, false);
            instance.name = "Slot_" + entry.NetworkIndex + "_" + entry.LogicalSlotKey;
            var firearm = instance.GetComponent<FirearmSlotController>();
            if (firearm == null || firearm.MagAnchor == null)
                throw new InvalidOperationException("ArsenalSlotBuilder.NotFirearmSlot:" + slotPrefab.name);

            assignments.AddRange(ArsenalGeneratedIdentityBinding.Bind(instance, ArsenalGeneratedIdentityBinding.RolesOf(firearm),
                run, stationKey, entry.LogicalSlotKey));

            // Якоря оружия и магазина — в позы раскладки оружия (туда UltimateXR примагничивает возвращаемый
            // предмет), карточка и опоры.
            firearm.ConfigurePresentationZone(slotKind);
            ArsenalSupportProjection.MaterializePresentation(firearm, presentation);

            // Коробка приёма оружия — из раскладки; не задана — приём сферой якоря как есть.
            if (presentation.PlaceZone.IsSet)
            {
                var zone = firearm.ItemAnchor.GetComponent<ArsenalAnchorPlaceZone>();
                if (zone == null) zone = firearm.ItemAnchor.gameObject.AddComponent<ArsenalAnchorPlaceZone>();
                zone.Configure(firearm.transform, presentation.PlaceZone);
            }

            // Магазин слота выдаёт склад станции через это предложение.
            var offer = instance.GetComponent<ArsenalMagazineOffer>();
            if (offer == null) offer = instance.AddComponent<ArsenalMagazineOffer>();
            offer.Configure(firearm, firearm.MagAnchor);

            // Выключен сам: перенос в активный ряд не разбудит его раньше назначения ID.
            instance.SetActive(false);
            return firearm;
        }
    }
}
