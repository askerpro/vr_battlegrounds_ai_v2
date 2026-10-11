# Список рендереров Cyborg восстановлен и принят; сборщик сохраняет явное владение

`avatar-renderer-regression/asset-repair`: merged/accepted `f33e5f4e0bc8a56c03963b657fe5bfa35459ac4c`
(принят c90a72dc, после rebase на `ce765889` игровые файлы побайтно совпали). Восстановлены 15
рендереров `PlayerControllersCyborgAvatar`; убраны 14 ссылок старой копии Ghost и пять добавленных
MeshRenderer, из-за которых `SetAvatarRenderMode` бросал исключение и оставлял незавершённый BeginSync.
`FixAvatarRenderers` больше не расширяет непустой явный список.

Проверено: snapshot 4e4b1ea2 / аренда321 — Cyborg и GhostAvatar прошли Start с 15 живыми ссылками,
SyncCallDepth=0, LoadMap(TestMap1) и GoLive без исключения; пользователь перезапустил Play и принял.
AvatarRendererOwnershipTests 6/6, GhostAvatarTests 10/10 (snapshot 59d0431a, native XML), списки трёх
аватаров 3/3 (snapshot 118b616e) — 19/19 выбранных. Не заявлено: полный матч, весь suite, Android.

Источники: `tasks/avatar-renderer-regression/Readme.md`; хаб (stage asset-repair, user-acceptance).
