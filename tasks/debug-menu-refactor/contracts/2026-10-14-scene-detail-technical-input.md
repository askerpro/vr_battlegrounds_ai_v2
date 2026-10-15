# Техническая композиция SceneDetail и вкладки «Сцена»

Обновлено: 2026-10-14, подготовка после inbox 6807/6841 и ответа ui 6821/6857.
Это проект маршрута, не grant, не ACK контрактов и не принятие реализации.

## Границы

UI владеет MenuDebug.cs и его локальным выбором категории. Provider level-design владеет
SceneDetail core и Lobby. В provider checkout ничего не пишем. Обычная заявка642 проверяет
только вкладки на принятой базе97a320c88; техническая композиция идёт отдельной заявкой после неё.

Контракт: `F:/UnityProjects/agent-infra/docs/immutable-input-components.md`.
Explicit components полностью проверяются даже в coordination off. Нужны зарегистрированный
consumer, RUNNING implementation с действующим permit, design>=verified с recorded hub proof.
Текст «design завершён» в Readme не является этим proof. Сейчас registration rev5 успешна,
design VERIFIED epoch3 SHA63dbf45f56; implementation RUNNING epoch1. Binding отправлен6964,
exact provider grant ожидается. Эти записи не принимают код и не заменяют проверки брокера.

## Provider input

Provider публикует immutable grant из собственного registered checkout с точными текущими
revision/core epoch, consumer revision/implementation epoch, общей B и TTL. PLANNED core допустим.
Каждый entry: exact path, A/M, mode, base_blob (null для A), source_sha, source_blob, raw SHA256.
Новые/изменённые .meta — явные entries; companion GUID и режимы сохраняются.

Read-only исследование `scene_composition_route` выявило: cc485d9bde3feadc65e703039176dbd367053351
содержит три runtime .cs, но не полный authored набор .meta/Lobby/profile. Текущий physical WIP
не равен этому Git input. Для реального Lobby нужен новый точный provider checkpoint.

Минимум для compilation: SceneDetailProfile.cs, SceneDetailApplier.cs, SceneDetailDebugSection.cs,
их .meta и новая folder .meta. Для реального Lobby provider задаёт замкнутый набор authored
Lobby.unity/profile/необходимых QualitySettings и texture .meta. Не берём весь WIP автоматически.
Editor setup scripts включаются только при подготовленном вызове; чужие tests/docs исключены.

## Consumer input

После recorded design verify начать implementation; свежие revision/epoch/B сообщить provider.
Consumer manifest в ignored reports: version1, purpose technical-unity-authoring, base_sha,
consumer {task_id,owner,revision,stage_id,epoch,absolute worktree}, components [{kind:grant,
grant_id,revision}]. Его hash относится к точным bytes файла. Новый request key с явными
--input-components и --input-components-sha256; scope/output roots — только собственный UI.

Design proof хранится в hub. Не создавать own_verified entries из tasks/** — такие paths
запрещены для components. Own_verified нужен только для реально заимствуемого Assets/Tools
предыдущего собственного verified этапа с recorded SHA и exact entries.

## UI API и данные

Provider API: SceneDetailDebugSection.Build(RectTransform, Action), Snapshot(). Публичного
IsVisible нет; доступность определяется один раз по непустому Snapshot(). Тот же snapshot
попадает в снимок MenuDebug. Заменить legacy fallback, условие добавления категории, BuildScene
и сам блок LobbyDetailTuner вместе, чтобы не оставлять два источника доступности секции.

Provider Snapshot возвращает пустую строку без Current или вне debug build. Прямая запись
Current/состояния provider запрещена. Build не вызывает Clear/Title; контейнер принадлежит UI.

## Проверка и освобождение

До request: capability runtime input_components_versions=[1], fresh B, scopes/specs/epochs,
grant TTL, каждый B→A путь, .meta/GUID, отсутствие пересечения immutable с UI writes/output roots.
Пакет: compilation, видимость/скрытие категории, Snapshot после +/- и fallback при исчезновении
секции. Настоящий Lobby runtime — через PlayLaunch и только внутри своей аренды.
После пакета/cleanup немедленно finish; capture доказывает неизменность borrowed blobs/modes.
Неизвестный исход устанавливается по durable данным до повтора. Grant/capture не принимают код.

Остаётся приёмка пользователем; принятый merge provider и релиз UI требуют отдельного штатного
порядка. При изменении B, revision/epoch/spec, grant revocation/TTL новый manifest и новый input.
