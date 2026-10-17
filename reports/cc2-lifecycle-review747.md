# cc2 lifecycle: review ticket747

Дата: 2026-10-15. Только чтение исходников и полученных отчётов; Unity, тесты, hub и Git mutations не запускались. Единственная запись — этот отчёт. Исторический M2/attempt-1 остаётся incomplete: второй матч не запускался, admin Stop/GoLive не выполнялись.

## Подтверждённая причина

**P2 — B4 требует четыре выигранных раунда вместо четырёх завершённых.**

- `tasks/bots-fix/tools/cc2-director/b4-runtime-watch.cs:120–125`: `firstNaturalProof = scored && firstRoundWonEvents >= 4 && firstMinds.Count == 2 && bought`.
- `Assets/Scripts/GameModes/GameMode.cs:394–396`: `RoundWonServer` вызывается только при `winner != null`. Ничья не порождает `roundWon`.
- `Assets/Scripts/GameModes/EliminationMode/EliminationMode.cs:522–531`: `roundScored` — вход в Resolution; завершение Scoreboard — `RaiseRoundWon` и решение о следующем раунде/конце матча. Следовательно один `roundScored` ещё не доказывает завершение полного цикла раунда.
- Durable probe ticket747: раунды 1 и 4 имеют `winner: 0`; scored существуют для 1..6, roundBegan — 1..6. В final probe четыре `roundWon`, но до завершения матча watcher успел прочитать только три. `firstNaturalProof=false` заблокировал Stop уже на Round5, когда первые четыре цикла были завершены.
- `watch.json:42–62`: `firstMaxRound=6`, `firstRoundWonEvents=3`, `firstNaturalProof=false`, `stopIntent=false`, `goLiveIntent=false`. Это stale periodic count, а не потеря четвёртого выигранного раунда: final probe содержит roundWon последнего раунда перед matchFinished.

**P2 для второго пакета — синхронное завершение при первой смене режима не оставляет времени на durable natural-end proof.**

- `b4-runtime-watch.cs:222–224`: чтение отчёта выполняется раз в 5 секунд; затем потеря первого Live немедленно вызывает `finish(false)`.
- В 747 Warmup уже готов: тот же key `0ba388e4acb54ff4986af493a0544a2d/1`, referee 12, epoch 3 вместо 2, Warmup mode netId 270 вместо Elimination 95. Watcher корректно оставил весь старый строгий пакет incomplete. Для новой разрешённой ветки naturalEnd это место нужно заменить ожиданием доказательства, а не объявлять произвольную смену mode естественным концом.

## Что фактически доказано в 747

Источник: `tasks/bots-fix/reports/received747/cc2-lifecycle-round1/runtime/attempt-1/M2/`.

- `watch.json:5`: причина `first-match-ended-or-mode-changed-before-admin-stop`.
- `probe/main-pid41776.json:423964–423965`: roundsPerHalf=3, roundsToWin=4; всего 6 раундов.
- `probe/main-pid41776.json:426846`: `matchFinished`, `t=502.306`, `winner=2`, `rounds=6`, scores `1:1, 2:3`.
- `probe/main-pid41776.json:426855`: `modeFinished`, `t=502.32`, тот же winner/scores.
- final=true; journal completed=false/incomplete=true; journal errors/droppedEvents/droppedInventory=0; timelineDropped=0; console errors/exceptions/asserts/relayErrors=0.
- Натуральный конец первого матча и готовый Warmup подтверждены отдельно. Это не доказательство adminStop, второго матча, sameMind между матчами или полного cc2 PASS.

Матч завершился штатно по лимиту 6 раундов со счётом 1:3. Наличие четырёх побед одной команды для конца не обязательно. `EliminationMode.cs:584–588` завершает матч при `highest >= RoundsToWin || _currentRound >= TotalRounds`. При четырёх победах одной команды матч может завершиться уже после Round4; Round5 тогда не существует. Строгий контракт «обязательно Round5, затем adminStop» не покрывает этот штатный исход без новой разрешённой naturalEnd ветки.

## Existing flow и сессии

Естественный конец:

1. `EliminationMode.cs:528–531,564–593`: цикл Scoreboard завершён → DecideAfterScoreboard.
2. `EliminationMode.cs:633–642`: FinishMap → ServerReviveAll → RaiseFinished.
3. `GameMode.cs:523–526`: Finished(winner).
4. `MapReferee.cs:551–563`: Finished(winner) публикуется **до** переключения в Warmup; затем ServerSwitchTo(warmup, stopCurrent:false).
5. `Series.cs:156–163` только записывает результат; следующая карта автоматически не грузится.

Admin stop:

1. `MapReferee.cs:364–376`: Stop → ServerStartWarmup, без события Finished и без победителя.
2. `MapReferee.cs:287–300,468–532`: ServerSwitchTo с stopCurrent:true → ForceStop → штатный EquipmentStrip → уничтожение прежнего mode → новый Warmup → CommitMode → BeginWhenReady.

В обеих ветках дальнейший штатный вход: `MapReferee.GoLive` (`310–347`) → новый Elimination через ServerSwitchTo. Descriptor публикуется до BeginWhenReady (`520–532`), поэтому Round0/WaitingForPlayers допустим до наблюдения Round1.

Новые PlayerSession не нужны. ServerSwitchTo меняет mode и штатно снимает экипировку, но не пересоздаёт сессии. `BotDirector.cs:224–228` создаёт BotMind при AddBot; удаление происходит при Remove/остановке сервера (`256–287`). `TrySyncPurchaseContext:427–440` сбрасывает контекст по ModeEpoch в прежнем Mind. Сохранение launch/server, двух session netId+instanceId и одного probe обязательно; Cancel/новый PlayLaunch между матчами разрушит цель sameMind.

## Точный минимальный predicate второго пакета

Оставить roundWon только диагностикой; не требовать положительного winner и не требовать четыре roundWon.

```csharp
bool fourScored = Enumerable.Range(1, 4).All(round =>
    firstMatchTimeline.Any(e => (string)e["kind"] == "roundScored" && (int?)e["round"] == round));
bool firstBaseProof = fourScored && firstMinds.Count == 2 && bought;

// Ветка adminStop: начало Round5 доказывает завершение Scoreboard первых четырёх.
bool mayAdminStop = branch == null && live && snapshot.ModeEpoch == firstEpoch &&
    elimination.CurrentRoundNumber >= 5 && firstBaseProof;

// Ветка naturalEnd: matchFinished подтверждает завершение последнего Scoreboard.
bool naturalEndProof = firstBaseProof && exactFirstFinishLatch &&
    durableFirstMatchFinished != null && (int?)durableFirstMatchFinished["rounds"] >= 4 &&
    durableFirstModeFinished != null && sameEndWinnerAndScores && endAfterFourthScored;
```

`firstMatchTimeline` — сегмент первой наблюдаемой Elimination, до нового запуска второго матча. Все события берутся из собственного непрерывного durable probe. `bought` и `firstMinds` сохраняют существующие фильтры session netId/instanceId, firstKey и firstEpoch. Нельзя использовать события второго матча для ретроактивного PASS первого.

`firstNaturalProof` следует присваивать только после доказательства завершённых циклов: для adminStop — firstBaseProof плюс Round>=5; для naturalEnd — naturalEndProof. `firstRoundWonEvents` оставить в отчёте без допускающего порога.

## Exact naturalEnd event и ограничение identity

- `BotStandMatchProbe.cs:458–470`: проба подписана на конкретный MapReferee.Finished.
- `BotStandMatchProbe.cs:538–544`: `matchFinished` содержит `{t,winner,scores,rounds}`.
- `BotStandMatchProbe.cs:475–490,535–536`: `modeFinished` приходит от конкретного GameMode.Finished и содержит `{t,winner,scores}`.
- У этих timeline events **нет** runKey, ModeEpoch, refereeNetId или modeNetId. `modeFinishedLocal` и сам Warmup не являются достаточным доказательством naturalEnd. Верхний `match.matchEnd` тоже не является новым событием и может сохраниться после следующего старта.

Для точной привязки без изменения gameplay добавить только read-only latch на `firstReferee.Finished` после принятия первого Live descriptor. Handler не вызывает Stop/GoLive/FinishActive: он фиксирует observed frame и проверенный snapshot `(firstKey, firstEpoch, refereeId, firstModeId)` до Warmup-коммита; проверяет exact captured referee/mode, own launch/run и живой own probe. В MapReferee этот сигнал действительно происходит перед ServerSwitchTo. Затем tick записывает latch в watch/branch evidence. Это не новый writer состояния игры. Подписку снять в любом terminal cleanup. Если latch/identity не подтверждён — никакой naturalEnd PASS по одному совпадающему JSON winner.

Вторая проверка — durable pair `matchFinished` и `modeFinished` из первого сегмента, оба после четвёртого scored, согласованные winner/scores; winner=0 допустим. В 747 пара реально присутствует. Так связываются точный первый mode и сохранившийся JSON; поля epoch/key у самих JSON events не следует выдумывать.

## Последовательность при смене mode и 5-секундном flush

1. При потере first Live не вызывать finish/cancel сразу. Сначала прежние проверки own owner/run, server-only, Active probe, immutable request, двух прежних sessions, отсутствия runtime errors и чужой карты/referee.
2. Если first mode завершился по latch, перейти в bounded `waiting-natural-proof`, сохранить frame/deadline; немедленно прочитать текущий собственный report. При неподтверждённой смене descriptor можно только ждать ограниченный срок либо завершиться incomplete; нельзя вызывать Stop на Warmup.
3. Пока durable pair ещё нет, оставить probe работающей и ждать обычного flush (WriteSeconds=5). Не вызывать FinishActive для получения нового снимка: это завершит непрерывный журнал. Отсутствующий/ещё старый файл — ожидание, не PASS; JSON parse failure/error/drop/изменённая заявка — incomplete. Перечитывать в tick, не блокирующим sleep. Cap720 продолжает считаться от установки B4; NoLive120 — от входа в ожидание и не сбрасывается каждым чтением/сменой signature.
4. До GoLive должны одновременно выполняться naturalEndProof и own ready Warmup: тот же valid key/referee; Warmup mode; mode netId согласован с snapshot; ModeEpoch>firstEpoch; ActiveModeId==Config.WarmupModeId. Два наблюдения в разных editor ticks/frames, позже eventFrame+1. Отсутствие этого результата за 120 с — incomplete.
5. Сохранить branch=`naturalEnd` и proof; `stopIntent` остаётся false. На ветке adminStop branch=`adminStop`, intent CreateNew+Flush(true) строго до единственного Stop; после него применить тот же Warmup gate. Ветки взаимоисключающие; Stop на уже естественно завершившемся матче не доказывает adminStop.
6. В обеих ветках после повторной IsWorldAuthority — один durable GoLive intent, затем GoLive; false/exception/unknown не повторять. Новый mode epoch>Warmup epoch и новый mode netId; Round0 ожидать до120с, наблюсти Round1, затем Round>=2, Round1 Buy/contextReset обеих прежних sessions и прежний Mind по одному журналу.

## Cleanup и границы

- Не адаптировать/не повторять M2 attempt-1: он уже Cancelled и final incomplete. Второму пакету нужны новый immutable attempt/run/output и новый исходный intent namespace.
- Во время waiting-natural-proof нельзя Cancel/relaunch, FinishActive, удалять/добавлять ботов, сессии или чинить снаряжение. Иначе потеряется continuous Mind proof.
- Успех: один explicit FinishActive(normal,true), проверить actual durable final с нулевыми ошибками/drops, затем один Cancel только своего owner/run. При отказе/капах — тот же штатный путь с completed=false. Не повторять FinishActive/Cancel при неизвестном исходе.
- В текущем B4 `155–168` уже добавлена проверка durable final; прежний finding о подавленной ошибке Write в этом физическом исходнике закрыт. Финальный журнал всё равно проверяется после SampleInventory("finish"), а не только по последнему periodic snapshot.
- B6 — только после Idle/editmode и точного совпадения собственной request с immutable; foreign request не удалять. При потере владения launch не отменять чужой запуск. Read-only latch обязательно отписать вместе с tick/log/quitting.
- Historical747 — runtime evidence одного natural end; ни исправленный predicate, ни второй матч этим review не выполнены. Предложение требует компиляции payload main и нового разрешённого runtime пакета. Новые gameplay tests не создавались.

## Physical SHA256 reviewed

```text
b4-runtime-watch.cs
DE5E50BAD6E20F17370749C930D8DB40197AEF858F495B1589290C60A4467D00
received747/.../M2/watch.json
1317CFDF178A3C6A51EC54C6C64FC3A844BF0F9FDFBD0D278990857DF238EC90
received747/.../M2/probe/main-pid41776.json
7247A2FCACBADF2EA6A8E8FC0AA5E227FB4F1D2F568BB551522F76B3A35B83DF
```


## Реализация после отдельного поручения ведущей сессии

После read-only review разрешена узкая реализация только B4/B5. Root/tag/attempt оставлены round1/attempt-1/M2; ведущая сессия переводит новый immutable пакет в round2. Исторические received747 не изменены.

Фактическая проверка Roslyn CSharpSyntaxTree с LanguageVersion=CSharp6 и SourceCodeKind=Script: B4 и B5 имеют по 0 syntax diagnostics. Это синтаксический разбор, не компиляция с публичными типами/сборками и не runtime-проверка. Полную компиляцию и runtime выполняет main. Физические файлы B4/B5: CR=0. Новых gameplay writers/tests не добавлено.

Exact unified diff относительно technical checkpoint 74a81dca5. Перед построением diff SHA256 старых blobs сверены с physical baseline DE5E50.../AD7EBE..., указанными в review.

### tasks/bots-fix/tools/cc2-director/b4-runtime-watch.cs

После правки SHA256: `3F663CDB188D1B7E8FD3DF3B7F87FDDD05AF228A30F9EB392C907D1D5205B909`.

```diff
--- 74a81dca5/tasks/bots-fix/tools/cc2-director/b4-runtime-watch.cs
+++ working/tasks/bots-fix/tools/cc2-director/b4-runtime-watch.cs
@@ -1,4 +1,4 @@
-// B4: ограниченный watcher штатных раундов и одного admin Stop -> GoLive. C# 6; не повторять установку.
+// B4: четыре завершённых раунда -> adminStop либо naturalEnd -> один GoLive. C# 6; не повторять установку.
 const string Root = "tasks/bots-fix/reports/cc2-lifecycle-round1";
 const string Attempt = "attempt-1";
 const string Run = "M2";
@@ -36,15 +36,25 @@
 var initialRoster = initialBots.Select(s => (object)new { sessionNetId = s.netId, sessionInstanceId = s.GetInstanceID(), name = s.PlayerName, token = s.DeviceToken }).ToArray();
 string runId = launch.RunId;
 double installed = EditorApplication.timeSinceStartup, phaseSince = installed, lastTick = -1, lastWrite = -1, lastProbeRead = -1;
-string state = "waiting-first-live", finishReason = null, firstKey = null, lastSignature = null;
+string state = "waiting-first-live", finishReason = null, firstKey = null, lastSignature = null, firstCompletionKind = null;
 ulong firstEpoch = 0, warmupEpoch = 0, secondEpoch = 0;
 uint refereeId = 0, firstModeId = 0, secondModeId = 0;
-int firstMaxRound = 0, secondMaxRound = 0, warmupCycles = 0, stopFrame = -1, transitionsDropped = 0;
+int firstMaxRound = 0, secondMaxRound = 0, warmupCycles = 0, stopFrame = -1, transitionsDropped = 0, lastWarmupFrame = -1;
 int runtimeErrors = 0;
 string firstRuntimeError = null;
 bool terminal = false, completed = false, stopIntent = false, goLiveIntent = false, firstNaturalProof = false, sameMindProof = false, secondRoundOneSeen = false;
 int firstRoundWonEvents = 0;
 int[] firstObservedBeganRounds = new int[0];
+int[] firstScoredRounds = new int[0];
+bool firstBaseProof = false, naturalEndProof = false, firstFinishObserved = false;
+string firstFinishError = null;
+int firstFinishFrame = -1, firstFinishWinner = 0, firstFinishRound = 0;
+double firstFinishAt = 0;
+uint warmupModeId = 0;
+object firstFinishLatch = null, naturalEndEvidence = null;
+VrBattlegrounds.Managers.MapReferee firstReferee = null;
+VrBattlegrounds.GameModes.EliminationMode firstMode = null;
+Action<VrBattlegrounds.Core.TeamData> onFirstFinished = null;
 var firstMinds = new Dictionary<uint, int>();
 var secondMinds = new Dictionary<uint, int>();
 var transitions = new List<object>();
@@ -65,16 +75,18 @@
     var authority = VrBattlegrounds.Maps.Runtime.MapRunAuthority.Instance;
     var snapshot = authority != null ? authority.Current : default(VrBattlegrounds.Maps.Runtime.MapRunSnapshot);
     var result = new {
-        schema = "cc2-lifecycle-watch@1", state, completed, finishReason, owner = Owner, attempt = Attempt, run = Run, runId,
+        schema = "cc2-lifecycle-watch@2", state, completed, finishReason, owner = Owner, attempt = Attempt, run = Run, runId,
         elapsed = Math.Round(EditorApplication.timeSinceStartup - installed, 3), capSeconds = CapSeconds, noLiveSeconds = NoLiveSeconds,
         sessionIds, sessionInstances, initialRoster, firstKey, firstEpoch, warmupEpoch, secondEpoch, refereeId, firstModeId, secondModeId,
-        firstMaxRound, secondMaxRound, firstRoundWonEvents, firstObservedBeganRounds, firstNaturalProof, secondRoundOneSeen, sameMindProof, firstMinds, secondMinds,
+        firstMaxRound, secondMaxRound, firstRoundWonEvents, firstObservedBeganRounds, firstScoredRounds, firstBaseProof,
+        firstNaturalProof, firstCompletionKind, naturalEndProof, naturalEndEvidence, firstFinishObserved, firstFinishLatch, firstFinishError, warmupModeId,
+        secondRoundOneSeen, sameMindProof, firstMinds, secondMinds,
         stopIntent, goLiveIntent, transitionsDropped, runtimeErrors, firstRuntimeError,
         descriptor = new { ready = snapshot.IsReady, key = snapshot.Key.ToString(), epoch = snapshot.ModeEpoch, mode = snapshot.ActiveModeId, modeNetId = snapshot.ActiveModeNetId, refereeNetId = snapshot.RefereeNetId },
         mapState = referee != null ? referee.CurrentState.ToString() : null,
         round = mode != null ? mode.CurrentRoundNumber : -1, phase = mode != null ? mode.CurrentRoundPhase.ToString() : null,
         updatedUtc = DateTime.UtcNow.ToString("o"), probePath, transitions,
-        scope = "FUNCTIONAL ServerOnly M2; 4 естественных раунда, затем Stop admin без победителя; не perf/Quest; seed UNKNOWN"
+        scope = "FUNCTIONAL ServerOnly M2; 4 завершённых раунда, adminStop либо naturalEnd различаются; один GoLive; не perf/Quest; seed UNKNOWN"
     };
     string temp = runRoot + "/watch.json.tmp";
     System.IO.File.WriteAllText(temp, Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
@@ -116,13 +128,33 @@
             }
         }
     }
-    if (state == "first-match") {
+    if (state == "first-match" || state == "waiting-natural-proof") {
+        int firstModeEvents = timeline.Count(e => (string)e["kind"] == "mode" && (string)e["type"] == "EliminationMode");
+        if (firstModeEvents > 1) throw new InvalidOperationException("Первый Elimination timeline неоднозначен.");
         bool scored = Enumerable.Range(1, 4).All(round => timeline.Any(e => (string)e["kind"] == "roundScored" && (int?)e["round"] == round));
+        firstScoredRounds = timeline.Where(e => (string)e["kind"] == "roundScored").Select(e => (int?)e["round"] ?? -1).Distinct().OrderBy(round => round).ToArray();
         firstObservedBeganRounds = timeline.Where(e => (string)e["kind"] == "roundBegan").Select(e => (int?)e["round"] ?? -1).Distinct().OrderBy(round => round).ToArray();
         firstRoundWonEvents = timeline.Count(e => (string)e["kind"] == "roundWon");
         bool bought = sessionIds.All(sid => events.Any(e => (uint?)e["sessionNetId"] == sid && (ulong?)e["modeEpoch"] == firstEpoch &&
             (string)e["kind"] == "buy" && (int?)e["shopToken"] == 1 && (string)e["runKey"] == firstKey));
-        firstNaturalProof = scored && firstRoundWonEvents >= 4 && firstMinds.Count == 2 && bought;
+        // Ничья даёт roundScored, но не roundWon. Полный цикл доказывает Round5 либо naturalEnd.
+        firstBaseProof = firstModeEvents == 1 && scored && firstMinds.Count == 2 && bought;
+        var matchEnds = timeline.Where(e => (string)e["kind"] == "matchFinished").ToArray();
+        var modeEnds = timeline.Where(e => (string)e["kind"] == "modeFinished").ToArray();
+        if (matchEnds.Length > 1 || modeEnds.Length > 1) throw new InvalidOperationException("Первый naturalEnd timeline неоднозначен.");
+        if (firstFinishObserved && matchEnds.Length == 1 && modeEnds.Length == 1) {
+            var matchEnd = matchEnds[0];
+            var modeEnd = modeEnds[0];
+            if ((int?)matchEnd["winner"] != firstFinishWinner || (int?)modeEnd["winner"] != firstFinishWinner ||
+                (int?)matchEnd["rounds"] != firstFinishRound || matchEnd["scores"] == null ||
+                !Newtonsoft.Json.Linq.JToken.DeepEquals(matchEnd["scores"], modeEnd["scores"]) ||
+                timeline.IndexOf(matchEnd) >= timeline.IndexOf(modeEnd))
+                throw new InvalidOperationException("Durable naturalEnd не совпадает с exact referee latch.");
+            bool endAfterScored = Enumerable.Range(1, 4).All(round => timeline.Take(timeline.IndexOf(matchEnd)).Any(e =>
+                (string)e["kind"] == "roundScored" && (int?)e["round"] == round));
+            naturalEndProof = firstBaseProof && firstFinishRound >= 4 && endAfterScored;
+            if (naturalEndProof) naturalEndEvidence = new { matchFinished = matchEnd, modeFinished = modeEnd, reportUpdatedUtc = (string)report["updatedUtc"] };
+        }
     }
     if (secondEpoch != 0) {
         bool firstRoundBuys = sessionIds.All(sid => events.Any(e => (uint?)e["sessionNetId"] == sid && (ulong?)e["modeEpoch"] == secondEpoch &&
@@ -136,6 +168,29 @@
 };
 EditorApplication.CallbackFunction tick = null;
 Action quitting = null;
+// Finished публикуется до Warmup-коммита. Только чтение/фиксация, исключения не уходят в publisher.
+onFirstFinished = winner => {
+    if (terminal || firstFinishObserved || firstFinishError != null || state != "first-match" || firstCompletionKind != null || stopIntent || goLiveIntent) return;
+    try {
+        var own = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
+        var authority = VrBattlegrounds.Maps.Runtime.MapRunAuthority.Instance;
+        var snapshot = authority != null ? authority.Current : default(VrBattlegrounds.Maps.Runtime.MapRunSnapshot);
+        if (own.Owner != Owner || own.RunId != runId || !own.Ready ||
+            VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != probe ||
+            !System.IO.File.Exists(requestPath) || System.IO.File.ReadAllText(requestPath) != System.IO.File.ReadAllText(runRoot + "/probe-request-immutable.json") ||
+            firstReferee == null || firstMode == null || VrBattlegrounds.Managers.MapReferee.Instance != firstReferee ||
+            firstReferee.ActiveGameMode != firstMode || firstReferee.CurrentState != VrBattlegrounds.Managers.MapState.Live ||
+            !snapshot.IsReady || snapshot.Key.ToString() != firstKey || snapshot.ModeEpoch != firstEpoch ||
+            snapshot.RefereeNetId != refereeId || snapshot.ActiveModeNetId != firstModeId ||
+            firstReferee.netId != refereeId || firstMode.netId != firstModeId)
+            throw new InvalidOperationException("Finished source/identity не совпадает с собственным первым mode.");
+        firstFinishFrame = Time.frameCount; firstFinishAt = EditorApplication.timeSinceStartup;
+        firstFinishWinner = winner != null ? winner.teamIndex : 0; firstFinishRound = firstMode.CurrentRoundNumber;
+        firstFinishLatch = new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, key = firstKey, epoch = firstEpoch,
+            refereeNetId = refereeId, modeNetId = firstModeId, frame = firstFinishFrame, winner = firstFinishWinner, round = firstFinishRound };
+        firstFinishObserved = true;
+    } catch (Exception error) { firstFinishError = error.GetType().Name + ": " + error.Message; }
+};
 Application.LogCallback onLog = (message, stack, type) => {
     if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) {
         System.Threading.Interlocked.Increment(ref runtimeErrors);
@@ -145,6 +200,7 @@
 Action<string, bool> finish = (reason, success) => {
     if (terminal) return;
     terminal = true; completed = success; finishReason = reason; state = "finishing";
+    if (!object.ReferenceEquals(firstReferee, null)) firstReferee.Finished -= onFirstFinished;
     control["terminal"] = true;
     string failure = null;
     try { write("Завершение: " + reason); } catch (Exception e) { failure = "watch-write: " + e.Message; completed = false; }
@@ -189,7 +245,11 @@
     try {
         double now = EditorApplication.timeSinceStartup;
         if (now - installed >= CapSeconds) { finish("cap-720s", false); return; }
+        if ((state == "waiting-first-live" || state == "waiting-natural-proof" || state == "waiting-warmup" || state == "waiting-second-live") && now - phaseSince >= NoLiveSeconds) {
+            finish("no-live-or-warmup-120s", false); return;
+        }
         if (runtimeErrors != 0) { finish("runtime-error", false); return; }
+        if (firstFinishError != null) { finish("first-finish-latch-error: " + firstFinishError, false); return; }
         if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active || Mirror.NetworkClient.active) { finish("play-or-serveronly-ended", false); return; }
         if (now - lastTick < .25) return;
         lastTick = now;
@@ -217,14 +277,22 @@
         if (ready && referee.CurrentState == VrBattlegrounds.Managers.MapState.Paused) { finish("unexpected-pause", false); return; }
         if (state == "waiting-first-live" && live) {
             firstKey = snapshot.Key.ToString(); firstEpoch = snapshot.ModeEpoch; refereeId = referee.netId; firstModeId = mode.netId;
+            firstReferee = referee; firstMode = elimination; firstReferee.Finished += onFirstFinished;
             state = "first-match"; phaseSince = now; write("Первый Live descriptor принят.");
         }
         if (now - lastProbeRead >= 5) { lastProbeRead = now; readProbe(); }
         if (state == "first-match") {
-            if (!live || snapshot.ModeEpoch != firstEpoch) { finish("first-match-ended-or-mode-changed-before-admin-stop", false); return; }
-            firstMaxRound = Math.Max(firstMaxRound, elimination.CurrentRoundNumber);
-            if (firstMaxRound >= 5 && firstNaturalProof) {
+            if (!live || snapshot.ModeEpoch != firstEpoch) {
+                if (!firstFinishObserved) { finish("first-mode-changed-without-exact-natural-end", false); return; }
+                firstCompletionKind = "naturalEnd"; state = "waiting-natural-proof"; phaseSince = firstFinishAt;
+                lastProbeRead = now; readProbe();
+                write("Exact naturalEnd наблюдён; ждём durable pair, обычный flush не повторяется.");
+            } else {
+                firstMaxRound = Math.Max(firstMaxRound, elimination.CurrentRoundNumber);
+            }
+            if (state == "first-match" && !firstFinishObserved && firstMaxRound >= 5 && firstBaseProof) {
                 if (!VrBattlegrounds.Network.StateEventAuthority.IsWorldAuthority) { finish("authority-lost-before-stop", false); return; }
+                firstCompletionKind = "adminStop"; firstNaturalProof = true;
                 stopIntent = true; stopFrame = Time.frameCount; state = "waiting-warmup"; phaseSince = now;
                 durable(runRoot + "/admin-stop-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, sessionIds, firstEpoch, firstMaxRound, firstRoundWonEvents, firstNaturalProof, frame = stopFrame });
                 write("Один штатный Stop администратора; не естественный конец матча.");
@@ -232,14 +300,31 @@
                 durable(runRoot + "/admin-stop-return.json", new { utc = DateTime.UtcNow.ToString("o"), returned = true });
                 return;
             }
-        } else if (state == "waiting-warmup") {
+        }
+        if (state == "waiting-natural-proof" && now - phaseSince >= NoLiveSeconds) {
+            finish("no-live-or-warmup-120s", false); return;
+        }
+        if (state == "waiting-natural-proof" && naturalEndProof) {
+            firstNaturalProof = true; state = "waiting-warmup";
+            write("Durable naturalEnd первого mode подтверждён; adminStop этой веткой не проверен.");
+        }
+        if (state == "waiting-natural-proof" || state == "waiting-warmup") {
+            bool ownWarmup = ready && mode.IsWarmup && referee.CurrentState == VrBattlegrounds.Managers.MapState.Warmup &&
+                snapshot.ModeEpoch > firstEpoch && snapshot.ActiveModeId == snapshot.Config.WarmupModeId && snapshot.ActiveModeNetId != firstModeId;
+            if (ready && !ownWarmup) { finish("unexpected-mode-before-second-live", false); return; }
+            if (ownWarmup) {
+                if (warmupEpoch == 0) { warmupEpoch = snapshot.ModeEpoch; warmupModeId = snapshot.ActiveModeNetId; }
+                else if (snapshot.ModeEpoch != warmupEpoch || snapshot.ActiveModeNetId != warmupModeId) { finish("warmup-descriptor-changed", false); return; }
+            }
+            int completionFrame = firstCompletionKind == "adminStop" ? stopFrame : firstFinishFrame;
             bool warmup = ready && mode.IsWarmup && referee.CurrentState == VrBattlegrounds.Managers.MapState.Warmup &&
-                snapshot.ModeEpoch > firstEpoch && snapshot.ActiveModeId == snapshot.Config.WarmupModeId && Time.frameCount > stopFrame + 1;
-            warmupCycles = warmup ? warmupCycles + 1 : 0;
+                ownWarmup && state == "waiting-warmup" && firstNaturalProof && Time.frameCount > completionFrame + 1;
+            if (!warmup) { warmupCycles = 0; lastWarmupFrame = -1; }
+            else if (Time.frameCount != lastWarmupFrame) { warmupCycles++; lastWarmupFrame = Time.frameCount; }
             if (warmupCycles >= 2) {
                 if (!VrBattlegrounds.Network.StateEventAuthority.IsWorldAuthority) { finish("authority-lost-before-golive", false); return; }
                 warmupEpoch = snapshot.ModeEpoch; goLiveIntent = true; state = "waiting-second-live"; phaseSince = now;
-                durable(runRoot + "/admin-golive-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, sessionIds, firstEpoch, warmupEpoch, key = firstKey, warmupCycles });
+                durable(runRoot + "/admin-golive-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, sessionIds, firstEpoch, warmupEpoch, key = firstKey, warmupCycles, firstCompletionKind, naturalEndProof });
                 write("Готовый Warmup descriptor подтверждён; один GoLive администратора.");
                 bool accepted = referee.GoLive();
                 durable(runRoot + "/admin-golive-return.json", new { utc = DateTime.UtcNow.ToString("o"), accepted });
@@ -260,10 +345,7 @@
         if (state == "second-match") {
             if (!live || snapshot.ModeEpoch != secondEpoch) { finish("second-match-ended-or-mode-changed", false); return; }
             secondMaxRound = Math.Max(secondMaxRound, elimination.CurrentRoundNumber);
-            if (secondMaxRound >= 2 && sameMindProof && firstNaturalProof) { finish("normal-round5-stop-warmup-second-round2-sameMind", true); return; }
-        }
-        if ((state == "waiting-first-live" || state == "waiting-warmup" || state == "waiting-second-live") && now - phaseSince >= NoLiveSeconds) {
-            finish("no-live-or-warmup-120s", false); return;
+            if (secondMaxRound >= 2 && sameMindProof && firstNaturalProof) { finish("normal-" + firstCompletionKind + "-warmup-second-round2-sameMind", true); return; }
         }
         string signature = state + "/" + (ready ? snapshot.ModeEpoch.ToString() : "not-ready") + "/" +
             (elimination != null ? elimination.CurrentRoundNumber + "/" + elimination.CurrentRoundPhase : "no-elimination");
@@ -273,7 +355,7 @@
 };
 control["finish"] = finish; control["runId"] = runId; control["owner"] = Owner; control["terminal"] = false;
 durable(runRoot + "/watch-install.json", new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, sessionIds, sessionInstances, initialRoster, probePath, capSeconds = CapSeconds });
-write("Watcher установлен: 2 бота, 0 людей; только штатные admin Stop/GoLive, снаряжение/урон не меняются.");
+write("Watcher установлен: 2 бота, 0 людей; adminStop/naturalEnd различаются; один GoLive, снаряжение/урон не меняются.");
 AppDomain.CurrentDomain.SetData(marker, control);
 Application.logMessageReceivedThreaded += onLog;
 EditorApplication.quitting += quitting;
```

### tasks/bots-fix/tools/cc2-director/b5-runtime-status.cs

После правки SHA256: `86445A785D293279A6B3E7091D802476C2A17A2AA5AE9D306B2E0CE5CCBED724`.

```diff
--- 74a81dca5/tasks/bots-fix/tools/cc2-director/b5-runtime-status.cs
+++ working/tasks/bots-fix/tools/cc2-director/b5-runtime-status.cs
@@ -16,6 +16,11 @@
             elapsed = (double?)json["elapsed"], firstMaxRound = (int?)json["firstMaxRound"], secondMaxRound = (int?)json["secondMaxRound"],
             firstEpoch = (ulong?)json["firstEpoch"], warmupEpoch = (ulong?)json["warmupEpoch"], secondEpoch = (ulong?)json["secondEpoch"],
             firstNaturalProof = (bool?)json["firstNaturalProof"], sameMindProof = (bool?)json["sameMindProof"],
+            firstCompletionKind = (string)json["firstCompletionKind"], naturalEndProof = (bool?)json["naturalEndProof"],
+            naturalEndEvidence = json["naturalEndEvidence"],
+            firstBaseProof = (bool?)json["firstBaseProof"], firstScoredRounds = json["firstScoredRounds"],
+            firstObservedBeganRounds = json["firstObservedBeganRounds"], firstRoundWonEvents = (int?)json["firstRoundWonEvents"],
+            firstFinishObserved = (bool?)json["firstFinishObserved"], firstFinishLatch = json["firstFinishLatch"], firstFinishError = (string)json["firstFinishError"],
             firstMinds = json["firstMinds"], secondMinds = json["secondMinds"], sessionIds = json["sessionIds"],
             stopIntent = (bool?)json["stopIntent"], goLiveIntent = (bool?)json["goLiveIntent"],
             runtimeErrors = (int?)json["runtimeErrors"], transitionsDropped = (int?)json["transitionsDropped"],
```

## Дополнительное узкое чтение B6 main delta

`b6-runtime-stop.cs:57–78`: копирование только после cleanupReady+CleanupPassed из собственного exact RunId каталога с завершающим directory separator; чужой путь/отсутствующий файл дают warning, существующая копия не перезаписывается, SHA256 сравнивается. Опасного foreign cleanup/overwrite в этой delta не обнаружено. Остаточное ограничение полноты: null/empty after.LogArchives даёт пустой archivedLogs без warning; если для данного прогона архивы обязательны, main следует отразить отсутствие как cleanupWarning и всё равно не задерживать штатный finish. B6 этим исполнителем не изменён.

