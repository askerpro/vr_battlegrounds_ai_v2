Обновлено 2026-10-13. Кандидаты вне хаба; порядок — приоритет.

1. `hand-writer-contract` — контракт порядка писателей кисти с avatar-ik (список — AGENT.md «Invariants»); bots-fix пишет `BotBody`/`UxrArmIKSolver` — analysis.md «Минимальная база знаний», `expert-map.md`.
2. `grip-pose-coverage` — `GrabPoseCoverageTests` влит красным (d8d94cf9: MEF без хвата планшета/жетона/части оружия, Cyborg — пистолета/M16/дробовика), генератора переноса поз на базы нет; сверить с инвентарём mef-grip-audit.
   Боты от него не зависят: решение пользователя (bots-fix 4115) — Cyborg убран из ботов (MEF, MEF Black).
   Нужен только для игроков на Cyborg; запись — на базе `PlayerBase_SdkHands`.
3. `hrq-fresh-verification` — 89/89, 6/6, Android PASS HRQ 0.5 сняты на 2c845870; свежий прогон, возможно в palm-validation — handoff.md «Незавершённое и ограничения».
4. `hrq-export-reports` — экспорт HRQ из `Docs/tasks/report/hand-rig-quality` в `tasks/hand-rig-quality/reports` — Details.md «Порядок завершения static-analyzer» п.2.
5. `sdk59-changelog-name` — `changelog/2026-10-07-sdk-preview-scale.md` → `<date>-sdk-59.md` со ссылками — там же.
6. `kb-links-fix` — `tasks/hand-rig-quality/expert/sources.md:61`: ссылки `../../Readme.md`, `../../Details.md`, `../../tool.md` не разрешаются (нужно `../`) — владельцу KB.
7. `known-issues-28-sync` — Issue 28 пишет «патч и тест не реализованы», а патч 45 есть — `Docs/UltimateXR/sdk-patches.md`, через этап Docs.
8. `grips-kb-docs` — продуктовая KB `Docs/grips/`; этап documentation её не покрывает — analysis.md «Минимальная база знаний».
9. `profile-awake-adapter` — старт `.codex/agents/avatar-grip-expert.toml` через awake; файл у влитого expert-registration, нужен новый этап.
10. `stale-grab-comments` — manipulation: устаревшие комментарии GrabRules/TwoHandGrabPolicy о порядке CanGrabDelegate — analysis.md «Подтверждено исходниками» п.2.
11. `sdk-no-metacarpal-pose` — патч SDK: поза без пястных на кисти с пястными в рантайме ставит проксимальную от запястья под пястной (179°; `UxrRuntimeFingerDescriptor` ветка без пястной + `UxrAvatarRig.HandRuntimeTransformation`). Сейчас обойдено перекодировкой поз `MEF_cyborg_hands/HandPoses`; любая новая поза без пястных сломается так же. `patch-reserve`, регрессия по всем аватарам — Details.md «cyborg-grips».
12. `fit-runtime-guard` — Hand Pose Fit: сторож «рантайм против редактора» (угол фаланг после `UxrRuntimeHandDescriptor` и мирового пути) и правдоподобие суставов; анализатор оценил выкрученную кисть Scar лишь notable — Details.md «cyborg-grips».
13. `graft-corpse-bones` — `CorpseTests.У_аватара_команды_есть_труп_из_его_модели(MEF_cyborg_hands_Player)` красный: кости пальцев трупа (`CC_Base_*_Index1..3` и др.) не находятся в модели после пересадки кисти — труп не встанет в позу; пересобрать труп из модели с пересаженной кистью (владелец трупов — по карте зон) или учесть в стадии `Tools/mef-avatar`. Наблюдение worker571, 2026-10-13.
14. `mkr9-left-main-placement` — MKR9 основная точка, левая рука: кисть мимо рукояти и у MEF (F4), и у `MEF_cyborg_hands` после Grip Extraction (палец 18мм, кисть мимо) — подтверждено в шлеме 2026-10-13; разобрать зеркалирование источника для левой руки (`PackClipGripSource`), затем перезаписать.
15. `grip-fit-penetration` — после догиба пальцы местами чуть входят в меш рукояти (шлем 2026-10-13); ограничить curl по проникновению фаланг (не только подушечки) и/или итерация анализа после записи — Grip Extraction шаг 4.
16. `mef-pack-pose-tests-left` — `HandsPackHandPoseTests`/`KinemationHandPoseTests` на Optimized_MEF_Player(_Black) красные: левая рука расходится с кадром пака на 10–23° (Scar/PPK/SniperRifle большой палец, Mk14/AK105/MKR9 пальцы); этапом grip-extraction не вызвано — сверить на базе, вход `mef-correction`/R4.
