# cc3: common perf probe

2026-10-15. Source pin/hashes в common-probe-source-manifest.json — подготовка own overlay, не accepted source/result и не готовый Unity пакет.

- H = accepted B018e с неизменным default Hybrid + pinned own probe/tools; C = тот же B + отдельный exact own cutover manifest + **побайтно та же** probe. SDK/guards из B одинаковы; целое historical дерево, old SDK и чужие pending компоненты не копировать. Host SDK81 grant и seed-beforeAddBots8548 открыты.
- При материализации — одна definition probe/Request/Gate и прежний GUID, без параллельного класса. Raw source хранится только в собственном TEMP до разрешённого immutable overlay. Проверить semantic compilation обеих точных сборок; текущий C PASS не заменяет H build.
- Старый Request имеет RunTag/OutputDirectory/ExpiresUtc/WriteSeconds (общий период10с). FinishActive принимает **один string**, без completed-bool; DirectorEquip поля нет. Нужен собственный guarded cc3 harness, старые cc2 payload неизменёнными несовместимы. Hybrid gameplay API не дописывать ради cc2.
- Дополнительные trace collectors выключены: B7, Blaze/public/shot-tail subscribers, NetObserver/NetReceiveTrace не устанавливать. Стандартные match timeline/frame/network metrics common probe остаются включены. Runtime trace-off пока не доказан; private reflection/придуманный public toggle запрещены.
- Одинаковые B/SDK/platform/Unity/hardware/scene/NavMesh/Quality/URP/Host topology/controlled seed/прогрев/окна H/C. Existing asmdef имеет UNITY_EDITOR: built player/Quest запуск не подтверждён. Seed до AddBots не заменять локальным Random.InitState.
- N8: Combat p95(C8)<=freshH8 по approved scope; H10/C10 — scaling/GC без нового tolerance. GC: bytes/frame, bytes/s, duration/frames и gen0 collections/rate; нет pause duration/all generations. Invalid recorder/marker — UNKNOWN. Стандартная observer overhead одинакова; workload/длительности показываются performance.
- Обязательны actual final=true/own runTag, нулевые console errors/exceptions/asserts/relayErrors и timelineDropped, recorder validity, четыре завершённых раунда либо подтверждённый natural end с actual rounds. FinishActive return не доказывает файл: Write ловит IO до Warning. Чтение actual final после единственного вызова; mutation не replay.
- Guarded own owner/run/request, authority для admin actions, durable intents до mutation. Затем own Cancel, Idle/Playfalse/CleanupPassed, удаление только unchanged own request, полные SHA256-verified PlayLaunch logs до немедленного finish/receive. Common probe этим lifecycle не управляет.

Текущий scope: C stand slice18 sources errors0/warnings29 CS0618 (pinned probe warnings0); H static API без найденного mismatch, exact H semantic/runtime **pending**. Новые gameplay tests/SDK edits/Unity не выполнялись.
