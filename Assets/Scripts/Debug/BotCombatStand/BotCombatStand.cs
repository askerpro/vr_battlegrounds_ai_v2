using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Newtonsoft.Json;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>Очередь воспроизводимых сценариев; решения живого бота остаются у Blaze.</summary>
    public sealed class BotCombatStand : MonoBehaviour
    {
        public MapData Map;
        public GameObject DetourWall, HardCover, SoftCover, VisualCover;
        public WeaponInfo[] Weapons;
        public static BotCombatStand Instance { get; private set; }
        public static string LastOutput => Instance != null ? Instance._output : null;
        public BotStandRunOptions ActiveOptions { get; private set; }
        public static event Action<BotStandCaseResult, BotStandSession> CaseStarted;
        public static event Action<BotStandCaseResult, BotStandSession> CaseSampled;
        public static event Action<BotStandCaseResult> CaseFinished;
        private BotStandRunStatus _status;
        private BotStandSession _session;
        private Coroutine _routine;
        private bool _cancel;
        private string _output;
        private UnityEngine.Random.State _random;
        private bool _randomSaved;
        private BotStandCaseResult _activeCase;
        private readonly List<BotStandCaseResult> _results = new List<BotStandCaseResult>();

        private void Awake() => Instance = this;
        private void OnDestroy() { Cleanup(); if (Instance == this) Instance = null; }

        public static BotStandRunHandle RunAll(BotStandRunOptions options) =>
            RunCases(options, (BotStandScenarioId[])Enum.GetValues(typeof(BotStandScenarioId)));

        public static BotStandRunHandle RunCases(BotStandRunOptions options, params BotStandScenarioId[] ids)
        {
            if (Instance == null || !Application.isPlaying || !NetworkServer.active)
                throw new InvalidOperationException("Нужен Play Mode отдельного серверного стенда.");
            if (Instance._routine != null) throw new InvalidOperationException("Очередь уже выполняется.");
            if (BotDirector.Instance!=null && BotDirector.Instance.Bots.Count>0) throw new InvalidOperationException("Есть чужие боты — запуск запрещён.");
            if (options == null || ids == null || ids.Length == 0 || options.Seeds == null || options.Seeds.Length == 0)
                throw new ArgumentException("Нет вариантов/seed сценария.");
            Instance._cancel = false;
            Instance._results.Clear();
            string run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Instance._output = Path.GetFullPath(string.IsNullOrEmpty(options.OutputDirectory) ? "Docs/tasks/report/bot-combat-stand/" + run : options.OutputDirectory);
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if ((Instance._output + Path.DirectorySeparatorChar).StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Отчёты стенда нельзя сохранять в Assets.");
            options = JsonConvert.DeserializeObject<BotStandRunOptions>(JsonConvert.SerializeObject(options));
            Instance.ActiveOptions = options;
            Directory.CreateDirectory(Instance._output);
            Instance._status = new BotStandRunStatus { RunId = run, Total = ids.Length * options.Seeds.Length, ReportPath = Path.Combine(Instance._output, "summary.json") };
            Instance._routine = Instance.StartCoroutine(Instance.RunGuarded(options, ids));
            return new BotStandRunHandle { RunId = run, OutputDirectory = Instance._output };
        }

        public static BotStandRunStatus GetStatus(string runId) => Instance != null && Instance._status != null && Instance._status.RunId == runId ? Instance._status : null;
        public static void Cancel(string runId) { if (Instance?._status?.RunId == runId) Instance._cancel = true; }

        private IEnumerator RunGuarded(BotStandRunOptions options, BotStandScenarioId[] ids)
        {
            IEnumerator run = Run(options, ids);
            try
            {
                while (true)
                {
                    bool next;
                    int invalidBefore=_status.InvalidFixture;
                    try { next=run.MoveNext(); }
                    catch (Exception e)
                    {
                        _status.Error=e.ToString(); _status.Passed=false; _status.IsFinished=true;
                        if(_status.InvalidFixture==invalidBefore) _status.InvalidFixture++;
                        Cleanup();
                        GameLog.Debug.Error("[BotCombatStand] Очередь остановлена: " + e.Message);
                        try { WriteReport(); } catch (Exception reportError) { GameLog.Debug.Error("[BotCombatStand] Запись отчёта: " + reportError.Message); }
                        break;
                    }
                    if (!next) break;
                    yield return run.Current;
                }
            }
            finally { (run as IDisposable)?.Dispose(); Cleanup(); _routine=null; }
        }

        private IEnumerator Run(BotStandRunOptions options, BotStandScenarioId[] ids)
        {
            _random = UnityEngine.Random.state; _randomSaved = true;
            try
            {
            foreach (int seed in options.Seeds)
            foreach (BotStandScenarioId id in ids)
            {
                if (_cancel) break;
                _session?.Dispose(); _session = null;
                yield return null; yield return null;
                if(BotDirector.Instance!=null && BotDirector.Instance.Bots.Count>0) throw new InvalidOperationException("Перед новым сценарием появились чужие боты.");
                SetGeometry(id);
                UnityEngine.Random.InitState(seed);
                BotNavMesh.Clear();
                var result = new BotStandCaseResult { Id = id.ToString(), Seed = seed, Status = "NeedsReview", Profile=options.Profile, Capture=options.Capture };
                _activeCase=result;
                _status.CurrentCase = result.Id + "/" + seed;
                _session = new BotStandSession();
                bool supported = id != BotStandScenarioId.V01 && id != BotStandScenarioId.L01;
                if (!supported) { result.Status = "Unsupported"; result.Error = id == BotStandScenarioId.V01 ? "Countdown требует отдельного запуска Elimination." : "Смерть/призрак/смена карты требуют отдельной lifecycle-очереди."; }
                try
                {
                    if (!supported) throw new NotSupportedException(result.Error);
                    var category = options.Rifle ? WeaponCategory.Rifle : WeaponCategory.Pistol;
                    WeaponInfo weapon = Weapons?.FirstOrDefault(w => w != null && w.Category == category && w.WeaponPrefab != null && (string.IsNullOrEmpty(options.WeaponName) || w.name == options.WeaponName));
                    if (weapon == null) throw new InvalidOperationException("Нет оружия нужной категории.");
                    _session.Begin(id, weapon, options);
                    BotStandMeasurements.ValidateFixture(id, _session);
                    CaseStarted?.Invoke(result, _session);
                }
                catch (NotSupportedException e) { result.Status = "Unsupported"; result.Error = e.Message; }
                catch (Exception e) { result.Status = "InvalidFixture"; result.Error = e.Message; }
                float start = Time.time;
                while (!_cancel && result.Status != "InvalidFixture" && result.Status != "Unsupported" && Time.time - start < Mathf.Clamp(options.CaseSeconds, 1f, 30f))
                {
                    _session.Tick(Time.time - start, Mathf.Clamp(options.CaseSeconds, 1f, 30f));
                    Sample(result);
                    try { CaseSampled?.Invoke(result, _session); }
                    catch (Exception e) { result.Status = "InvalidFixture"; result.Error = e.Message; }
                    yield return new WaitForSeconds(0.1f);
                }
                result.Duration = Time.time - start;
                BotStandMeasurements.Evaluate(result);
                if (result.Frames.Count == 0 && result.Status != "InvalidFixture" && result.Status != "Unsupported") { result.Status = "InvalidFixture"; result.Error = "Нет кадров акторов."; }
                try { CaseFinished?.Invoke(result); }
                catch (Exception e) { result.Status = "InvalidFixture"; result.Error = e.Message; }
                _results.Add(result);
                _activeCase=null;
                _status.Completed++;
                if (result.Status == "InvalidFixture") _status.InvalidFixture++;
                else if (result.Status == "Failed") _status.Failed++;
                else if (result.Status == "Unsupported") _status.Unsupported++;
                else if (result.Status == "NeedsReview") _status.NeedsReview++;
                WriteReport();
            }
            }
            finally { Cleanup(); }
            _status.IsFinished = true;
            if (_cancel) _status.Error = "Прогон отменён.";
            _status.Passed = _status.Failed > 0 || _status.InvalidFixture > 0 || _cancel ? false :
                _status.NeedsReview > 0 || _status.Unsupported > 0 ? (bool?)null : true;
            WriteReport();
            _routine = null;
            GameLog.Debug.Info($"[BotCombatStand] Завершён {_status.RunId}; отчёт {_status.ReportPath}");
        }

        private void Cleanup()
        {
            try
            {
                if(_activeCase!=null)
                {
                    var interrupted=_activeCase; _activeCase=null;
                    interrupted.Status="InvalidFixture"; interrupted.Error=_status?.Error ?? "Сценарий прерван до завершения.";
                    try { CaseFinished?.Invoke(interrupted); } catch(Exception e) { interrupted.Error += " Запись: " + e.Message; }
                    _results.Add(interrupted); _status.Completed++; _status.InvalidFixture++;
                    _status.Passed=false; _status.IsFinished=true;
                    try { WriteReport(); } catch(Exception e) { GameLog.Debug.Error("[BotCombatStand] Прерванный отчёт: " + e.Message); }
                }
            }
            finally
            {
                var owned=_session; _session=null;
                try { owned?.Dispose(); }
                finally { if (_randomSaved) { UnityEngine.Random.state = _random; _randomSaved = false; } }
            }
        }

        private void SetGeometry(BotStandScenarioId id)
        {
            DetourWall.SetActive(id == BotStandScenarioId.T01 || id == BotStandScenarioId.T08);
            HardCover.SetActive(id == BotStandScenarioId.T03 || id == BotStandScenarioId.T05);
            SoftCover.SetActive(id == BotStandScenarioId.T04);
            VisualCover.SetActive(false);
            var floor = DetourWall.transform.parent.Find("Floor");
            floor.gameObject.SetActive(id != BotStandScenarioId.T10);
            foreach (string name in new[] { "GapFloorA", "GapFloorB" })
            {
                var part = floor.parent.Find(name);
                if (part == null)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
                    go.transform.SetParent(floor.parent, false);
                    go.transform.position = new Vector3(0,-.15f,name == "GapFloorA" ? -5.75f : 5.75f);
                    go.transform.localScale = new Vector3(24,.3f,8.5f);
                    go.GetComponent<Renderer>().sharedMaterial = floor.GetComponent<Renderer>().sharedMaterial;
                    part = go.transform;
                }
                part.gameObject.SetActive(id == BotStandScenarioId.T10);
            }
            Physics.SyncTransforms();
        }

        private void Sample(BotStandCaseResult result)
        {
            if (!NetworkServer.active) { result.Status = "InvalidFixture"; result.Error = "Сервер остановлен."; return; }
            if(_session?.Subject?.ActiveAvatar==null) { result.Status="InvalidFixture"; result.Error="Проверяемый актор исчез."; return; }
            if(result.Id!="T02" && _session.Target?.ActiveAvatar==null) { result.Status="InvalidFixture"; result.Error="Контрольная цель исчезла."; return; }
            if (BotDirector.Instance == null) return;
            foreach (var session in _session.OwnedBots)
            {
                var player = session.ActiveAvatar;
                if (player == null) { result.Status="InvalidFixture"; result.Error="Обязательный бот потерял тело."; return; }
                var body = player.GetComponent<BotBody>(); var driver = player.GetComponent<BotCombatDriver>(); var gun = player.GetComponent<BotGunner>();
                if (body == null || driver == null || gun == null) continue;
                var brain = driver.Brain; var agent = brain != null ? brain.navmeshAgent : null;
                var target = driver.Target;
                var frame = new BotStandFrame { Frame = Time.frameCount, Time = Time.time, BodyId = player.GetInstanceID(), Name = session.PlayerName,
                    Team = session.TeamIndex, Alive = player.IsAlive, Feet = new[] { body.Feet.x, body.Feet.y, body.Feet.z },
                    State = brain != null ? brain.state.ToString() : "visual-idle", Stage = BotSenses.Stage().ToString(), Combat = driver.CombatActive,
                    TargetTeam = target != null && target.Session != null ? target.Session.TeamIndex : 0, Shots = gun.ShotsFired,
                    Category = gun.WeaponCategory.ToString(), Weapon = gun.Firearm != null ? gun.Firearm.name : null,
                    Author = gun.Firearm != null && StateEventAuthority.IsAuthorOfItem(gun.Firearm),
                    NavOnMesh = agent != null && agent.isOnNavMesh, CompletePath = agent != null && agent.isOnNavMesh && agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathComplete };
                BotStandMeasurements.Sample(frame, player, _session.Target?.ActiveAvatar);
                result.Frames.Add(frame);
            }
        }

        private void WriteReport()
        {
            File.WriteAllText(_status.ReportPath, JsonConvert.SerializeObject(new { status = _status, options=ActiveOptions, cases = _results }, Formatting.Indented), new System.Text.UTF8Encoding(false));
            BotStandReport.Write(_output,_status,_results);
        }
    }
}
