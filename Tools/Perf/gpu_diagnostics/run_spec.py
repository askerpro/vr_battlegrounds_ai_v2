"""Подготовка immutable входов существующего E2E, без запуска приложения или сборки."""
import hashlib
import json
import re
import uuid
from pathlib import Path

from .model import _json, _require

# Совпадает с PerfRenderDiagnosticControl.Variants (Assets/Scripts/Debug/E2E/Scenarios).
RENDER_DIAGNOSTICS = ("none", "floor-basic", "floor-unlit", "floor-planar-unlit", "floor-planar-lit", "skybox-color",
                      "floor-basic+skybox-color", "floor-unlit+skybox-color")


def _dirty_evidence(entry):
    """Запись dirty build manifest: прежний плоский вид {status, path, sha256} или вид Build-Game
    {checkout, evidence: {status, path, sha256}, generated}. Возвращает сами evidence без пути checkout."""
    evidence = entry.get("evidence") if isinstance(entry.get("evidence"), dict) else entry
    return {key: evidence.get(key) for key in ("status", "path", "sha256")}


def _prepare_run(build: Path, views: Path, out: Path, variant, fingerprint,
                 discovery, pair_id, repetition, phase_seconds=10.0, view_name=None, capture_kind="none", target="windows",
                 render_diag=None, head_pose="eye"):
    out = out.resolve()
    _require(not out.exists(), "Выходной RunSpec bundle уже существует")
    manifest = _json(build)
    source_sha = manifest.get("sourceSha")
    _require(isinstance(source_sha, str) and re.fullmatch(r"[0-9a-fA-F]{40}", source_sha), "Build manifest: нужен полный sourceSha")
    profiles = [row for row in manifest.get("profiles", []) if row.get("profile") == "e2e"]
    _require(len(profiles) == 1 and profiles[0].get("success") is True, "Требуется успешный текущий e2e build artifact")
    _require(discovery != (fingerprint is not None), "Укажите либо discovery, либо actual fingerprint")
    _require(capture_kind in (("none", "draw") if target == "windows" else ("none", "stage", "draw"))
             and not (discovery and capture_kind != "none"), "Capture требует измеряемый RunSpec поддержанного backend")
    if fingerprint is not None:
        _require(re.fullmatch(r"[0-9a-fA-F]{64}", fingerprint) is not None, "Fingerprint требует SHA256")
    _require(variant in ("lod-a", "lod-b") and type(repetition) is int and repetition >= 1, "Неверный variant/repetition")
    _require(isinstance(pair_id, str) and pair_id.strip() and 4 <= phase_seconds <= 120, "Неверный pair-id/phase seconds")
    _require(discovery or phase_seconds >= 6, "Измеряемой фазе нужен запас сверх minimumWindowSeconds4")
    view_manifest = _json(views)
    _require(isinstance(view_manifest.get("views"), list) and view_manifest["views"], "Views file пуст")
    build_raw, views_raw = build.read_bytes(), views.read_bytes()
    original_views_hash = hashlib.sha256(views_raw).hexdigest()
    if view_name is not None:
        selected = [row for row in view_manifest["views"] if row.get("name") == view_name]
        _require(len(selected) == 1, "Выбранный view отсутствует/неоднозначен")
        views_raw = (json.dumps({"views": selected}, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    digest = lambda raw: hashlib.sha256(raw).hexdigest()
    _require(render_diag is None or render_diag in RENDER_DIAGNOSTICS, "Неизвестный render_diag")
    _require(head_pose in ("eye", "off"), "head_pose: eye или off")
    args = {"mode": "map-only", "puppets": "0", "phase_s": format(phase_seconds, "g"),
            "warmup_s": "5", "scene_detail": variant}
    # Вариант рендера фиксируется планом фаз (его SHA256 в RunSpec); отдельного поля RunSpec не добавляем.
    if render_diag is not None:
        args["render_diag"] = render_diag
    phase_plan = {"schemaVersion": 1, "scenario": "perf-stress", "args": args,
                  "originalViewsSha256": original_views_hash,
                  "viewsSha256": digest(views_raw), "settleSeconds": 4,
                  "note": "План входов; фактические transitions пишет владелец StressTestServer"}
    plan_raw = (json.dumps(phase_plan, ensure_ascii=False, allow_nan=False, indent=2) + "\n").encode("utf-8")
    quality = {"profileId": "LobbyDetailProfile", "requestedFingerprint": fingerprint}
    if discovery:
        quality["discoveryOnly"] = True
    spec = {"schemaVersion": 1, "runId": uuid.uuid4().hex, "pairId": pair_id, "variant": variant,
            "repetition": repetition, "mode": "instrumented" if capture_kind != "none" else "live", "sourceSha": source_sha,
            "buildManifestSha256": digest(build_raw), "scenario": "perf-stress",
            "requestedSceneId": "Assets/Scenes/Lobby.unity", "workloadId": "lobby-lod-map-only-v1",
            "phasePlanSha256": digest(plan_raw), "views": {"path": "views.json", "sha256": digest(views_raw)},
            # off — реальный трекинг (визуальная проверка человеком в шлеме), eye — симуляция для замеров без человека.
            "headPose": {"mode": "off", "eyeMeters": None} if head_pose == "off" else {"mode": "eye", "eyeMeters": 1.6},
            "windows": {"width": 1280, "height": 720, "windowed": True},
            "quality": quality, "capture": {"kind": capture_kind, "minimumWindowSeconds": 4}}
    out.mkdir(parents=True)
    dirty_evidence = []
    # Архивируем только реально известный generated lock; dirty не превращается в clean.
    for entry in (_dirty_evidence(row) for row in (manifest.get("dirty") or {}).get("entries", [])):
        if entry["path"] != "Packages/packages-lock.json":
            continue
        lock_path = build.parent / "source-evidence" / "packages-lock.json"
        if not lock_path.is_file():
            lock_path = Path(profiles[0]["outputRoot"]).parents[1] / "Packages" / "packages-lock.json"
        if lock_path.is_file():
            lock_raw = lock_path.read_bytes()
            _require(digest(lock_raw) == entry.get("sha256"), "Build generated packages-lock изменился после source capture")
            (out / "source-evidence").mkdir(exist_ok=True)
            (out / "source-evidence" / "packages-lock.json").write_bytes(lock_raw)
            dirty_evidence.append({"path": entry["path"], "sha256": digest(lock_raw), "copy": "source-evidence/packages-lock.json"})
    (out / "source-evidence.json").write_text(json.dumps({"sourceSha": source_sha, "dirty": manifest.get("dirty"),
        "archivedGeneratedFiles": dirty_evidence}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    for name, raw in (("build.json", build_raw), ("views.json", views_raw), ("phase-plan.json", plan_raw)):
        (out / name).write_bytes(raw)
    (out / "run-spec.json").write_text(json.dumps(spec, ensure_ascii=False, allow_nan=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    launch = {"Scenario": "perf-stress", "Map": "Lobby", "Clients": 1, "Timeout": 300,
              "RunSpec": str(out / "run-spec.json"), "Arg": [key + "=" + value for key, value in args.items()],
              "note": "Использовать существующий Run-E2E.ps1; это входы, не новый launcher"}
    if target == "quest":
        launch.pop("Clients")
        quest = [row for row in manifest["profiles"] if row["profile"] == "quest"][0]
        apks = [row for row in quest["files"] if row["path"].lower().endswith(".apk")]
        _require(len(apks) == 1, "Нужен единственный APK в quest inventory")
        launch.update(ServerPlayer=str(Path(profiles[0]["outputRoot"]) / "VrBattlegrounds.exe"),
                      Apk=str(Path(quest["outputRoot"]) / apks[0]["path"]), Topology="client",
                      note="Использовать прежний Run-QuestE2E.ps1; это immutable входы, не launcher")
    if capture_kind == "draw" and target == "windows":
        launch["captureNote"] = "Нужен -RenderDocBridge <native helper>; instrumented capture не заменяет normal GPU замер"
    elif capture_kind != "none":
        launch["captureNote"] = "Quest detailed collector отдельный; mode должен быть включён до нового запуска приложения"
    (out / "launch-args.json").write_text(json.dumps(launch, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return {"status": "prepared-discovery-only" if discovery else "prepared", "runId": spec["runId"],
            "runSpec": str(out / "run-spec.json"), "launchArgs": str(out / "launch-args.json"),
            "reason": "Discovery не публикует workload.ready и не используется как принятый GPU замер" if discovery else None}


def prepare_windows_run(build: Path, views: Path, out: Path, variant, fingerprint,
                        discovery, pair_id, repetition, phase_seconds=10.0, view_name=None, capture_kind="none",
                        render_diag=None):
    return _prepare_run(build, views, out, variant, fingerprint, discovery, pair_id, repetition,
                        phase_seconds, view_name, capture_kind, render_diag=render_diag)


def prepare_quest_run(server_build: Path, quest_build: Path, views: Path, out: Path, variant, fingerprint,
                      discovery, pair_id, repetition, phase_seconds=10.0, view_name=None, capture_kind="none",
                      render_diag=None, head_pose="eye"):
    """Композиция двух неизменённых builder profiles с одинаковым source; приёмка не выводится из SHA."""
    _require(not out.exists(), "Выходной RunSpec bundle уже существует")
    server, quest = _json(server_build), _json(quest_build)
    _require(server.get("sourceSha") == quest.get("sourceSha") and isinstance(server.get("sourceSha"), str)
             and re.fullmatch(r"[0-9a-f]{40}", server["sourceSha"]), "Windows/Quest source SHA не совпали")
    selected = []
    for source, profile_name in ((server, "e2e"), (quest, "quest")):
        profiles = [row for row in source.get("profiles", []) if row.get("profile") == profile_name]
        _require(len(profiles) == 1 and profiles[0].get("success") is True
                 and profiles[0].get("actual", {}).get("developmentBuild") is True, "Нужны успешные Test e2e/quest profiles")
        selected.append(profiles[0])
    dirty = [(source.get("dirty") or {}).get("value") for source in (server, quest)]
    dirty_value = True if True in dirty else False if all(value is False for value in dirty) else None
    dirty_entries = {}
    for source in (server, quest):
        for entry in (source.get("dirty") or {}).get("entries", []):
            # Сборочные checkout у e2e и quest разные: сравниваются evidence (status/path/sha256), не путь checkout.
            evidence = _dirty_evidence(entry)
            key = evidence["path"]
            _require(key not in dirty_entries or _dirty_evidence(dirty_entries[key]) == evidence,
                     "Один dirty path имеет разные evidence в двух build roots; плоская композиция неоднозначна")
            dirty_entries.setdefault(key, entry)
    combined = {"schemaVersion": 1, "sourceSha": server["sourceSha"], "sourceKind": "unknown",
                "sourceKindReason": "Композиция исходных builder profiles; accepted/checkpoint proof не подменяется Git SHA",
                "dirty": {"value": dirty_value, "entries": list(dirty_entries.values()),
                          "reason": "В одном из исходных manifests dirty неизвестен" if dirty_value is None else None,
                          "scope": "Композиция исходных e2e/quest dirty evidence; полные manifests сохранены отдельно"},
                "profiles": selected, "composition": {"kind": "matching-source-builder-profiles",
                    "sources": [{"file": name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                                for name, path in (("server-build.json", server_build), ("quest-build.json", quest_build))]}}
    inputs = out.parent / (out.name + "-build-inputs")
    _require(not inputs.exists(), "Build inputs уже существуют; повтор не перезаписывает результат")
    inputs.mkdir(parents=True)
    (inputs / "server-build.json").write_bytes(server_build.read_bytes())
    (inputs / "quest-build.json").write_bytes(quest_build.read_bytes())
    (inputs / "build.json").write_text(json.dumps(combined, ensure_ascii=False, allow_nan=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    # Build root может уже не хранить generated lock (Build-Game возвращает его к индексу): допускается сохранённая
    # рядом с замороженными manifests копия source-evidence/packages-lock.json; её SHA256 сверяет _prepare_run.
    saved_lock = server_build.parent / "source-evidence" / "packages-lock.json"
    if saved_lock.is_file():
        (inputs / "source-evidence").mkdir()
        (inputs / "source-evidence" / "packages-lock.json").write_bytes(saved_lock.read_bytes())
    result = _prepare_run(inputs / "build.json", views, out, variant, fingerprint, discovery, pair_id,
                          repetition, phase_seconds, view_name, capture_kind, target="quest", render_diag=render_diag,
                          head_pose=head_pose)
    for name in ("server-build.json", "quest-build.json"):
        (out / name).write_bytes((inputs / name).read_bytes())
    return result
