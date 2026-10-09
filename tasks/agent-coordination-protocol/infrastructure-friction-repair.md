# Устранение блокировок чтения и EOL/stat checkout

Source release: bc839195fa47f4476d3a8cad06300b88f158e49a, проверенный isolated agent-infra c14d; source master
интегрирован обычным fast-forward. Runtime обновляется только после штатного merge
данного release record в origin/dev, при паузе admission и завершённых операциях.

Hook: quoted regex, read-only chains, Get-Content/Select-Object ranges, обычные
Bash cat/ls/head/tail/wc/grep/sed-read, Git status/log/diff/show и help известных
CLI не требуют begin в accepted/expired/unregistered. Codex cmd и Claude command
используют общий classifier. Конфиги обоих клиентов подключают один launcher.
Подстановки/редиректы/произвольные внешние программы и фактические mutating
команды сохраняют проверки; hook не объявляется общей shell sandbox.

Проверки: RED→GREEN, independent review без blockers, final focused14/14;
CLI21, nested5, deletes15. Реальный avatar checkout:6/6 ALLOW иactual Git exit0
для status, log-oneline, log-stat, diff-stat, diff-name-only, showHEAD:AGENTS.
Baseline реального launcher ранее блокировал4 read-кейса; после deployment
повторяются те же4 и2 мутации, которые должны остаться denied.

EOL-only checkout source04e17cb: copy-index/lock/CAS/byte verification сбрасывает
ложный stat без смены blob/mode/flags и без force/clean. Final14 switch tests PASS,
independent review, prior Git45/fullinfra359 PASS с оговорённой границей версии.
Это не чинит LightingData с valid stat: отдельный audit выявил13/14 corrupted
LightingData и готовит raw backup/ref +canonical materialization, не rebake.
Обычный Git clean/heartbeat не доказывает бинарную целостность или игровую приёмку.

Публикация MCP launcher уже1e169ec9, инструкцииce765889; правки в чужие agent
worktree не записываются. Ветки обновляют владельцы обычным rebase с сохранением
локальных diff. История/очередь/контракты/ACK сохраняются.
