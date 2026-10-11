Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет.

1. `hand-writer-contract` — контракт порядка писателей кисти с avatar-ik (список — AGENT.md «Invariants»); bots-fix пишет `BotBody`/`UxrArmIKSolver` — analysis.md «Минимальная база знаний», `expert-map.md`.
2. `grip-pose-coverage` — `GrabPoseCoverageTests` влит красным (d8d94cf9: MEF без хвата планшета/жетона/части оружия, Cyborg — пистолета/M16/дробовика), генератора переноса поз на базы нет; сверить с инвентарём mef-grip-audit.
3. `hrq-fresh-verification` — 89/89, 6/6, Android PASS HRQ 0.5 сняты на 2c845870; свежий прогон, возможно в palm-validation — handoff.md «Незавершённое и ограничения».
4. `hrq-export-reports` — экспорт HRQ из `Docs/tasks/report/hand-rig-quality` в `tasks/hand-rig-quality/reports` — Details.md «Порядок завершения static-analyzer» п.2.
5. `sdk59-changelog-name` — `changelog/2026-10-07-sdk-preview-scale.md` → `<date>-sdk-59.md` со ссылками — там же.
6. `kb-links-fix` — `tasks/hand-rig-quality/expert/sources.md:61`: ссылки `../../Readme.md`, `../../Details.md`, `../../tool.md` не разрешаются (нужно `../`) — владельцу KB.
7. `known-issues-28-sync` — Issue 28 пишет «патч и тест не реализованы», а патч 45 есть — `Docs/UltimateXR/sdk-patches.md`, через этап Docs.
8. `grips-kb-docs` — продуктовая KB `Docs/grips/`; этап documentation её не покрывает — analysis.md «Минимальная база знаний».
9. `profile-awake-adapter` — старт `.codex/agents/avatar-grip-expert.toml` через awake; файл у влитого expert-registration, нужен новый этап.
10. `stale-grab-comments` — manipulation: устаревшие комментарии GrabRules/TwoHandGrabPolicy о порядке CanGrabDelegate — analysis.md «Подтверждено исходниками» п.2.
