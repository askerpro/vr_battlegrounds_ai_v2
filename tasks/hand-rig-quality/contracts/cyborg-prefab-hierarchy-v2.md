# Чистая база кистей киборга — продолжение 2026-10-18

Основание: переданное пользователем решение из Antigravity e8a3a9ba-7e73-4ab0-b297-f846737bcafd,
исследование ae9dbaf7-c04a-46b9-b8d3-9b247985c959; продолжить существующий WIP.

## Результат

`PlayerBase` содержит общий каркас игрока без тела и поз. `Camera Controller` и
`BigHandsIntegration` — ввод, сенсоры, модели контроллеров и эффекты; они сохраняются.
Встроенное тело `PlayerBase/Cyborg` общему каркасу не принадлежит.

`CyborgHandsBase` — непосредственный Unity Variant от `PlayerBase`, GUID
`71b74b0d08444f49bbb7aa2828d0890a` сохраняется. На базе полный разрешённый набор поз
киборга, включая унаследованные от прежней SDK-базы, и default pose. Собственного тела,
`MEF_Rig` и привязок аватарных костей нет. Device visuals не считаются телом аватара.

Два непосредственных потомка: `PlayerControllersCyborgAvatar` с собственным Cyborg body
и `MEF_cyborg_hands_Player` с существующим MEF rig, кистями, skinning, optimized LOD,
хитбоксами, карманами и часами. Black остаётся вариантом MEF player. GUID всех этих
префабов сохраняются. `_parentPrefab` UltimateXR соответствует фактическому Unity parent.
`PlayerBase_SdkHands` перемещается в `Unused` с сохранением GUID после успешной миграции.

## Способ и сохранность

Только Unity Prefab API: снять полностью разрешённые экземпляры до изменения предков,
unpack/convert/save с прежними путями. Не собирать свежий MEF_Rig: это потеряет graft,
1470 bone overrides и собственные добавления player. Не менять YAML вручную.

До записи сохранить точные исходные байты собственных outputs и входящие PPtr.
Unity SaveAsPrefabAsset не гарантирует сохранение local fileID: если сохранение разрушает
существующую внешнюю ссылку, пакет откатывает собственные outputs и сообщает ссылку.
Переназначение чужих потребителей требует отдельного точного scope; оружейные GUID
и authored grip records не переписываются вслепую.

Уточнение по native782: GUID сохранён, но root localID изменился. В рамках той же
согласованной реструктуризации добавлен точный native remap существующих полей и
prefab override target/objectReference. Смысловой ключ — путь имён/ordinal среди
одноимённых siblings и тип/ordinal однотипных компонентов. Внешние потребители входят
в общий backup/output scope. Ghost как Variant Controllers снимается заранее и
пересоздаётся после родителя, чтобы сохранить его resolved graph и собственные overrides.
Source links исчезнувшего наследуемого `PlayerBase/Cyborg` — устаревшая metadata;
их удаление допустимо только при доказанном исчезновении именно этого inherited
контейнера и отсутствии действующего собственного поля/override.objectReference.
Остальные потерянные объекты блокируют пакет. SDK sample-потребители — резерв85.

Калибровка и настройки конечных аватаров сохраняются из снимков. При удалении старого
тела MEF-потомка действующие ссылки на него за пределами удаляемой ветки блокируют
операцию: нельзя молча терять calibration, skins или grabber binding.

## Проверка и ограничения

GUID Variant не означает собственный authored object: Unity выдаёт generated localID
унаследованным объектам. NonSDK содержит только PrefabInstance и stripped UxrAvatar,
без added GameObjects/components. R6 исправляет источник дрейфа: самостоятельный
PlayerBase редактируется через LoadPrefabContents/Save в тот же путь вместо полной
распаковки копии; снимки всех потомков сделаны заранее. Сохранение IDs требует native
подтверждения. Условный допуск drift не вводится: исходные GUID/fileID/source/readback
guards остаются строгими. Рассмотренный более широкий inbound-index сохранён только
как отдельный черновой artifact в reports и не подключён к миграции.

Исторический DevicePrefabRegistry.asset не загружается: его script удалён в42aa82aec.
Native786 подтвердил main=null,0загруженных объектов и0входящих зависимостей среди
10225Assets, исходные SHA256 asset/meta совпали. Попытка сохранить orphan без remap
неприменима: native save меняет Controllers root fileID. Пользователь 2026-10-18
явно разрешил удалить этот asset и meta после объяснения причины и резервирования.
R8 удаляет точный asset через AssetDatabase.DeleteAsset после свежего strict proof
и SourceBackup. Затем проверяет отсутствие asset/meta/GUID path и переснимает
активные remapper/incoming до записи предков. Любой отказ возвращает исходные bytes.
IncomingReferences.RequireResolvable и CanResolveReference остаются неизменными:
в существующих assets никакие новые неразрешимые ссылки не допускаются.

Native791 локализовал406различий только в203парах UXR metadata: preview lifecycle
записывает __isInPrefab=false и GUID base. R8 использует штатную SDK паузу
AutomaticIdGenerationPrefs на всю транзакцию, включая capture, unload и rollback;
finally восстанавливает исходное значение или отсутствие ключа. UID общих компонентов
новой CyborgHandsBase берутся из исходного snapshot через SetEditorUniqueId без
генерации новых ID, записываются как overrides и строго перечитываются после save.
Полный override multiset guard сохраняется.

До подтверждения пользователем: компиляция, диагностика до/после, применимые неизменённые
проверки; никаких новых/переписанных tests, smoke checks или managed probes.
Проверить глазами Unity hierarchy, обе модели, Black, LOD, часы/карманы, руки и хваты.
Runtime/Quest — отдельная проверка, сохранение prefab её не заменяет.
После подтверждения в этой же задаче обновить AvatarHandPoseChainTests и относящиеся
к аватарам ожидания PrefabComposition/AvatarLoadout; затем принятый commit/integration.
