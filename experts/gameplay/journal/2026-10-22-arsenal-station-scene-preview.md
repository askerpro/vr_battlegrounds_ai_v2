# arsenal-station-scene-preview: план станции и превью в Edit Mode, 2026-10-22

Сборка станции разделена на расчёт и создание: `ArsenalStationComposer.PlanComposition(description, station)` → план
(запись манифеста, ряд, раскладка, поза слота в ряду), без объектов, Play и сети; `PrepareComposition` строит слоты
строго по плану (`ArsenalSlotRow.Mount`), геометрия ряда — `ArsenalSlotRow.SlotLocalPoses` (её же использует `Arrange`).
Описание станции карты — `MapArsenalStationDescriber.Describe`, общий вход адаптера запуска и превью.

Новый инвариант: всё, что показывает станцию без сборки (редакторское превью
`Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalStationScenePreview.cs`), читает план и общие функции поз, карточки и опор
(`ItemWorldPose`, `ApplyCard(Transform…)`, `ArsenalPriceTag.ApplyFrame/Text/TextColor`,
`ArsenalSupportProjection.MaterializeSupportRole`), а не повторяет их расчёт. Превью всегда включено, копии не
сохраняются в сцену. Тесты — `ArsenalStationScenePreviewTests`.
