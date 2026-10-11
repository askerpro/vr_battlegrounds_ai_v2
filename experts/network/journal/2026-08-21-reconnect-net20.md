# Переподключение без перезапуска приложения (NET-20) исправлено без правок Mirror

`7823c51daf5a1d565da9e8296674c5e1a441659a`. Корень: при загрузке Offline `PhysicalSpaceSyncManager.Awake` звал
`Destroy(gameObject)` на корне ветки `--- MANAGERS ---`; `GameNetworkManager` оставался выключенным,
`UpdateScene()` из `LateUpdate` не вызывался, и `NetworkClient.isLoadingScene` навсегда оставался `true`.
Правка: `Destroy(this)`, отсев дубликата ветки — в `PersistentRoot.Start`. Роль Discovery выбирается заново
новым экземпляром.

Проверено автором: ворота PASS, EditMode 98/98, все пять сценариев яруса C зелёные; сценарий
`session-recovery-on-reconnect` без обхода GREEN (195 → 17 с). Окружение прогона — unknown.

Источники: коммит выше; `Docs/troubleshooting.md` «После разрыва не могу переподключиться»;
`Docs/audit/network-audit-2026-08.md` NET-20, NET-21.
