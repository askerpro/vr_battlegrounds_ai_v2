# Проверка пользователем, 2026-10-18

Это шаги приёмки подготовленных изменений. Автоматический PASS не заменяет
оценку поведения пользователем; новые тестовые ожидания ещё не написаны.

## Сборочный helper

1. Проверить reports/helper-fix/principal-final/build.json и ps51-final/build.json:
   sourceSha3d24f530, Quest3/Server237 файлов, UXR ea5574049cff8620,
   outputRoot указывает на существующие reports/build3d24/Quest и Server.
2. Проверить ancestry: originalPair остаётся failed, raw manifests не изменены,
   attempts.exitCode=null и control-flow-derived не выдаются за сохранённый код
   процесса. UNKNOWN subtarget/sourceKind и dirty не скрыты.
3. Для собственного повторения из этого worktree выбрать новый отсутствующий
   каталог и выполнить в PS5.1 или PS7:

```powershell
powershell.exe -NoProfile -File Tools/release/Build-Game.ps1 -ReaggregateInput tasks/bots-validation/tools/reaggregate-input.json -ReaggregateOutputDirectory tasks/bots-validation/reports/helper-fix/user-qa
```

Новая сборка Unity для этой команды не нужна. Helper должен создать новый
manifest, сохранив исходные artifacts. Фактические выполненные вызовы и hash
исходников находятся в reports/helper-fix/diagnosis.md и final reports.

## Floor стенда

В пользовательском Editor открыть BotCombatStand и запустить штатно через
PlayLaunch. В гарнитуре проверить телепорт на Floor, устойчивое приземление
и отсутствие проваливания/неожиданного перемещения. Raw authoring99/99 и
catalog7maps/0errors из reports/floor818 — только технические результаты;
пользовательская проверка телепорта ещё не проведена.

## Пассивная сетевая диагностика

NetworkStartupDiagnostics по умолчанию false. В согласованных прогонах true
пишет startup JSONL каждого участника с UTC/Stopwatch, network/scene/transport
events и редкими frame gaps. После Cancel логи закрываются, прежние debugLog
и подписки восстанавливаются. Прогоны829 получили CleanupPassedtrue.

Качество диагностики проверяется по reports/trace829/timeline-analysis.md и
сырым JSONL: localhost connect доlisten и discovery timeout внутри longframe
должны быть различимы. Эти прогоны не исправляют подключение, не являются
performance baseline и не принимают внешний Player/Spectator или Quest.

## Дальнейшая приёмка

После согласования и реализации startup admission повторить прежние случаи
через PlayLaunch с теми же180s/readiness/ролями/seed. Проверить подключение двух
PC Spectator, bodyless/team0, выход/восстановление профиля; затем соответствующие
игровые/визуальные сценарии. Новые tests/expectations и accepted commit только
после пользовательского подтверждения качества соответствующего изменения.
