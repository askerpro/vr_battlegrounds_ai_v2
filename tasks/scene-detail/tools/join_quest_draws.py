"""Связать рейтинг draw Quest (ranking.json) с мешами/шейдерами по XML кадра RenderDoc.

renderdoccmd convert -c zip.xml даёт последовательность команд кадра. Unity (Development) подписывает
объекты vkSetDebugUtilsObjectNameEXT: буферы — именем меша, пайплайны — именем шейдера, текстуры —
своими именами. Для каждого vkCmdDraw*/vkCmdDrawIndexed берётся текущий bound pipeline/index/vertex
buffer и стек debug-меток; k-й draw кадра сопоставляется k-му draw рейтинга по eid с проверкой числа
треугольников. Имя меша — кандидат (одинаковые меши у разных объектов сцены неразличимы).
"""
import argparse
import json
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


def text_of(chunk, name):
    for el in chunk:
        if el.get("name") == name:
            return el.text
    return None


def ids_in(chunk, array_name):
    for el in chunk:
        if el.get("name") == array_name:
            return [c.text for c in el]
    return []


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("run_dir")
    p.add_argument("--top", type=int, default=40)
    args = p.parse_args()
    run = Path(args.run_dir)
    xml_path = run / "xml" / "frame.zip.xml"
    names = {}
    pipeline_modules = {}
    state = {"pipeline": None, "ib": None, "vb": [], "labels": []}
    frame_draws = []
    for _, chunk in ET.iterparse(xml_path, events=("end",)):
        if chunk.tag != "chunk":
            continue
        n = chunk.get("name")
        if n == "vkSetDebugUtilsObjectNameEXT":
            names[text_of(chunk, "Object")] = text_of(chunk, "ObjectName")
        elif n == "vkCreateGraphicsPipelines":
            # Unity подписывает шейдерные модули, а не пайплайн: пайплайн → его модули.
            modules = [el.text for el in chunk.iter() if el.get("name") == "module"]
            pipeline = text_of(chunk, "Pipeline")
            if pipeline:
                pipeline_modules[pipeline] = modules
        elif n == "vkCmdBindPipeline":
            bp = None
            for el in chunk:
                if el.get("name") == "pipelineBindPoint":
                    bp = el.get("string")
            if bp == "VK_PIPELINE_BIND_POINT_GRAPHICS":
                state["pipeline"] = text_of(chunk, "pipeline")
        elif n == "vkCmdBindIndexBuffer":
            state["ib"] = text_of(chunk, "buffer")
        elif n == "vkCmdBindVertexBuffers":
            state["vb"] = ids_in(chunk, "pBuffers")
        elif n == "vkCmdBeginDebugUtilsLabelEXT":
            label = None
            for el in chunk.iter():
                if el.get("name") == "pLabelName":
                    label = el.text
            state["labels"].append(label)
        elif n == "vkCmdEndDebugUtilsLabelEXT":
            if state["labels"]:
                state["labels"].pop()
        elif n in ("vkCmdDrawIndexed", "vkCmdDraw"):
            if n == "vkCmdDrawIndexed":
                tri = int(text_of(chunk, "indexCount")) // 3
            else:
                tri = int(text_of(chunk, "vertexCount")) // 3
            frame_draws.append({"call": n, "triangles": tri, "instances": int(text_of(chunk, "instanceCount")),
                                "pipeline": state["pipeline"],
                                "indexBuffer": names.get(state["ib"]) if n == "vkCmdDrawIndexed" else None,
                                "vertexBuffers": [names.get(v, v) for v in state["vb"]],
                                "labels": [l for l in state["labels"] if l]})
        chunk.clear()
    ranking_path = run / "ranking.json"
    ranking = json.loads(ranking_path.read_text(encoding="utf-8"))
    by_eid = sorted(ranking["all"], key=lambda r: r["eid"])
    mismatches = []
    if len(by_eid) != len(frame_draws):
        raise SystemExit("число draw не совпало: ranking %d, xml %d" % (len(by_eid), len(frame_draws)))
    for r, f in zip(by_eid, frame_draws):
        if r["triangles"] != f["triangles"]:
            mismatches.append({"eid": r["eid"], "ranking": r["triangles"], "xml": f["triangles"]})
        r["call"] = f["call"]
        module_names = sorted({names[m] for m in pipeline_modules.get(f["pipeline"], []) if m in names})
        r["pipeline"] = f["pipeline"]
        r["shader"] = " + ".join(module_names) if module_names else names.get(f["pipeline"], "pipeline " + str(f["pipeline"]))
        r["mesh"] = f["indexBuffer"] or (f["vertexBuffers"][0] if f["vertexBuffers"] else None)
        r["vertexBuffers"] = f["vertexBuffers"]
        r["labels"] = f["labels"]
    if mismatches:
        raise SystemExit("сопоставление по треугольникам не сошлось: %s" % mismatches[:5])
    total = ranking["totalClocks"]
    agg = {}
    for key in ("shader", "mesh"):
        acc = defaultdict(lambda: {"clocks": 0.0, "draws": 0, "triangles": 0})
        for r in ranking["all"]:
            a = acc[r[key] or "?"]
            a["clocks"] += r["clocks"] or 0
            a["draws"] += 1
            a["triangles"] += r["triangles"] or 0
        rows = [{"name": k, "sharePct": round(100 * v["clocks"] / total, 2), **v} for k, v in acc.items()]
        rows.sort(key=lambda x: -x["clocks"])
        agg[key] = rows
    ranking["joinedFrom"] = str(xml_path)
    ranking["byShader"] = agg["shader"]
    ranking["byMesh"] = agg["mesh"]
    ranking["limitations"] = ranking.get("limitations", []) + [
        "Связь draw→меш/шейдер по debug-именам Unity; одинаковые имена мешей разных объектов не различаются",
        "Combined Mesh (static batching) объединяет несколько объектов сцены в один draw"]
    ranking_path.write_text(json.dumps(ranking, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    top = sorted(ranking["all"], key=lambda r: -(r["clocks"] or 0))[:args.top]
    for r in top:
        print("%2d eid=%-5s %5.1f%% tri=%6s busy=%5.1f tex=%5.1f vtx=%5.1f | %s | %s" % (
            r["rank"], r["eid"], r["sharePct"], r["triangles"], r["shadersBusyPct"] or 0,
            r["textureFetchStallPct"] or 0, r["vertexFetchStallPct"] or 0, r["shader"], r["mesh"]))
    print("--- by shader")
    for x in agg["shader"][:12]:
        print("%5.1f%% draws=%3d tri=%7d %s" % (x["sharePct"], x["draws"], x["triangles"], x["name"]))
    print("--- by mesh")
    for x in agg["mesh"][:15]:
        print("%5.1f%% draws=%3d tri=%7d %s" % (x["sharePct"], x["draws"], x["triangles"], x["name"]))


if __name__ == "__main__":
    main()
