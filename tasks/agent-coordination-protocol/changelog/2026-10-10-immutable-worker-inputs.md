# Неизменяемые Unity inputs

Source c72db11a06bb4cfc1d76c4142f692949cf476ab3: reviewed b253fe4 parent9e5fa445
интегрирован поверх reviewed offaudit53f6760, без ослабления legacy modeoff.
Input moduleSHA256 ab2f9e46f3900423b679580dd93ddfc6f36fbcab05e309c109f9e97fc90f346f;
offaudit94d9491659dd06d7db4d366f130be34b81669d7609afb3f8f70e417796729ef3.

Provider owner/session/revision/stage scope + exactblobs/SDK reservation; consumer
exact grant/recorded own verified proofs; snapshot/receipt/revalidation и полный R/ref
до immutable failure. GUID/metadata/delete overlap/binary/EOL guards сохранены.
Metadata scan batch1024/budget64MiB вместо2process/meta. Purposetechnical-unity-authoring,
TTL1200 default/max86400, RUNNING pinned finish при expiry/revoke. Grants не являются
ownership, ACK, accept/merge или gameplay evidence.

Автор24 PASS доbatch, final15 послеbatch, legacy163(skip1) доfinalrefinements.
Независимое CLOSED:11risk доbatch,9batch/GUID наfinalab2f; все3findings закрыты.
Integration13/13PASS31,991с проверяет взаимодействие offaudit и нового guardworker; fullfinalsuite не
выдаётся. Rollback текущим deploy с capabilities preflight; старые scripts/manualcopy
не гарантируют совместимость новых незавершённых tickets.
