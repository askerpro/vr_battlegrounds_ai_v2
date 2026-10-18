# Технические H/C inputs cc-3

Builder создаёт две воспроизводимые Git-сборки через временный private index. Рабочие Assets,
HEAD и настоящий index не записываются. Это упаковка исходников для последующих проверок;
она не подтверждает компиляцию, Unity/runtime, Quest или пользовательскую приёмку.

Текущий состав — common36. Входы: B `50c27ae71da47d43245cbd4eb1ba6747a2a7e44c`, новый C* **PENDING**
с SDK81/82/84/86/87/88, Mirror6/7, companion stand и этим builder. До checkpoint нового C* упаковку не запускать.
own probe `2fa11b2767f23d4c709f739deb629fd378307c97`. Отдельный источник границ пакета —
C0 `48b27cf2bb0c9eb2dc646cc8f058bdd9d0eaf5a0` закрепляет legacy frozen77; содержимое берётся
из C*, не из C0. Десять дополнительных common M-путей перечислены отдельно и не меняют C0.

- H = B + common36 из C* + pinned probe2 + весь `tools/cc3-perf/` из C*.
- C = H + cutover48 из C* с переносом A/M/D. Удаляемые blobs не читаются.
- Common36 явно перечислены в builder: SDK81/82/84/86/87/88, Mirror6/7, Game/Core оружия, authoring,
  MagazineOnly profile, R08/Revolver, цепочка Spectator и общий DebugOrchestrator.
- Дополнение к legacy common26: `HeadlessPrecacheGuard.cs`, `UxrGrabbableObject.cs`,
  `UxrGrabbableObjectAnchor.cs` (SDK87, Replay-only derived proxy) и
  `Assets/ThirdParty/Mirror/Core/NetworkManager.cs` (Mirror6), а также
  `Assets/ThirdParty/Mirror/Core/NetworkIdentity.cs` (Mirror7), `UxrComponent.cs`,
  `UxrUniqueIdImplementer_1.cs` и `UxrMirrorAvatar.cs` (SDK88), а также
  `Assets/Editor/VR_Battlegrounds/Testing/StandProtocol.cs` и
  `Assets/Editor/VR_Battlegrounds/Testing/PlayModeStandParticipant.cs` (companion stand:
  typed request-team-change через существующий Cmd, без прямых writers). Эти десять зависимостей
  должны существовать в B и C* и входить в diff C* как M; blobs одинаковы в H и C.
- Cutover48 — frozen paths C0 в Bots, Editor/Bots, Resources/Bots и BotCombatStand,
  кроме pinned `BotStandMatchProbe.cs` и `.meta`.
- `BotAuthorityTests.cs` исключён. Весь `Assets/Tests` в обеих сборках остаётся из B.

Проверки упаковки требуют accepted ancestry B → local origin/dev и B → C*, исходный frozen77
с прежними A/M/D плюс десять явных M (ровно 87 Assets paths), common36/cutover48,
закреплённые SHA256 и meta GUID пробы, LF у переносимых
text blobs, одинаковые H/C common/probe/tools blobs и modes. Фактический H/C diff должен быть
ровно cutover48. Это ограничения состава технического пакета, а не игровые test expectations.

Оба technical commits имеют parent B. Даты и identity commits фиксированы по C*;
при одинаковых B/C*/probe получаются одинаковые trees и commits. Новые имена refs
`codex/tmp/cc3-<label>-<uuid>-hybrid|candidate` уникальны; refs создаются одной транзакцией.
Ни accepted commit, ни push, ни grants/аренда не выполняются.

## Следующая упаковка common36 после нового checkpoint

```powershell
python -I tasks/bots-fix/tools/cc3-perf/build-inputs.py --base 50c27ae71da47d43245cbd4eb1ba6747a2a7e44c --candidate <FINAL_COMMON36_C_SHA> --probe 2fa11b2767f23d4c709f739deb629fd378307c97 --output tasks/bots-fix/reports/cc3-perf-inputs/common36-attempt-1.json --label common36-attempt-1 --build
```

Это ещё не выполненная команда. Common36 refs/inventory отсутствуют; новый SHA ожидается от principal.
Расширение общего companion stand не меняет expectations существующих perf payloads.

## Историческая техническая упаковка common34

Источник той упаковки C* — `53e3a85fd6c2f88d7593ffe2a9aa94bd5e9eb1a2`, source tree
`ac321d34695ffe76c7ae269bae994670d4adec22`; это не актуальный common36 source.
Ниже команда и результат прежней версии builder, сохранённые только как история.

```powershell
python -I tasks/bots-fix/tools/cc3-perf/build-inputs.py --base 50c27ae71da47d43245cbd4eb1ba6747a2a7e44c --candidate 53e3a85fd6c2f88d7593ffe2a9aa94bd5e9eb1a2 --probe 2fa11b2767f23d4c709f739deb629fd378307c97 --output tasks/bots-fix/reports/cc3-perf-inputs/sdk88-mirror7-attempt-1.json --label sdk88-mirror7-attempt-1 --build
```

Команда завершилась exit 0; существующий отчёт не перезапускать и не заменять.
H — `1a20c093aca8075b0ff772685510f46ccf119883`, tree `a6991b2ff5b38b00649ccd85afc7d9fd9d166dc3`.
C — `1b8497e673cfc4397a6c03412067e436f9a7e0bd`, tree `403351049f3c1ff5766fe543779420106ee39f49`.
Полный inventory: `tasks/bots-fix/reports/cc3-perf-inputs/sdk88-mirror7-attempt-1.json`, SHA256
`8e7cadd4ed425d3b97a30c64d632d48a6bb8ccdd71ad71d6caffded970b13afc`.
Фактически: Assets85 (A18/M53/D14), common34/cutover48/probe2/excludedTest1/tools12;
H↔C diff ровно cutover48, Tests из B, LF82/CR0, HEAD/index неизменны.
Этот отчёт подтверждает только упаковку и не является runtime/perf acceptance. Ссылки дописаны после упаковки;
frozen inputs сохраняют прежние tool/docs blobs из C*, а не эти последующие записи.

Без `--build` нет записей; `--help` только показывает CLI. Builder не делает fetch и не использует
чужие checkout, grants или старые SCOPES builder. Физические незакоммиченные правки не входят в пакет:
все runtime/probe/tool bytes читаются из Git objects выбранных commits. В C* должны присутствовать
оба файла этого builder и остальной cc3tools пакет.

Output — новый ignored JSON только внутри `tasks/bots-fix/reports/`; существующий файл не заменяется.
Отчёт содержит разрешённые операции, source commits, blob/mode/SHA256/bytes inventory выбранных путей,
удаления как tombstones, H/C trees/commits/refs и SHA256 настоящего index до/после.
Для удаляемого исходного blob сохранены mode/object ID без чтения содержимого и SHA256.
При ошибке сохраняется `failed` с известными результатами. Созданные objects/refs не откатываются;
после неизвестного исхода сначала прочитать этот отчёт и refs, а не повторять команду.

H содержит common dependencies без пользовательской приёмки и не обозначает чистую принятую baseline.
Исторические Unity-результаты отдельных компонентов не становятся проверкой новых H/C snapshots.
Предыдущие common30 H `d5933476e3982262e6941b6f0b9aad699b8206bd` / C
`e1d95e8c2ffa00da3e3df2d6c1f68d27dc512308` имеют только офлайн-компиляцию, без runtime.
Эти evidence не заменяют свежую компиляцию и runtime будущих common36 snapshots; прежние отчёты не заменяются.
