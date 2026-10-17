# Подготовленный пакет проверки GPU launch provider r2

2026-10-15. Исполняется после завершения всех трёх owned implementation частей и review.
Это план проверки, не подтверждение результатов. Старые build matrix/DevTools PASS не
переносятся на новые source. Новые tests/probes до пользовательской проверки не создаются.

Маршрут подтверждён сопровождающим8789: diagnostic EDITOR_DELAYCALL_STALLED сам по себе
не блокирует штатные begin/guard; prepared FIFO работа продолжается. Backend — текущий MCP,
production CLI backend ещё не выпущен. AndroidCompileGate.Run(), не RunBatch (закрывает Editor);
timeout до300с и остаток lease >=timeout+60. Никаких чужих delegates/recovery/альтернативного клиента.

## До заявки

1. Совместить только собственные файлы stage gpu-launch-inputs; сверить normative r2 hashes.
   Новые CS без .meta допустимы только как input: GUID создаёт Unity в собственной lease.
2. PS AST parser всех изменённых PS1/PSM1; existing build-launcher-check.ps1 с report path
   без слова checkouts. Проверить actual result counts, diff/EOL owned physical paths и
   checkpoint blobs. JSON/spec/bundle форматы сверить с модулем/runtime/build helper.
3. Все writers заканчивают изменения input; root фиксирует technical checkpoint и precise
   output roots для новых .meta и reports/gpu-provider/unity. Физический performance WIP
   не входит в input. Совместная runtime интеграция — только отдельный grant/dependency proof.
4. Перед request fresh inbox, fetch/rebase origin/dev с сохранением WIP; проверить текущую
   broker base, ancestry checkpoint, worker state. Чужие Play/dirty/prefab/tests/import не трогать.

## Пакет своей RUNNING lease

1. Штатные request → watch-ticket → claim → begin → guard. Capability только в private TEMP;
   конкретные request/job IDs и input/base SHA в ignored отчёте, без token.
2. Получить terminal import/compile state. Новые .meta должны соответствовать новым CS;
   импорт не считается успешной компиляцией. При compile failure сохранить actual errors,
   не выполнять зависимые runtime/tests; cleanup и сразу штатный finish/receive.
3. При clean compile открыть существующие DevTools discovery и выполнить точную текущую
   namespace selection существующим backend. Сохранить полный discovery, native terminal,
   XML/JSON/log, уникальные leaves и unexpected outside selection. Не использовать старое
   число129/141/146 как замену текущего discovery; counts сверяются на этом input.
4. Android compilation gate — только существующий штатный gate, если Editor backend и
   подготовленный scope позволяют его без конфликта платформы/тестов. Иначе явно pending,
   не запускать параллельный Editor в worker и не превращать Windows compile в Android PASS.
5. Не открывать Play/не запускать GPU сценарий: performance adapter не включён в этот input.
   Готовность API/compile не доказывает реальную ready/invalidation/capture семантику.
6. После подтверждённого terminal и cleanup сразу finish → receive; результат и .meta
   получить штатно. С неизвестным исходом inspect durable/native state, не replay/clear/finish.
   Infra fault → сопровождающему canonical infra_issue и проверенный native канал, без self-repair.

## После пакета

Сверить received output, Unity GUIDs и scope. Если нужен fix — local owner edit, новый SHA
и новая FIFO заявка, не удержание lease. Полные evidence в reports/gpu-provider/; пользователю
только точный scope/counts/failures и пути. Технический checkpoint не accepted commit.

Следующий отдельный пакет с performance: Windows rendered и Q3 telemetry на явном immutable
совместном input, actual map/view/phase/settings, phase invalidation, source hashes/completeness.
Реальный GUI/шлем/capture результат и user acceptance нельзя заменить compile/EditMode.
