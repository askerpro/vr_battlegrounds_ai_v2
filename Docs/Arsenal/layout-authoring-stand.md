# Ручная компоновка слотов: один canonical Style

| Цель | Мы здесь | Осталось | Технический документ |
|---|---|---|---|
| Человек размещает оружие, магазины, карточки и Peg-опоры, сохраняя позы для regeneration | Stand20 сохранён; backend110/0, Inspector token/Undo/guards24/0, Android PASS; снимок вручную осмотрен | Художественная компоновка пользователем и отдельное подключение следующего генератора | Этот документ |

Canonical source — `IndustrialPegboardPresentation.asset`: defaults по фактической
зоне и exact `(WeaponInfo, Zone)` исключения. Ассортимент читается из FullDemo;
его Style reference, production Demo и Lobby в authoring-пакете не меняются.

Собственная сцена `Assets/Scenes/Debug/ArsenalLayoutAuthoring.unity` служит рабочей
проекцией, не входит в Build Settings и не является входом генератора. В ней нет
SDK, NetworkIdentity, физики и игровых контроллеров. Coordinator — editor-only.

В каждом слоте ItemTarget/MagazineTarget — обычные Transform с effective SDK Align
позой. Renderer-only предмет — дочерняя проекция через тот же DropAlign inverse;
Move/Rotate target переносит видимое оружие без непрерывного синхронизатора.
CardTarget и Peg SupportRole используют абсолютные slot-local poses. Shelf сама
поддерживает лежащие предметы и не содержит крючков.

Inspector даёт явные команды Bake выбранного/всех, карточка→zone default и Restore
из Style. Bake сохраняет только Style через один Undo и `SaveAssetIfDirty`.
Индивидуальное положение карточки становится exact override; общий default зоны
задаётся отдельной командой. Restore предупреждает о незапечённых staged правках.
Другие staged slots при Bake выбранного не уничтожаются. Масштаб targets/frames
запрещён: физический размер оружия и магазинов сохраняется.

Маршрут ручной работы после финального handoff:

1. Открыть `Assets/Scenes/Debug/ArsenalLayoutAuthoring.unity` и выбрать её единственный корневой Stand. Закрепить Inspector замком, чтобы он оставался виден при выборе handles.
2. В выпадающем списке выбрать слот. Кнопками «Выбрать: Оружие/Магазин/Карточка» или «Выбрать опору» выделить target; двигать и вращать его обычными Unity Move/Rotate. На Shelf опоры отсутствуют.
3. Нажать «Запечь выбранный → Style» либо «Запечь все 20 → Style». Это сохраняет единственный canonical Style; сохранение Scene само по себе не запекает позы.
4. «Карточка выбранного → общий default зоны» явно делает её позу общей для Peg либо Shelf. Обычный Bake карточки сохраняет только индивидуальное исключение.
5. «Восстановить из Style» пересоздаёт render-only проекцию из сохранённых поз и предупреждает о незапечённых правках. После Bake повторный Restore/открытие сцены воспроизводит сохранённую композицию.

Состав берётся из FullDemo, в Stand его не редактируют. Новые poses не подключаются
к production арсеналам автоматически; этот этап готовит авторские данные для
следующего согласованного генератора.

Общий Window по-прежнему отказывает любой dirty scene. Дополнительный authoring
lease context разрешает dirty только точной загруженной собственной сцене, одному
валидному Stand marker и захваченным scene handle/marker instance. Все остальные
сцены должны быть clean; закрытие, замена marker, чужая lease и Play запрещают
операцию. Это не общий `allowDirty`, не autosave и не сброс чужих dirty flags.

Статус проверки: `authoring-dirty-red.json` подтвердил старый отказ. Попытка Populate
показала реальный assembly-boundary дефект: Editor MonoBehaviour не прикрепляется
к GameObject. Единственный пассивный carrier перенесён в `Assets/Scripts/Arsenal`
с прежним GUID `c415754e62ae4d939bc48ba6933f4617`. Unity загрузил его из
`VrBattlegrounds`; editor backend пишет private derived cache через SerializedObject.
Повторный Populate сохранил собственную Debug-сцену: 20 слотов (11 Peg / 9 Shelf),
33 начальных Peg supports, Shelf supports=0, TMP20, SDK/NetworkIdentity/Collider/
Rigidbody=0. Foreign native scenes и семь входных assets совпали непосредственно
до/после Populate, FullDemo Style остаётся null. Отчёт —
`tmp/arsenal-visual-stage/authoring-populate-v2.json`. MCP вернул пустой ответ;
завершение установлено по сохранённому отчёту и actual idle состоянию редактора,
после чего замок освобождён. Backend Bake/regen/reload выполнил 110 проверок без
отказов на собственной временной копии Style: четыре типа поз, selected/all Bake,
byte-identical повторные сохранения, 20 regenerated слотов, default карточки зоны,
загрузка сцены и полный exact-owned cleanup. Foreign native сцена и входы совпали.
Отчёт — `tmp/arsenal-visual-stage/authoring-backend-roundtrip.json`. Actual Inspector
token выполнил 24 проверки без отказов: собственная dirty Stand сцена допустима,
чужая dirty сцена/неверный marker/path/дубликат/потерянный fake token/
закрытый context запрещены; default Window guard сохранён. Scoped Undo возвращает
позы своей копии Style. Real token удержан до exact cleanup и записи итогового
foreign readback, затем единственный Dispose; свои clone и пустая папка удалены.
Постоянный artist workspace оставлен clean с canonical Style. Отчёт —
`tmp/arsenal-visual-stage/authoring-inspector-token.json`. Свежий AndroidCompileGate
PASS, Errors=[]; console errors0. Снимок `tmp/arsenal-visual-stage/authoring-stand.png`
получен из изолированных render-only копий (315 meshes). Native workspace JSON/dirty,
семь inputs JSON/dirty/disk/meta и foreign loaded scenes совпали до/после рендера.
Все 20 панелей видны; исходные положения оружия/магазинов и малые Peg-опоры ещё
нуждаются в ручной художественной настройке. Это готовый authoring-инструмент,
не утверждение окончательной компоновки или готовности игрового арсенала.
Первые обращения остановились до мутаций на guard
папки: AssetPathToGUID по умолчанию возвращал недавно удалённый GUID при отсутствии
директории/meta/load. Проверялка различает existing assets через OnlyExistingAssets,
не ослабляя filesystem/meta/load guards. Native
render-only Style inputs20+angles20 вручную просмотрены; опоры в старых SupportPose
визуально не соответствовали оружию. Вместо дальнейшего подбора агентом приоритет
передан удобному ручному authoring пользователя с явным сохранением поз.

Начальные опоры — рабочая отправная точка, а не подтверждённая художественная
компоновка всех 20 предметов. Production Demo/Lobby этим стендом не заменяются.

Предел проверки сохранности: исходный снимок использовал instance IDs и после
импорта отметил 8880 несовпадений временных объектов. Их принадлежность исчезнувшему
реестру превью ретроспективно не доказана; исходные отчёты не удалены, результат не
объявлен ложным отказом. Непосредственная проверка остальных 8536 компонентов и
пяти native assets совпала. Отдельная read-only сверка подтвердила 168 SDK-компонентов
четырёх станций по пути, UniqueId и полному EditorJson; 1073 компонентов двух source
prefabs сохранили localFileID и EditorJson. Это ограниченное свидетельство, не полная
атрибуция всех строк старого снимка.

Перед native заполнением стенда нужен новый снимок затрагиваемого scope с persisted
object IDs, ссылками, dirty flags и дисковыми SHA. Его собирают короткими частями с
промежуточным результатом: сплошной обход GlobalObjectId/всех serialized references
сохранил отчёт, но занял несколько минут на main thread. Unity/assets/сцены в этой
read-only операции не записывались. Данные —
`tmp/arsenal-visual-stage/authoring-persisted-readonly.json`; старые исходные снимки
остаются рядом и не заменяются новым baseline.
