# Влит сброс снаряжения при смерти на верхнем уровне (death-equipment-drop)

Дата: 2026-10-13. origin/dev 01181ba22, принят пользователем.

- `PlayerLoadoutManager` больше не подписан на `PlayerDied`: сброс внутри `DieInternal` (вложенный
  `RemoveObjectFromAnchor`) до клиентов не доходил — у них оружие оставалось в кобуре трупа.
  `ServerDropEquipment` зовёт только `AvatarTeardown` с верхнего уровня: `ReleaseBeforeDestroy` (замена тела) и
  `ReleaseDeadBodyOnServer` (без замены). Один писатель.
- Порядок с manipulation: вливание после их `grab-authority/late-join-anchors` (SDK-патч 75, 8306c6a86) — поза
  предмета после снятия с якоря теперь синхронизируется, ствол у клиентов не проваливается (находка bots 5135).
- Проверки: network ACK 5293, manipulation 5154; стенд manipulation 560 PASS (смерть человека/бота → призрак,
  удаление бота). Не покрыто: смерть без смены тела, собственные Unity-сценарии gameplay.
- Общий файл `PlayerLoadoutManager.cs` с manipulation `pocket-active-hide` — они делают rebase (5764).
- Дальше: `death-body-defer/accepted-tests`. Открыт вопрос пользователю: исправление preflight при сборке из
  открытого Editor (launch-infra 5478).
