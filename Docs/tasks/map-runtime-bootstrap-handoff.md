# Продолжение map-runtime-bootstrap

| Цель | Мы здесь | Осталось выполнить | Технический документ |
|---|---|---|---|
| Перевести карты на единый immutable run и управляемую runtime composition | Контрактный и authoring срезы применены в активном worktree; checkpoint включает необходимые source-зависимости арсенала | Runtime composition/admission, mode transitions, Relay, адресная миграция карт и пользовательская приёмка | [Дизайн](map-runtime-bootstrap-design.md), [план](map-runtime-bootstrap-plan.md), [runtime-интеграция](map-runtime-bootstrap-runtime-integration.md) |

## Действующий срез

Рабочая копия: `F:\UnityProjects\Vr_Battlegrounds_ai`, ветка `dev`. Реализация находится здесь; отдельной копии кода или пакета, который ещё требуется применить, нет. Checkpoint не равен полной миграции: существующие production карты продолжают legacy flow.

- `MapRunConfig`, `MapRunResolver`, `MapRunScope`, `MapRunSnapshot`: immutable inputs, bounded Mirror wire contract, revision/key проверки и отмена с reverse teardown.
- `MapRunAuthority`: единственный writer на существующем SessionContext. `CommitPrepared` публикует CompositionReady; gameplay Ready не открывается. Защита от повторного BeginRun из cancellation/release callbacks уже применена.
- `MapRoot`: frozen local bindings, canonical MapData, hierarchy/placement и whole-scene serialized identity validation. StationKey принадлежит `ArsenalStationCompositionBinding`.
- `MapRuntimeCatalog` и Editor `MapRunPreflight`: native adapter, canonical asset refs, prefab namespace, dependency fingerprints и точные failures. Центральный catalog asset ещё не создан и не установлен.
- На MapData добавлены kind/debug exemptions. Существующие MapData assets и сцены этим срезом не мигрировались; их корректные значения назначить при адресной миграции.

## Source-зависимости checkpoint

Включить исходники и `.meta` следующих producer-компонентов, чтобы checkpoint не ссылался на будущий незакоммиченный пакет: `ArsenalPreset`, `ArsenalStationCompositionBinding`, `ArsenalStationPresetBinding`, `ArsenalPresentationStyle`, `ArsenalPresentationResolver`, `ArsenalPresentationApplicator`, `ArsenalMagazineOffer`, `ArsenalMagazineSupply`, `MagazineManipulationHistory`.

Связанные изменения `ArsenalSlotController`, `ArsenalPriceTag`, `ArsenalGrabRule`, поле `MapData.arsenalPreset` и read-only API `ArsenalWallController.LostItemReplaceDelay` также входят. Отключение прежней выдачи магазинов в FirearmSlotController и новые preset/refill lifecycle hooks стены не являются source-зависимостями bootstrap; они остаются в producer-задаче вместе с миграцией assets. Эти классы сохраняют producer ownership; bootstrap не присваивает себе их manifest/UID алгоритм. Generator/composer, редакторские инструменты генератора, migration assets и прочие задачи общего worktree в checkpoint не входят. Изменения WeaponInfo/readiness не требуются этому source-срезу.

## Проверки и пределы

При подготовке checkpoint повторно прошли contract 18/18, teardown 2/2, root 14/14 и AndroidCompileGate. Штатный компилятор Unity отдельно скомпилировал 349 game source файлов из индекса без ошибок: исходники других незакоммиченных задач не использовались, внешние SDK/package assemblies взяты из текущего Editor. Это source closure check, не proof полного чистого checkout всех внешних SDK. Сохранённый adversarial 6/6 не повторялся; teardown и adversarial имеют фактический RED до исправления. Inventory шести карт проверяет serialized IDs/world transforms, а не live SDK registration.

Existing EditMode группы: 20/21. Единственный отказ — LegsAnimator/bridge на трёх аватарах, которые bootstrap не менял. Не объявлять общий regression GREEN и не переписывать это ожидание до принятия игровой логики. MCP init timeout одного запуска не означал отсутствие native выполнения: итоговый XML сохранён, повтор не нужен без новой причины.

Отчёты конкретных прогонов и полная история прогресса находятся в [локальном пакете](report/map-runtime-bootstrap/). Он игнорируется Git по AGENTS.md. `checkpoint-manifest.json` содержит состав, hashes и статус подготовленного индекса; `progress-history.md` сохраняет исходный ledger. Для другого компьютера пакет нужно передать отдельно вместе с репозиторием. Актуальные архитектура и следующие действия сохранены в tracked документах независимо от доступности локальных отчётов.

## Следующее действие

1. Проверить текущий индекс/HEAD и прочитать runtime integration write-set. Автоматическая проверка отклонила прежний широкий патч bootstrap/admission + network/referee/arsenal/avatar lifecycle из-за риска блокировки gameplay. Он не применён. Не применять его частями как обход отказа; требуется разрешение этого конкретного пакета либо новое согласованное решение меньшего объёма.
2. Продолжить задачу 3: opt-in MapBootstrap с keyed prepare/spawn/teardown и закрытыми actor gates. Создать собственные catalog/service assets и scoped fixtures. Не удалять legacy services в существующих сценах до migration slice.
3. Задача 4: captured Series intention, mode prepare/commit и policy admission. Задачи 5–8: настоящий SDK registration/Relay proof, local admission, initial/direct Editor start, адресная миграция и cleanup legacy flow. Проверить точные разделы плана перед правками.
4. Не объявлять CompositionReady игровым Ready; remote LocalPlayable не доказывается socketless probe, fake ACK или таймером. Dedicated/start без SceneChanged и callback ordering обязательны.

## Как продолжить проверку

Unity общий. До acquire подготовить конкретный пакет, при nonzero acquire завершить команду без Unity action. После completion/cleanup немедленно release, затем анализировать.

```text
После checkpoint/request/watch-ticket/claim/begin своей заявки:
python Tools/agents/editor-broker.py guard --ticket <ticket> --token <token>
execute_code: return VrBattlegrounds.EditorTools.MapRunContractProbe.Run();
execute_code: return VrBattlegrounds.EditorTools.AndroidCompileGate.Run();
python Tools/agents/editor-broker.py finish --ticket <ticket> --token <token>
```

Остальные temporary method-body probes лежат в `Tools/Probes/MapRuntimeBootstrap/`: root-preflight, catalog-adversarial, teardown, baseline, lifecycle-baseline и native-inventory. `index-compile.cs` экспортирует references/defines для source closure check штатным csc Unity; локальный `prepare-checkpoint.py` сохраняет manifest/index sources и запускает этот compiler. MCP запускать с `compiler:auto`: отдельный backend Roslyn сейчас недоступен, CodeDom выполняет эти snippets. Чтение результата MCP — только через `Tools/UnityMcp/compact-result.js`. `catalog-adversarial` удаляет собственную временную папку с проверкой GUID, поэтому требует осознанного `safety_checks:false`; не использовать это для runtime-патча.

Пользователь 2026-10-05 явно разрешил этот WIP-коммит без проверки в Unity: задача ещё не завершена. Это разрешение относится к текущему checkpoint и не означает gameplay acceptance или изменение общего [правила /commit](../../.claude/commands/commit.md). При частичных изменениях общих файлов post-commit может пропустить Plastic transfer: проверять Git и Plastic отдельно по [version-control](../version-control.md), никогда не check-in весь workspace.
