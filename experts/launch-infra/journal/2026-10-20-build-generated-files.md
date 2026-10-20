# build-generated-files: generated-файлы сборочных checkout, 2026-10-20

`Tools/release/BuildCheckoutGenerated.ps1` — единственный список файлов, которые Unity сама переписывает в
выделенном build-* checkout: `Packages/packages-lock.json` (MCP там из git, а не embedded-копией) и поля
`m_Prefilter*` в `Assets/URPDefaultResources/{PC,Quest}.asset`. `Build-Game.ps1` возвращает их к индексу
до `-Ref` и в `finally` после сборки (при закрытом UnityLockfile, diff — в `Logs/`), `Run-QuestE2E.ps1 -Build` —
перед сборкой. Текущий проект (`$RepoRoot`) не обрабатывается.
Инварианты: generated — только статус ` M` и diff ожидаемой природы; staged, untracked, другие поля и файлы
по-прежнему отменяют `-Ref`/парную сборку. `-Ref` проверяет всю группу до переключения любого checkout.
Новый generated-файл добавляется в модуль вместе с проверкой природы diff и тестом
`Tools/release/tests/test_build_checkout_generated.py`.
Проверено: полная сборка quest,server da78253b6 на build-test, после неё оба checkout чисты.
Открыто: модуль не входит в отпечаток `tools` у `BuildProvenance`; `Run-QuestE2E` при явных путях не исключает
текущий проект; builder manifest видит переписанный lock в `dirty.entries`.
