# Структура Player и самостоятельный Ghost

2026-10-19. Task/owner `player-prefab-layout`, ветка `task/player-prefab-layout`, единственный writer `F:/CodexWorktrees/player-prefab-layout`.

## Состояние и решения

Структура Player принята пользователем9743. Поручение9786 разрешило ремонт связанных tests и вливание готового;9851/9854 утвердили самостоятельный Ghost. Последнее прямое решение9889: довести Ghost до влития самостоятельно без ожидания промежуточной ручной приёмки;9893 разрешило заменить obsolete Ghost-chain assertion после native доставки. Прежний запрет merge/push снят. Физическая/визуальная VR-проверка не проводилась и не подменяется техническими checks.

Проверенный результат: полный layout, Heavy в Unused,21Editor production path sources,10test sources. Ghost — Regular с прежними GUID/root PPtrs,4локальными SDK pose refs и2явными Tablet grip entries. Controllers скрывает локально3отдельных head meshes штатным списком UxrMirrorAvatar. Runtime C# и механизм замены тела/сессии не менялись. Будущий PlayerBody/Avatar split — отдельная PARKED задача родителя.

- [x] Heavy819 и layout823 получены, GUID/metas/имена сохранены; повторных moves нет.
- [x] UserQA структуры9743;15path literals/9test files актуализированы.
- [x] SDK fixture и применимость legacy-family tests исправлены по исходному контракту.
- [x] Native835: standalone Ghost, Controllers head и2Tablet entries; внешние ссылки сохранены.
- [x] По9889/9893 проверка Ghost обновлена на самостоятельный контракт; tablet assertions усилены.
- [x] Финальная компиляция/native diagnostics и13ограниченных EditMode filters; cleanup/finish/receive.

Ревью и интеграция идут штатно: независимые ACK по полному diff → accepted commit → merge-request/merge-execute с fast-forward → request-base → проверка remote SHA → owner task-close. Актуальные terminal интеграции, ACK и published SHA фиксируются canonical hub/brief; это не дополнительное ожидание разрешения человека.

## Фактическая проверка

Ticket836:227cases, **221passed / 0failed / 6ignored**, job `e8666aa1e7ea42cbbcf195b18517cd76`, input `d928805a31e4787bc4a2454b4d7c2861c04ee17c`, result `cf845ccfc29ec9338e492d65f52d99546722ced6`; outputs paths[], unexpected[], binary-invalid[]. Editor compilation CS0, terminal guard ready/noPlay/noTests/noCompile, worker освобождён.

Hand chain19/19, Ghost10/10, SDK parity15/15+binding11/11, registered body24/24, renderer ownership6/6, game wiring18/18, wrist8/8, composition17/17, loadout63/63, exact network catalogue1/1. Kinemation16passed+3ignored и HandsPack13passed+3ignored: все29pose-registration cases выполняются;6geometry/contact methods не применимы без активных NonSdkHands consumers. Это явный skipped с причиной, не PASS. Пол BotCombatStand не менялся; прежние3Floor failures отсутствуют на принятой базе48d38b9b.

Полные [native terminal](reports/test-repair/native-terminal836.json), [raw response bundle](reports/test-repair/native-response-bundle836.json), [fixture/skip summary](reports/test-repair/summary836.json) — ignored reports. Это исходный native JSON; XML не экспортировался. [Readback Ghost](reports/test-repair/ghost-final-diagnostics836.json):0null body refs,0rig transforms вне Ghost,8валидных SDK previews для4локальных поз;12preview meshes вместе с4tablet diagnostics не являются физической оценкой хвата.

## Native идентичность и границы

835 DONE/received, R`15842ec3c6d1130de9d2d0f182c4b486a81e31b9`: ровно3prefab bodies, metas не менялись. Ghost GUID `f3401ffe5a644d647a14eff0c476b4d2`, root fileID `7972394295592992913`, Unity Regular, SDK GUID chain только Ghost,0PPtrs на Controllers. Default/Pointing/Grab/GrabStandard —4ссылки на существующие SDK assets, vendor не копировался/не менялся.

Controllers SavePrefabAsset штатно canonicalized111child IDs (24GO/73Transform/14SMR), сохранив все15root IDs/GUID/meta. [Census](contracts/controllers-consumers.md) доказал: единственный зависимый consumer —14material overrides Ghost. Поэтому Ghost отделён первым из исходного Controllers; затем выполнено head save. Native13внешних PPtrs в8assets и их raw hashes совпали before/after. Registry/AvatarData/Managers/vendor не переписывались; сохранён прежний build/spawnable состав, включая Heavy.

Tablet_Base получил ровно2новые Ghost-GUID entries с точными pose/blend/L/R align соответствующих CyborgHandsBase entries; все прежние bindings совпали native. SDK preview обоих points/обеих рук valid,2324vertices каждое. Production GhostAvatarBuilder воспроизводит Regular/4poses и idempotent tablet bindings; полный Build/Register/генераторы не запускались.

Native835 receipts: [Ghost](reports/test-repair/ghost-standalone835.json), [Controllers](reports/test-repair/head-persistent835.json), [Tablet](reports/test-repair/ghost-tablet-bindings835.json), [external refs after](reports/test-repair/external-avatar-refs-after835.json). SHA256 Ghost receipt `5876ec41bedfa6eb12915e60e31573c0fcecc913ba17a35d105de9085d3e8978`, Tablet `d2dbc3eb56077f7d5bbf39f3df7f7abe23db17b5fefb5bcf06d0f1fe3c49994a`, final diagnostics `f6e6b9b7b8a12bc68430023c6ce7e21d0c7edf76d63eb55f510559c562d361ab`.

## Сохранение работы и история

Все checkpoint/stash сохранены. Основные: принятый layout/tests `89b2537afb8c4b7070b57f2b7d1f45f812071023`;830R`cf789942befabfbf5e45e9e899043800f06b719b` и backup`596e1e431ae03df04c790c508c0704e5f740f4dd`;833R`203e37b70ea271267e670d7629e1e4321228ec10`;834R`684f5ca39ce4ab1595cb07a4960285162fb4d30f` paths[].830/833 проявили ID canonicalization; собственные prefab bodies целиком восстановлены из immutable input, YAML вручную не редактировался.834 helper отклонён до выполнения из-за namespace; исправлен вне аренды.

Перед последним request сохранён весь tracked/staged/untracked WIP в checkpoint`d2f053650713db44e875b0ce404436210ceba403` и stash`f740bf4e20b50387529b0deeb85b941145017b46`, fresh fetch/rebase подтвердили accepted`48d38b9b2885b771f9cc961b4b640eaa7f74ca9e`. Более ранние backup stash также не удалялись. Точный layout/output contract и source hashes: [Details](Details.md), [manifest](contracts/layout-manifest.json), [paths/findings](contracts/editor-path-updates.md), [repair spec](contracts/test-repair.json).

Guard diagnostic EDITOR_DELAYCALL_STALLED передан сопровождающему canonical infra_issue9904; native jobs завершились, обход/ремонт/replay не выполнялись.

## Ручная проверка после доставки

1. Проверить дерево Player, прежние имена/GUID и Heavy в Unused.
2. У живого Controllers своя голова скрыта, удалённый игрок видит голову; тело/руки не отключаются.
3. После выбытия Ghost заменяет тело штатно: прозрачность/цвет/постобработка, нет хитбоксов/карманов/выдачи оружия, триггер зоны и возрождение работают.
4. На обеих руках проверить Default/Pointing/UI и обе точки хвата планшета, выбор команды и возврат в живое тело.
5. Сетевой/Quest/визуальный результат зафиксировать отдельно от приведённых EditMode/native доказательств.
