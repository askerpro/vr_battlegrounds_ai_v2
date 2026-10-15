"""Синтетическая офлайн-проверка анализатора; не доказательство Unity runtime."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("cc1_analyze", HERE / "analyze.py")
analyzer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyzer)


def fixture():
    data = {}
    for name in analyzer.COMBOS:
        scenario = name.split("-")[1] if name.startswith("AB-") else "T07"
        passive = dict.fromkeys(analyzer.PASSIVE_ONE, 1)
        passive.update(dict.fromkeys(analyzer.PASSIVE_ZERO, 0))
        passive.update(role="subject-ghost", passive=True)
        live = dict.fromkeys(analyzer.LIVE, 1)
        live.update(role="subject", passive=False)
        equipment = {"timeline": [
            {"event": "primaryGrab", "what": "PPK", "value": {"source": "anchor"}},
            {"event": "stowedInAnchor:PPK"}, {"event": "primaryGrab", "what": "AK105"},
            {"event": "death"}, {"event": "newBody"}, {"event": "shot", "what": "AK105"}],
            "hands": {"primaryGrabs": 0 if name == "U-unarmed" else 2, "stowsPlaced": 1},
            "unarmed": {"frames": 10, "blazeActiveFrames": 0, "shotRequestFrames": 0, "restPoseFrames": 10},
            "criteria": {"unarmedNoBlazeNoShots": True, "noActionsAfterDeath": True, "ghostDrivenWithoutHandsAim": True},
            "ghost": {"bodies": 1}, "stowDropped": 0}
        logs = dict.fromkeys(("missingAnim", "emptyAnim", "errors", "exceptions"), 0)
        cc1 = {"combo": name, "scenario": scenario, "sampleFrames": 10, "sampleErrors": [],
               "kit": [live, passive], "target": {"frames": 10, "cameraHeightAboveRoot": {"min": 1.65, "max": 1.65, "mean": 1.65}, "rootDriftFromFirstM": 0},
               "proxy": {"frames": 10, "proxies": [{"bornFrame": 1}],
                         "moves": [{"anim": "Move", "proxy": 1, "firstOfProxy": True, "firstMoveOfProxy": True, "from": "Idle", "result": "moved", "latency": 0.1}],
                         "normalizedTime": {"sameStateFrames": 10, "increasingFrames": 10}}, "log": logs}
        data[f"runtime/{name}/summary.json"] = {"cases": [{"Id": scenario, "Status": "NeedsReview", "Error": None,
            "Frames": [{"Feet": [0, 0, 0]}], "Metrics": {"cc1": cc1, "playerFlow": {
                "shots": 0 if name == "U-unarmed" else 1, "hitsOnTarget": 1, "hitRate": 1,
                "foreignDamageEventsOnTarget": 0, "consoleErrors": 0, "equipment": equipment}}}]}
    for phase, names in (("p1", analyzer.COMBOS[:5]), ("p2", analyzer.COMBOS[5:])):
        data[f"runtime/{phase}/queue.json"] = {"state": "completed", "error": None, "notes": [],
            "log": logs.copy(), "combos": [{"combo": name, "status": {"Total": 1, "Completed": 1, "Failed": 0, "InvalidFixture": 0, "Error": None}} for name in names]}
    return data


def run(data):
    with tempfile.TemporaryDirectory(prefix="cc1-analyzer-") as tmp:
        root = Path(tmp)
        for relative, value in data.items():
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(value), encoding="utf-8")
        subprocess.run([sys.executable, "-I", str(HERE / "analyze.py"), tmp, str(root / "no-base")], check=True, capture_output=True)
        return json.loads((root / "verdict.json").read_text(encoding="utf-8"))["checks"]


def main():
    data = fixture()
    key = "proxy A: все Move ≤ 0,2 с"
    missing = copy.deepcopy(data)
    failed = copy.deepcopy(data)
    path = "runtime/AB-T01-ak105/summary.json"
    missing[path]["cases"][0]["Metrics"]["cc1"]["proxy"]["moves"] = []
    failed[path]["cases"][0]["Metrics"]["cc1"]["proxy"]["moves"][0]["result"] = "noMove3s"
    if "--before" in sys.argv:
        assert run(missing)[key] is True
        assert run(failed)[key] is True
        print("REPRODUCED: missing Move and failed Move falsely PASS (2 synthetic cases)")
        return
    good = run(data)
    assert all(value is True for value in good.values()), good
    unknown_start = copy.deepcopy(data)
    unknown_start[path]["cases"][0]["Metrics"]["cc1"]["proxy"]["moves"][0]["from"] = None
    unknown_checks = run(unknown_start)
    assert unknown_checks["proxy A: исходное состояние первого Move измерено"] is False
    assert unknown_checks["proxy A: первый Move ≤ 0,2 с (T01/T06/T08)"] is None
    variants = [("missing Move", missing, key), ("failed Move", failed, key)]
    def variant(label, relative, mutate, check=None):
        changed = copy.deepcopy(data)
        mutate(changed, relative)
        variants.append((label, changed, check))
    variant("missing AB case", path, lambda d, p: d.pop(p), key)
    variant("missing queue", "runtime/p1/queue.json", lambda d, p: d.pop(p))
    variant("failed queue", "runtime/p2/queue.json", lambda d, p: d[p].update(state="failed", error="fault"))
    variant("queue error", "runtime/p1/queue.json", lambda d, p: d[p].update(error="fault"))
    variant("onFinished error", "runtime/p2/queue.json", lambda d, p: d[p].update(notes=["onFinished: fault"]))
    for field in ("errors", "exceptions", "missingAnim", "emptyAnim"):
        variant("phase " + field, "runtime/p1/queue.json", lambda d, p, f=field: d[p]["log"].update({f: 1}))
    variant("missing phase log", "runtime/p1/queue.json", lambda d, p: d[p].pop("log"))
    variant("missing queue entry", "runtime/p1/queue.json", lambda d, p: d[p]["combos"].pop())
    variant("sample error", path, lambda d, p: d[p]["cases"][0]["Metrics"]["cc1"].update(sampleErrors=["fault"]))
    variant("missing observer metrics", path, lambda d, p: d[p]["cases"][0]["Metrics"].pop("cc1"))
    variant("missing playerFlow metrics", path, lambda d, p: d[p]["cases"][0]["Metrics"].pop("playerFlow"))
    variant("missing target metric", path, lambda d, p: d[p]["cases"][0]["Metrics"]["cc1"]["target"].pop("rootDriftFromFirstM"))
    variant("missing case log counter", path, lambda d, p: d[p]["cases"][0]["Metrics"]["cc1"]["log"].pop("errors"))
    variant("first Move from Back", path, lambda d, p: d[p]["cases"][0]["Metrics"]["cc1"]["proxy"]["moves"][0].update({"from": "Back"}), "proxy A: первый Move ≤ 0,2 с (T01/T06/T08)")
    variant("missing first Move of second proxy", path, lambda d, p: d[p]["cases"][0]["Metrics"]["cc1"]["proxy"]["proxies"].append({"bornFrame": 2}), "proxy A: первый Move ≤ 0,2 с (T01/T06/T08)")
    variant("missing stowDropped metric", "runtime/P-purchase-ak105/summary.json", lambda d, p: d[p]["cases"][0]["Metrics"]["playerFlow"]["equipment"].pop("stowDropped"))
    variant("missing unarmed grabs metric", "runtime/U-unarmed/summary.json", lambda d, p: d[p]["cases"][0]["Metrics"]["playerFlow"]["equipment"]["hands"].pop("primaryGrabs"))
    variant("missing normalizedTime counter", path, lambda d, p: d[p]["cases"][0]["Metrics"]["cc1"]["proxy"]["normalizedTime"].pop("increasingFrames"))
    variant("invalid normalizedTime count", path, lambda d, p: d[p]["cases"][0]["Metrics"]["cc1"]["proxy"]["normalizedTime"].update(increasingFrames=11))
    for label, changed, check in variants:
        result = run(changed)
        assert result[check] is False if check else any(value is False for value in result.values()), (label, result)
    print(f"PASS: complete synthetic fixture; rejected {len(variants)} incomplete/faulted synthetic fixtures; unknown initial state marked unmeasured; no Unity runtime proof")


if __name__ == "__main__":
    main()
