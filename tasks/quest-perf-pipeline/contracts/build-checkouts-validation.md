# Пакет проверки сборочных checkout

2026-10-15. Владелец launch-infra. Пользователь поручил реальные сборки и проверку
инструментов на уже созданных четырёх checkout. Развёртывание93d7dfbc3 подтверждено.
Это план проверки и фактические результаты; dedicated batch разрешён решением пользователя.

## Состояние на 2026-10-15 16:40MSK

Test cold завершён на74bd42f5760b5e3f04d2e886a5685a2f52ff3d1d: оба GameBuilder PASS,
UXR748f8ac1c2e18298 совпадает. Pipeline exit1 из-за несовместимого Read-UxrIdHash.
Reader исправлен без изменения producer; RED15/18 → GREEN18/18, principal18/18.
Полный cold результат — reports/build-checkouts-validation/test-cold-pair-result.json,
command exit и оба Unity лога сохранены. Это не pipeline PASS.

После terminal сохранены diff собственных build callbacks (catalog/UPM lock; Win64 URP
prefilter PC.asset). При отсутствии процессов/locks восстановлены только эти пути из
проверенного входного SHA; оба checkout clean, Library сохранены. Test repeat запущен
свежим preflight14:01MSK, exec12203, тот же runtime SHA; caller hash зафиксирован отдельно.
Test repeat завершён14:15: оба builder PASS, pipeline exit0,851.417с, UXR совпал.
Полный test-repeat-pair-result.json содержит hashes/artifact paths/Library creation и
EditorUserBuildSettings hashes. До cleanup diff совпали с cold побайтно; после terminal/free/
no-lock восстановлены только эти paths, оба Test clean, Library/Build сохранены.
Cold Test binaries overwritten repeat; для cold остаются full logs/manifest/metadata hashes,
отдельного бинарного архива нет. Текущие Test outputs сохранены в Build.
Prod cold старт14:34MSK/exec57313, logs143441, тот же runtime74bd. Предстартовые Prod
root clean/free/no-lock и Library отсутствует. Перед Prod repeat сохранить полные cold
Build outputs отдельно под task reports. При неизвестном исходе повтор запрещён.

Prod cold завершён15:59:38: обе платформы PASS, pipelineexit0,5128.277с, UXR748f8ac1c2e18298.
Все230cold outputfiles архивированы в reports/build-checkouts-validation/artifacts/prod-cold/,
source/copy SHA256 каждого совпал; полный manifest сохранён. После terminal/free/no-lock
восстановлены только exact own generated diff (совпали с Test cold), Library/Build сохранены.
Prod repeat16:15:48/exec1330/logs161619 запущен после freshpreflight, тот же74bd и callerAB74B435.
После terminal сохранить repeat outputs/metadata и сравнить Test platform cache hashes.

Prod repeat завершён16:33:33MSK: обе платформыPASS/pipelineexit0,1064.570с. Все8builderPASS,
первый Testcoldpipelineexit1 сохранён; три последующихpipelineexit0. Fullmatrix-result.json
сверяет8native log source/copy hashes. Архивы Prod cold230 + Test/Prod repeat464files полные,
source/copy SHA256 каждого совпал. После freshfree/no-lock scoped owncleanup все4root clean.
Final-checkout-state.json подтверждает неизменные Test platform-state hashes и Library creation
times после обоих Prod проходов. Матрица завершена; readerfix готов к пользовательской приёмке.

Ответ владельца gameplay8213 подтвердил: MapCatalogBuildStep/RebakeCatalog сохраняет derived
catalog через SaveAssetIfDirty без automatic postbuild restore. Архивация и возврат только
собственных generated diffs к exact frozen input после terminal/free/no-lock относятся к нашему
build cleanup. Source rebake/контракт чистоты callback остаются gameplay и отдельному кандидату
catalog-build-derived-state; ручная правка scene/YAML и расширение задачи не требуются.

## Условия начала

- Решение пользователя2026-10-15 сняло ожидание batch admission/reservation7514:
  выделенные сборки выполняются без брокера, на уровне инструкций.
- Непосредственно перед Ref/Unity проверены процессы builder/Unity, активные логи и
  UnityLockfile обоих checkout. При занятой паре или неясном статусе повторить проверку
  по таймеру через20–30мин; не запускать второй build и не прерывать чужой.
- Проверены текущие инструкции после normal fetch/rebase и один полный immutable SHA входа.
- Источник фиксирован checkpoint или принятым коммитом; грязный task-diff не считается входом.
- Оба checkout соответствующей конфигурации чистые, rolebuild/full, без LFS pointers,
  занятых UnityLockfile и параллельной сборки. Нет ручного удаления Library/Build.

## Матрица

1. Первая Test: `Build-Game.ps1 -Targets 'quest,server' -Config Test` с явными Test Win64/Android путями и `-Ref`.
2. Повторная Test: тот же SHA и команда, без удаления кешей.
3. Первая Prod: те же targets и SHA, отдельные Prod Win64/Android пути.
4. Повторная Prod: тот же SHA и команда, без удаления кешей.
5. После Prod проверить, что Test checkout остаются на той же платформе и их Library сохранена.

Tablet не включён в эту первоначальную матрицу: это дополнительный профиль на Android,
а не пятый checkout. QuestE2E dry-run уже проверен; реальная пара E2E и шлем — отдельный прогон.

## Наблюдаемые результаты

- Полная исходная команда/SHA, выбранные checkout, Config и buildTarget; UTC начало/конец,
  время каждой группы Android/Win64 и всего вызова.
- Exit 0 и фактические builder PASS / BuildResult Succeeded, без ошибок компиляции.
- Игровые EXE/APK существуют в целевом Build/Server и Build/Quest, изменены текущим прогоном;
  размер/sha256 и пути записаны. Существующий старый файл не доказывает успешную сборку.
- Test Summary показывает ожидаемый sha12; Prod штатно не получает Test-штамп.
- uxr-ids.txt между профилями и checkout совпадают; штатная итоговая кросс-сверка PASS.
- Manifest Quest проверен штатным валидатором. TargetQuest3 не включается (решение пользователя).
- Library/Bee/IL2CPP каталог каждой пары появился и сохранился между повторами/конфигурациями.
  Время повтора записывается фактически; ускорение не предполагается и не гарантируется заранее.
- После выхода все собственные Unity batch-процессы завершены; изменения Assets/Packages/ProjectSettings
  перечислены и сопоставлены с ожидаемым восстановлением scope. Ничего чужого не откатывается.

Полные логи и metadata — reports/build-checkouts-validation/, вне Git. Обычный инструмент
пишет Logs/build-*.log в исходном task-worktree, отдельного last-build.json нет.
При неизвестном исходе/таймауте сначала readonly inspect процесса/лога/артефактов; повторов вслепую нет.
Инфраструктурный отказ — hub infra_issue + проверенный Codex канал сопровождающему, без self-repair.
Отказ собственного GameBuilder/дирижёра расследуется launch-infra после освобождения общей пары.
