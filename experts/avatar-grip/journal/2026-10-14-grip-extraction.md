# Grip Extraction Tools влит: перенос хвата с кисти на кисть по центру ладони, пилот на 6 стволах

Дата: 2026-10-14. Вливание fast-forward `origin/dev e16b86b82` по решению пользователя («можешь сразу
вливать» после шлема «всё, что сделано, можно выпускать»); этап `grip-extraction` записан `record-merged`.

## Результат
- Общий конвейер извлечения хвата (`Assets/Editor/VR_Battlegrounds/Avatars/GripExtraction/`): источник —
  клипы паков HandsPack/KINEMATION, цель — профиль аватара (MEF, `MEF_cyborg_hands`), опора — запястье
  (прежнее) или центр ладони SDK; позы пишутся на риге цели с пястными; режимы замер/предпросмотр/запись.
- R0: MEF с опорой по запястью повторяет прежние хваты (58 рук, ≤0,0004мм, 0°).
- Пилот `MEF_cyborg_hands` (Scar, MP5K, AK105, MKR9, SRM12, Viper): прежние копии align MEF смещали центр
  ладони на 9–20мм и поворачивали её на 9,6°; новая посадка 0мм/0°, догиб по подсказкам анализатора —
  грубых нарушений 11→6 из 24. Шлем: «стало гораздо лучше»; MKR9 левая мимо рукояти — в бэклоге.

## Блокер при выпуске
`SaveAsPrefabAsset` из `LoadPrefabContents` записал `NetworkIdentity._assetId: 0` (Mirror `SetupIDs` в
изолированной сцене) и пустой prefab provenance у новых align. Provenance исправлен в инструменте; `_assetId`
в пилоте возвращён к канону вручную (решение пользователя), исправление инструмента — ветка
`codex/grip-extraction-assetid-fix`, обязательно до `grip-extraction-run` (условие weapon-system).
Предложения: network — патч Mirror и общий помощник сохранения; agent-infra — постпроцессор нормализатора
не срабатывает в worker.

## Уроки
- После любой записи префабов прогонять `NetworkAssetIdOnDiskTests` и сверять diff по `_assetId`,
  `__prefabGuid`, `__isInPrefab`, `_uxrUniqueId` — сторож по загруженному ассету их не видит.
- Повторная верификация после rebase: stop → begin → verify (verified SHA должен быть предком влитого).
