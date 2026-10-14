# grab-authority — авторство хвата, поздний вход, карман и кобуры

Обновлено2026-10-14. Владелец `manipulation`, Codex; worktree
`F:/CodexWorktrees/manipulation/Vr_Battlegrounds_ai`, ветка `haptics`.
План [plan.json](plan.json), зарегистрированная редакция13; доказательства [Details.md](Details.md).

## Статус

| Этап | Состояние |
|---|---|
| `held-motion` — SDK73 | влит `ae016723c` |
| `late-join-anchors` — SDK75 | влит `8306c6a86`, принят по стенду |
| `death-release-authority` | снят: реализован gameplay `death-body-defer` |
| `pocket-active-hide` | пользователь принял; GREEN660 пройден; кандидат готовится к вливанию |
| `holster-calibration-scale` | planned; после merged `scale-events` и `pocket-active-hide` |

## Принятая архитектура

SDK владеет grab-state. Карман переносит магазин под собственный выключенный `Storage` на корне
`UxrAvatar`, сохраняет `activeSelf=true` и мировой масштаб. `ItemStored` — единственный путь
серверной записи netId в `_pocketMagazines`; клиент ждёт `identity.isClient` перед скрытием.
Collision-ignore пропускает объекты вне сцены с одной ошибкой на handle; уточнение SDK75 проверяет
наличие реализации сети. Сокращение снимка SDK75 отозвано, `DontCheckCache` сохранён.

## Проверки

- GREEN660, снимок `4da5bea753` на базе `97a320c884`: AndroidCompileGate PASS0,
  полный профильный набор470/470,0 failed/skipped; новые тесты9/9, LooseItems12/12.
- RED655:14/21,7 ожидаемых отказов на возвращённой деактивации магазина и снятой защите физики.
  Рабочие продуктовые файлы восстановлены побайтово, Unity-generated meta/GUID получены штатно.
- 660 DONE, receive paths=[] unexpected=[]. Полный XML: `reports/worker-660-green/TestResults.xml`, вне Git.
- Более широкий641:604/613,9 отказов в других областях (пол BotCombatStand×5, позы магазинов×4);
  владельцы подтвердили6917/6926. Этот набор не объявляется зелёным.
- Исторические S2C592/601: магазины и16 патронов видны опоздавшему; калибровка не искажает размер.
  [Журнал](changelog/2026-10-13-pocket-active-hide.md). Новое независимое ревью без P1/P2.

## Приёмка и следующий шаг

Пользователь2026-10-14: «считай я принял - вливай». Вопросов по приёмке нет;
новая физическая Quest-проверка агентом не заявляется.

После свёртки WIP: verify → accept → merge-request → merge-execute, затем request-base и
уведомления network/weapon-system/bots/gameplay/launch-infra. Игровые файлы кандидата сверяются с GREEN.
Документы другой своей задачи haptics остаются локально и не входят в этот этап.

Кобуры: контракт `calibration-scale-events` r1 agreed, события ещё не merged.
На каждой машине локально сохранить мировой масштаб по `ScaleChanging`, восстановить через
`localScale` по `ScaleChanged`, без снятия с якоря и синхронизации UXR.

## Ограничения и процесс

Несетевой встроенный магазин в кармане ещё не передаётся опоздавшему — отдельный backlog.
Inbox обработан; межзадачные сообщения идут в canonical inbox, старый Claude-route очищен.
Координация off, Unity-аренда обязательна. Все подробные трассы в ignored reports.
