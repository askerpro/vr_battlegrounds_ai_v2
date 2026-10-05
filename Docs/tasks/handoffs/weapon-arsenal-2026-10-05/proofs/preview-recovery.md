# Read-only recovery после guard incident

Source GO удерживается root. В этой recovery не было acquire, импорта, writer, Play stop или удаления. Ранее guard=false был ошибочно проигнорирован batch; import own probe был вызван, окончание compile тогда не подтверждено, lease истекла. Исторический incident JSON сохраняется.

Свежие MCP resources: один Vr_Battlegrounds_ai@878b4a6962c686af; Play=false, compile=false, reload pending=false, update=false, phase idle, external changes=false. Prefab Stage закрыт. Активная сцена WeaponSightManualCalibration. Console содержит чужой CS1061 HandPoseEditorCapture.cs:72 (List<FitTriangle>.Where); это current Console entry, не доказательство текущей неуспешной компиляции. Этот файл не исправлялся.

Read-only native execute: passed=true; загружен ArtistDirtyBaselineProbe из Assembly-CSharp-Editor, MonoScript GUID49f5fa1163114be3949f0ad8fe2ddf54, Prepare имеет4 параметра, own context=null. AssetDatabase fixture paths0, загруженных Task4 fixture scenes0, Prefab Stage=null. Prepare/Select/Observe/Cleanup не вызывались. Это bounded residue census, не полный аудит всех временных объектов процесса.

При первом filesystem snapshot чужая lease codex-bot-stand была активна. После readback свежий filesystem snapshot показывает info отсутствующим. Это не наша lease и не разрешение writer.

Current hashes:

- imported probe и tmp probe: 88326E8B2A4473412E5943E95B3A4A356B671A4BD3F3EDC374BD2867D351AD5A
- ArsenalEditorActions.cs:36CF3FDF75E8539BEBF4891D609F152E86C0CC83ED648AD6926495202FA1D354
- ArsenalPriceTag.cs:D0068B0B6C25BCD9DC97BFC3135295850B8646356CFC76A5E4CE2BC0FFFCAE20
- Assembly-CSharp-Editor.dll:75FF8163A543A7C7F1429E9CE9FCCDE8D941D124173976CEA650D9D002904307

Product Preview/Descriptor по-прежнему отсутствуют. Исторический manifest не объявляется текущим.

`recovery-hard-stop.cjs` — исполняемая offline проверка допуска, не Unity acceptance. Guard call завершается отдельно; writer call не существует в guard batch. Перед writer требуется второй свежий receipt с literal passed/executionCompleted/outerSuccess=true, false Play/import/compile, consoleErrors0, неизменными owner/leaseIdentity/source/stage и сроком. Unknown/false/stale/lost lease запрещают вызов writer. Не переносить инструментальные outer success в passed. Повторный executionCompleted writer запрещён; renewal допустим только пока реальная операция идёт. После result+cleanup следующий вызов release; анализ вне lease.

Остаток: root должен рассмотреть recovery и восстановить конкретный Source GO. Native fixture/product RED/GREEN ещё не выполнены; текущий foreign Console требует актуальной атрибуции/исчезновения перед новым write guard. Полный graph/resource residue аудит и current standalone ref compile остаются prerequisite следующего разрешённого пакета.
