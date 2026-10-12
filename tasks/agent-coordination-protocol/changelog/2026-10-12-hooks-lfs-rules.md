# LFS-хуки после rebase/merge, правила Play и serialized-repair

Инцидент 4567 (lobby-decoration): после `git rebase` в основном checkout LFS-файлы, переписанные
rebase, оставались указателями (521 файл Assets/Art/Lobby). Добавлены `.githooks/post-rewrite` и
`.githooks/post-merge`: та же гидратация, что post-checkout, через общий
`configure_lfs_worktree.py hydrate` (только main/worker; агентские worktree с hydration skip — no-op;
ошибки не прерывают rebase/merge). Исправлен `after_checkout` при `files: null`. Регрессионные тесты
`LfsRewriteMergeHookTests`. Ограничение: merge с конфликтом и `--no-commit` хук не вызывают — после
них `configure_lfs_worktree.py checkout`.

Правила агентов: Play только через PlayLaunch (Docs/test-stand.md, «Как агенту включить Play»);
прямой `EditorApplication.isPlaying = true` запрещён (запрос 3746). Launcher
`Tools/agents/serialized_repair.py` (восстановление serialized binary после LF→CRLF).

Проверки: Tools/agents/tests 15 OK; два независимых ревью CLOSED (сценарии rebase -i/autosquash,
autostash, --apply, конфликт, ff/merge/squash pull, отсутствие git-lfs). Игровая приёмка не требуется.
