# Канал состояния UXR перенесён с аватара на объект уровня сессии; связь сессия↔аватар реплицируется

T-11 (`29e87b5dbe8d3575fe85bd4516307009009eabae`): `PlayerSession.ActiveAvatarNetId` — `[SyncVar(hook)]`,
источник правды о связи, пишет только сервер; на выделенном сервере связь ставит сеттер (хук SyncVar там не
вызывается). Закрыта NET-04. Проверено автором: ворота Android PASS, тесты 18/18.

T-12 (`afda2605bd8fd11cf83a0f4d6a0f80ec3c1641f5`): транспорт канала состояния UXR вынесен из `UxrMirrorAvatar` в
`NetworkStateRelay` на префабе SessionContext (спавн в `GameNetworkManager.OnStartServer`, живёт до остановки
сервера); статических полей в `UxrMirrorAvatar` не осталось. Закрыты NET-01, NET-02, NET-03, NET-15 (приём на
клиентах был мёртв целиком). Патч 1 в `Docs/UltimateXR/sdk-patches.md` переписан. Проверено автором: ворота
Android PASS, EditMode 23/23, `avatar-swap-death-replication` exit 0 (был красным до правки);
`dedicated-server-arsenal` остался exit 1 (NET-06 не трогалась).

Ограничение из коммита: зритель получает собственное событие обратно (отсечка автора по netId объекта игрока);
текущий статус — unknown.

Источники: коммиты выше; `Docs/audit/architecture-review-2026-08.md` «Корень 1», «Корень 2»;
`Docs/tasks/T-11-session-avatar-link.md`, `Docs/tasks/T-12-state-channel-to-session.md`.
