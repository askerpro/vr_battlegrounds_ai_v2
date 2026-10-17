# GPU diagnostics: точные launch/runtime inputs переданы performance

2026-10-15. Canonical8600: performance начал свой offline/collector срез и запросил
scoped договор launch/runtime inputs. Подготовлен gpu-launch-inputs r1, состояние proposed;
provider quest-perf-pipeline/gpu-launch-inputs planned, planrev32. До ACK/agree launch-код
не меняется; runtime/GPU/Quest проверок этого расширения нет.

RunSpec и source build/runtime evidence остаются у существующих E2E-дирижёров.
Performance владеет phase/conditions adapters, workload semantics, capture.ready,
samples и normalized gpu-package-v1. Source evidence не перезаписывает package run.json;
raw-data hash, GitSHA, artifact/view hashes различаются. На PC runtime paths содержат
runId/role/processInstanceId. CaptureBuildConditions/AddArtifact/Views/HeadPose переиспользуются.

Нормативные файлы: tasks/quest-perf-pipeline/contracts/gpu-launch-inputs.{md,json};
source audit: task reports/build-checkouts-validation/gpu-launch-field-audit-8600.md.
MD SHA256 EC62B07312F725AA87D34B98DDB3BFFDAE439E72BEA755DCB58BDA911CF9294F.
Точный scope включает transport/harvest и отдельную явную часть full build provenance;
E2EResult/Views/HeadPose source в writes не включены. Реализацию нельзя объявлять имеющейся.

RegisterCAS31 записал rev32, но вернул PUBLICATION_BUSY. Canonical infra_issue8632
и подтверждённое native-уведомление переданы сопровождающему; self-repair/recovery не было.
Последующие canonical message и contract-propose опубликованы успешно.
Ответ8643 сопровождающего: pending=null, rev32 сохранён, инфраструктурный ремонт не требуется;
старый registerCAS31 не повторяется. Snapshot договора/plan: 3f72c366b057dbd19a3f9ecc7bc03e450e3a58c1,
без code acceptance. Запрос review/ACK performance передан8639.

Прежний build acceptance закрыт: reader e83af8413, finaldocs ed8b79f17.
Base753 DONE/published ed8b79f17 подтверждён canonical8537 и native watch-ticket.
Это не доказывает будущую текущую базу: перед новым worker нужен свежий preflight.

Review8649 performance принял смысл r1, запросив точные DTO/returns до formal ACK.
Подготовлена r2 и приложение contracts/gpu-launch-dto-v2.md: public readonly поля,
точные типы/конструкторы, DiagnosticReportResult для всех Report/Invalidate, clocks,
nullable conditions и payload identity. Независимый doc review подтвердил достаточность
типизации; его две P2 неоднозначности (cross-field identity и replay precedence) уточнены.
Frozen RunSpec getter не обещается, adapter использует собственный performance input.
Source scope/planrev32 не расширяются. Это документация; compile/runtime не запускались.
R1 штатно снята владельцем до первого ACK: хаб не допускает две открытые правки одного
контракта. R2 formally proposed; оба normative document SHA256 закреплены в specJSON.

R2 agreed: consumer ACK8729/8735, provider ACK32, agreement publication
cba84ea36323e3656a92ea521aa4dbdee22f9d23. Source реализация идёт по
tasks/quest-perf-pipeline/gpu-launch-implementation-plan.md в трёх непересекающихся scopes.
Runtime compile-only: 0errors/1CS1701; build helper compile-only exit0; существующий
build launcher harness18/18. Это не Unity compile и не runtime/GPU evidence.
Независимое review: launcher early role/scene validation и полный inventory требуют
исправлений; builder provenance/final inventory и runtime r2 проверяются отдельно.
Правило inventory: исключается только точный root build.json; wildcard build.json.*
не исключает реальные файлы. Все service writes должны завершиться до final inventory.

Сопровождающий8789 подтвердил штатный MCP/FIFO маршрут compile/DevTools и
AndroidCompileGate.Run; delayCall warning не является запретом prepared work.
Своей новой Unity заявки ещё нет. Canonical8797: performance observer подготовлен;
bridge подключается только после immutable provider checkpoint и явного grant/proof.
