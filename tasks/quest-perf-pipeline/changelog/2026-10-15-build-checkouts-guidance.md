# Существующие build-checkout: проверка и инструкция агентам

2026-10-15. Пользователь подтвердил: четыре checkout уже созданы Antigravity; нужны испытания
самих сборок и инструментов, общие инструкции и готовые команды. Повторное развёртывание не требуется.

Свежая независимая проверка:4/4 worktree clean/detached93d7dfbc3, rolebuild/full,
8828/8828 физических LFS-файлов,missing0/pointer0. Во всех нет Library/Build/Logs/reports/Temp;
0APK/игровыхEXE/статусов/логов. Source93d7 уже ancestor origin/dev.
Отчёт вне Git: reports/2026-10-15-build-checkouts-antigravity/audit.md.

В своём этапе build-checkouts (running,epoch1) дополнены Docs/release.md и expertAGENT:
полный SHA/checkpoint, явные пути пары, preflight, семантикаRef/DryRun, Test/Prod/Tablet,
готовые команды, кеши/логи/артефакты и запрет fallback. Инструменты уже существуют:
Tools/release/Build-Game.ps1, Tools/e2e/Run-QuestE2E.ps1; новых сборщиков/писателей не добавлено.

Точные документированные команды безRef и безUnity проверены:
Test pair exit0,Prod pair exit0,QuestE2E Build+DryRunBuild exit0.
Первые sandbox-запуски Test/Prod остановились на GitLFS metadata exit128; штатная эскалация
чтения дала успешные проверки. Это доступ sandbox, не неисправность инфраструктуры.
Полные логи: reports/2026-10-15-build-checkouts-antigravity/*-documented-dry-run.log.

Перед реальной сборкой unresolved admission: общий AGENTS требует RUNNING workerlease,
а Docs описывает dedicated batch без интерактивной очереди; общая build-пара пока не имеет
подтверждённого механизма назначения доRef. Свою аренду/mutex или обход не создаём.
Нужен сопровождающий: infra_issue7288, также доставлен Codexnative после свежей проверки endpoint.
Reviewable candidate общих инструкций — contracts/build-checkouts-agent-guidance.md.
До решения владельца -Ref/Unity/adb не выполняются; первая/повторная сборка ещё не доказана.

Ревью исправило неверное утверждение первоначального draft: Ref+DryRun не переключает checkout
и не гидратирует LFS; preview использует текущий HEAD. Docs и пример теперь отражают это,
реальная команда закомментирована до явного batch-допуска и назначения пары по7288.

Ответ владельца7514: dedicated batch без FIFO/lease интерактивного worker — решение пользователя,
подтверждённое maintainer-handoff.md:79. Отсутствует owner-bound назначение/резервирование пар;
Build-Game не вызывает broker admission, lockfile — только preflight; worker guard принимает
только editor_root. Сопровождающий принял отдельный адресный хвост admission/reservation и общего
AGENTS candidate. Не создаём свой lease/mutex; до выпуска маршрута Ref/Unity не запускаются.
Это missing capability, не отказ собственных сборочных инструментов. Готовая матрица сохранена
в contracts/build-checkouts-validation.md, база инструкций673 DONE. Новый native route собственной
задачи опубликован epoch6 после справочного client=codex (rev27), owner/worktree/стадии сохранены.
