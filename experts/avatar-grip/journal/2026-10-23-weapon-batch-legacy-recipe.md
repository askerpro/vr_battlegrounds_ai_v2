# weapon-batch-legacy-recipe: позы `_orig` на риге цели (G6), `_orig` механики, вывод MEF_Base_Avatar, 2026-10-23

Принято пользователем в шлеме 2026-10-24. Документы — `Docs/grip-extraction.md` (`_orig`, регистрация),
`Docs/grip-known-issues.md` (G6).

Для зоны:
- G6 повторно: регистрация копировала позы пака без пястных. Теперь `AvatarGripRegistration` кодирует позу на риге цели
  (`GripPoseEncoder.Encode(…, GripPoseEncoding.TargetRigWorld)`) и сверяет игру с редактором ≤ 1°
  (`RuntimeToleranceDeg`); существующая поза с расхождением — заметка; `ReencodePoses(avatar, apply)` перекодировал
  10 поз CyborgHands (GUID прежние). Исследование [J](../../../tasks/weapon-batch-legacy-recipe/research/J-twisted-finger-poses.md).
- `_orig` деталей механики: `<Пак>_<Предмет>_<Slide|Pump>_orig`, snap внутри граббабла; запасное окно — рука, ближайшая
  к детали; без источника — расчётная точка. Snap затворов/помпы — прежние из dev, позы новые
  ([C](../../../tasks/weapon-batch-legacy-recipe/research/C-orig-action-tr15.md)).
- MEF_Base_Avatar выведен: `RetiredGripAvatarCleanup`, профиль `MEF` и R0 Grip Extraction удалены, цель по умолчанию —
  `CyborgHands`; вторая точка `BrowningHiPower_Magazine` убрана ([D](../../../tasks/weapon-batch-legacy-recipe/research/D-mef-cleanup-browning-mag.md)).
- Сторож YAML регистрации допускает родителя с удалёнными и новыми snap-узлами. Снимок принятых хватов —
  `CyborgGripSnapshotTests`, перезапись — явным `Записать_снимок_хватов`.
- Открыто: ручные хваты магазинов MKR9/PPK/TR15 и затворов (поза/snap MKR9, TR15); патч SDK `sdk-no-metacarpal-pose`.
