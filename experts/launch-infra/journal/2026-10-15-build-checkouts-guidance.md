# Build-checkout уже созданы; проверка инструментов и общий маршрут агентов

Пользователь15.10 уточнил: существующие4buildcheckout и реализация93d7dfbc3 не делаются повторно;
следующий объём — реальные сборки/кеши и инструкции всех агентов с готовыми инструментами.
Свежий audit4/4 clean/build/full,8828LFS; Library/Build отсутствуют. Dry-run Test/Prod/QuestE2E exit0.
Own Docs/release.md и AGENT.md дополнены точными командами существующих BuildGame/RunQuest.
Новые сборщики/общие писатели не добавлялись. Evidence tasks/quest-perf-pipeline/reports/2026-10-15-build-checkouts-antigravity/.

Общие AGENTS и назначение пары — у сопровождающего: infra_issue7288+Codexnative.
Требуется согласовать RUNNING workerlease из общих правил с dedicatedbatch из Docs;
до этого только dry-run безRef/Unity. Candidate tasks/quest-perf-pipeline/contracts/build-checkouts-agent-guidance.md.
Свою инфраструктуру для обхода не создаём. Первые/повторные сборки и Quest ещё открыты.

Сопровождающий7514 подтвердил: исходный dedicated batch не требует аренды интерактивного
worker, но назначения/owner-bound резервирования пар сейчас нет. Это отдельный адресный
infra-хвост сопровождающего; восстановление GUI worker7457/7492 не выдаёт batch-capability.
Собственные матрица/критерии подготовлены в contracts/build-checkouts-validation.md;
до выпуска batch admission/reservation сохранён запрет Ref/Unity. База инструкций673 DONE.
У своей задачи штатно обновлён справочный client=codex (rev27), native route epoch6;
owner/worktree/стадии сохранены. Чужие маршруты и ACK не менялись.
