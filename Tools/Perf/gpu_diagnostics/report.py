"""Автономный русский HTML: measured данные не смешиваются с атрибуцией capture."""
import hashlib
import html
import json
import math
import os
from pathlib import Path, PureWindowsPath
from urllib.parse import quote


def esc(value):
    return html.escape(str(value), quote=True)


def number(value):
    if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value):
        return f"{value:.3f}".rstrip("0").rstrip(".")
    return "нет данных"


def anchor(side, kind, value):
    return side + "-" + kind + "-" + hashlib.sha256(str(value).encode("utf-8")).hexdigest()[:16]


def safe_raw_link(raw_path, package, output):
    """Разрешать только существующий data artifact внутри исходного package."""
    if not isinstance(raw_path, str) or not raw_path or ":" in raw_path or "\\" in raw_path:
        return None
    path = Path(raw_path)
    if path.is_absolute() or PureWindowsPath(raw_path).is_absolute() or ".." in path.parts:
        return None
    root = Path(package).resolve()
    target = (root / path).resolve()
    if not target.is_relative_to(root) or not target.is_file():
        return None
    if target.suffix.lower() not in {".txt", ".json", ".jsonl", ".csv", ".rdc", ".png", ".jpg", ".jpeg"}:
        return None
    try:
        return quote(Path(os.path.relpath(target, Path(output).resolve())).as_posix(), safe="/")
    except ValueError:
        return None


def table(headers, rows):
    if not rows:
        return '<p class="muted">нет данных</p>'
    return '<div class="scroll"><table><thead><tr>' + ''.join(f"<th>{esc(h)}</th>" for h in headers) + \
        '</tr></thead><tbody>' + ''.join('<tr>' + ''.join(f"<td>{c}</td>" for c in row) + '</tr>' for row in rows) + \
        '</tbody></table></div>'


def json_detail(value):
    return '<details><summary>Все поля</summary><pre>' + esc(json.dumps(value, ensure_ascii=False, indent=2)) + '</pre></details>'


def link_id(side, kind, value, known):
    label = '<code>' + esc(value) + '</code>'
    if value in known:
        return f'<a href="#{anchor(side, kind, value)}">{label}</a>'
    return label + ' <span class="muted">unresolved</span>'


def timeline(a, b, metric_id):
    available = [(side, run.get("metrics", {}).get(metric_id, {})) for side, run in (("A", a), ("B", b))]
    values = [s["value"] for _, metric in available for s in metric.get("samples", [])]
    if not values:
        return '<p class="muted">Timeline: сырых samples нет; summary-only не восстанавливает ряд.</p>'
    top = max(max(values), 0.001)
    paths = []
    for side, metric in available:
        samples = metric.get("samples", [])
        if not samples:
            continue
        start = min(s["start"] for s in samples)
        end = max(s["end"] for s in samples)
        span = max(end - start, 0.001)
        stride = max(1, math.ceil(len(samples) / 1200))
        shown = samples[::stride]
        if shown[-1] is not samples[-1]:
            shown = shown + [samples[-1]]
        points = ' '.join(f'{45 + 900 * (s["start"]-start)/span:.2f},{155 - 130*s["value"]/top:.2f}' for s in shown)
        paths.append(f'<polyline class="line{side}" points="{points}"/><text x="50" y="{180 if side == "A" else 198}">'
                     f'{side}: {number(end-start)} с, {len(samples)} samples; показан каждый {stride}</text>')
    return (f'<svg viewBox="0 0 980 220" role="img" aria-label="{esc(metric_id)}: реальные samples A и B">' \
        f'<path class="axis" d="M45 20 V155 H945"/><text x="3" y="20">{number(top)}</text>' + ''.join(paths) + \
        '</svg><p class="muted">X: время от начала каждого ряда; часы разных прогонов не совмещены. '
        'Уменьшение числа точек относится только к рисунку, статистика использует все samples.</p>')


def render_report(comparison, out):
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    a, b = dict(comparison.get("a", {})), dict(comparison.get("b", {}))
    for run in (a, b):
        for key in ("scene", "attribution", "metrics", "conditions"):
            run[key] = run.get(key) or {}
    sections = ['<header><p class="eyebrow">VR BATTLEGROUNDS · GPU DIAGNOSTICS v1</p>',
                '<h1>Как правка изменила GPU</h1>',
                '<p>Обычный app GPU и отдельные подробные capture. Ресурсы помогают искать причину; '
                'их вклад не складывается в стоимость кадра.</p></header>',
                '<nav><a href="#conditions">Условия</a><a href="#metrics">Измерения</a>'
                '<a href="#groups">Проходы и группы</a><a href="#scene">Объекты и ресурсы</a>'
                '<a href="#next">Следующий эксперимент</a></nav>']
    status = comparison.get("status", "invalid")
    reasons = comparison.get("reasons", [])
    visible_reasons = list(dict.fromkeys(reasons[:3] + [r for r in reasons if r.startswith("Несовпадение")]))
    sections.append(f'<section class="verdict {esc(status)}"><h2>{esc(status)}</h2>'
                    '<p>Статус относится к сопоставимости этой пары, а не к приёмке бюджета.</p><ul>' +
                    ''.join(f'<li>{esc(r)}</li>' for r in visible_reasons) + '</ul>' +
                    ('<details><summary>Все причины и ограничения (' + str(len(reasons)) + ')</summary><ul>' +
                     ''.join(f'<li>{esc(r)}</li>' for r in reasons) + '</ul></details>' if len(reasons) > len(visible_reasons) else '') + '</section>')
    sections.append('<section id="conditions"><h2>Паспорт и условия</h2>')
    passport = []
    for side, run in (("A", a), ("B", b)):
        passport.append([side, esc(run.get("runId", "нет данных")), esc(run.get("variant", "нет данных")),
                         esc(run.get("mode", "нет данных")), esc(run.get("path", "нет данных"))])
    sections.append(table(["", "Run ID", "Вариант", "Режим", "Источник package"], passport))
    source_rows = []
    for side, run in (("A", a), ("B", b)):
        source, integrity = run.get("source") or {}, run.get("sourceIntegrity") or {}
        raw_link = safe_raw_link(source.get("path"), run.get("path", ""), out)
        source_rows.append([side, esc(source.get("kind", "нет данных")),
                            esc(integrity.get("status", "unverified")),
                            '<code>' + esc(source.get("sha256", "нет данных")) + '</code>',
                            f'<a href="{esc(raw_link)}">Исходный файл</a>' if raw_link else 'нет локального evidence'])
    sections.append(table(["", "Источник", "Проверка SHA256", "Объявленный SHA256", "Evidence"], source_rows))
    ca, cb = a.get("conditions", {}), b.get("conditions", {})
    rows = []
    allowed = comparison.get("allowedConditions", [])
    for key in sorted(set(ca) | set(cb)):
        va, vb = ca.get(key), cb.get(key)
        hint = "изменение разрешено явно" if key in allowed else ("различается" if va != vb else "")
        rows.append([esc(key), esc(json.dumps(va, ensure_ascii=False)), esc(json.dumps(vb, ensure_ascii=False)), esc(hint)])
    sections.append(table(["Поле", "A", "B", "Проверка"], rows))
    sections.append('<p>Quest 2: <strong>notMeasured</strong>. Подробные Windows-выводы требуют отдельной '
                    'калибровки против Quest 3. Автоматического коэффициента пересчёта нет.</p></section>')
    sections.append('<section id="metrics"><h2>Измерения A/B</h2><p>Δ = B − A; положительное время — рост. '
                    'Одна пара показывает наблюдаемую дельту всего workload. '
                    'Повторы и A/A noise ещё не подтверждены этим отчётом.</p>')
    metrics = comparison.get("metrics", {})
    rows = []
    for mid, m in metrics.items():
        ma, mb = a.get("metrics", {}).get(mid, {}), b.get("metrics", {}).get(mid, {})
        rows.append([f'<a href="#{anchor("m", "metric", mid)}">{esc(mid)}</a>',
                     esc(m.get("scope")), esc(m.get("unit")), number(m.get("a")), number(m.get("b")),
                     number(ma.get("p95")), number(mb.get("p95")), number(m.get("delta")), number(m.get("deltaPercent")),
                     f'{esc(ma.get("count", "?"))} / {esc(mb.get("count", "?"))}'])
    sections.append(table(["Метрика", "Scope", "Unit", "A p50", "B p50", "A p95", "B p95", "Δ", "Δ %", "N A/B"], rows))
    for mid in sorted(set(a.get("metrics", {})) | set(b.get("metrics", {}))):
        secondary = mid not in {"app_gpu_ms", "cpu_ms", "compositor_gpu_ms"}
        if secondary:
            sections.append(f'<details><summary>{esc(mid)} · provenance и samples</summary>')
        sections.append(f'<h3 id="{anchor("m", "metric", mid)}">{esc(mid)}</h3>')
        for side, run in (("A", a), ("B", b)):
            metric = run.get("metrics", {}).get(mid, {})
            provenance = {k: metric.get(k) for k in ("source", "scope", "unit", "cadence", "aggregation", "availability")}
            label = 'summary-only' if metric.get("sourceSummaryOnly") else 'raw samples'
            sections.append(f'<p>{side}: <strong>{label}</strong> · {esc(provenance)}</p>')
        sections.append(timeline(a, b, mid))
        if secondary:
            sections.append('</details>')
    sections.append('<h3>Охват видов</h3>')
    expected = set()
    measured = set()
    for run in (a, b):
        c = run.get("conditions", {})
        if isinstance(c.get("expectedViews"), list):
            expected.update(str(v) for v in c["expectedViews"])
        if c.get("viewId"):
            measured.add(str(c["viewId"]))
    if expected:
        sections.append(table(["Вид", "Данные этой пары"], [[esc(v), "есть" if v in measured else "notMeasured"] for v in sorted(expected)]))
    else:
        sections.append('<p>Стандартный ViewsFile не объявлен: охват всей сцены — notMeasured. '
                        'Один ракурс не подтверждает худший вид или бюджет сцены.</p>')
    sections.append('</section><section id="groups"><h2>Проходы и группы</h2>'
                    '<p>Это отдельный scope capture, не обычный бюджет. Значения разных unit/scope/source '
                    'не ранжируются вместе. Inclusive/overlap значения не суммируются.</p>')
    groups_a = a.get("attribution", {}).get("groups", [])
    groups_b = b.get("attribution", {}).get("groups", [])
    sections.append('<h3>Дорогая работа baseline A</h3>')
    buckets = {}
    for group in groups_a:
        key = tuple(str(group.get(k, "unknown")) for k in ("unit", "scope", "source", "metric"))
        buckets.setdefault(key, []).append(group)
    for key, groups in sorted(buckets.items()):
        sections.append('<h4>' + esc(' · '.join(key)) + '</h4>')
        ordered = sorted(groups, key=lambda g: -(g.get("value") if isinstance(g.get("value"), (int, float)) else -math.inf))
        sections.append(table(["Группа", "Значение", "Связь"], [[f'<a href="#{anchor("A", "group", g.get("id"))}">{esc(g.get("name", g.get("id")))}</a>', number(g.get("value")), esc(g.get("confidence", "unresolved"))] for g in ordered]))
    if not buckets:
        sections.append('<p>нет данных: GPU capture/group attribution не импортирован.</p>')
    sections.append('<h3>Изменения групп A/B</h3>')
    group_delta_allowed = status not in {"invalid", "incomparable"}
    if not group_delta_allowed:
        sections.append('<p>Дельты групп заблокированы: прогоны не прошли проверку сопоставимости.</p>')
    # Capture-local event/group IDs не являются cross-run identity.
    gb = {g["comparisonKey"]: g for g in groups_b if g.get("comparisonKey")}
    deltas = {}
    for ga in groups_a if group_delta_allowed else []:
        other = gb.get(ga.get("comparisonKey"), {})
        keys = ("unit", "scope", "source", "metric")
        if all(ga.get(k) is not None and ga.get(k) == other.get(k) for k in keys) and \
                all(isinstance(g.get("value"), (int, float)) for g in (ga, other)):
            key = tuple(str(ga[k]) for k in keys)
            deltas.setdefault(key, []).append((ga, other, other["value"] - ga["value"]))
    for key, values in sorted(deltas.items()):
        sections.append('<h4>' + esc(' · '.join(key)) + '</h4>')
        sections.append(table(["Группа", "A", "B", "Δ"], [[esc(ga.get("name", ga["id"])), number(ga["value"]), number(gb_["value"]), number(delta)] for ga, gb_, delta in sorted(values, key=lambda v: -abs(v[2]))]))
    if not deltas:
        sections.append('<p>нет данных: нет совпавших group ID/provenance для числовой дельты.</p>')
    for side, run in (("A", a), ("B", b)):
        scene = run.get("scene", {})
        objects = {o.get("id") for o in scene.get("objects", [])}
        resources = {o.get("id") for o in scene.get("resources", [])}
        for group in run.get("attribution", {}).get("groups", []):
            sections.append(f'<article id="{anchor(side, "group", group.get("id"))}"><h3>{side}: '
                            f'{esc(group.get("name", group.get("id")))}</h3><code>{esc(group.get("id"))}</code> '
                            '<button class="copy" type="button">Копировать ID</button>')
            sections.append('<p>Contributors: ' + ', '.join(link_id(side, "object", oid, objects) for oid in group.get("contributors", [])) + '</p>')
            sections.append('<p>Resources: ' + ', '.join(link_id(side, "resource", rid, resources) for rid in group.get("resourceIds", [])) + '</p>')
            sections.append('<p>Capture events: ' + ', '.join(esc(e) for e in group.get("eventIds", [])) + '</p>')
            raw = safe_raw_link(group.get("rawPath"), run.get("path", ""), out)
            sections.append(f'<p><a href="{esc(raw)}">Raw evidence</a></p>' if raw else '<p class="muted">Raw evidence: unresolved</p>')
            sections.append(json_detail(group) + '</article>')
    sections.append('</section><section id="scene"><h2>Объекты, ресурсы и свет</h2>'
                    '<p>Структурные сведения не являются измерением стоимости объекта. '
                    'Observed LOD и expected LOD должны оставаться разными полями. '
                    'Цветом выделены различия данных одинакового stable ID.</p><input id="filter" '
                    'placeholder="Фильтр по пути, GUID или ID" aria-label="Фильтр объектов и ресурсов">')
    count = 0
    for kind, collection in (("object", "objects"), ("resource", "resources"), ("light", "lights")):
        other = {o.get("id"): o for o in b.get("scene", {}).get(collection, [])}
        before = {o.get("id"): o for o in a.get("scene", {}).get(collection, [])}
        for side, run in (("A", a), ("B", b)):
            for obj in run.get("scene", {}).get(collection, []):
                count += 1
                changed = before.get(obj.get("id")) != other.get(obj.get("id"))
                sections.append(f'<article class="item {"changed" if changed else ""}" id="{anchor(side, kind, obj.get("id"))}">'
                                f'<h3>{side} · {kind} · {esc(obj.get("path", obj.get("id")))}</h3>'
                                f'<code>{esc(obj.get("id"))}</code> <button class="copy" type="button">Копировать ID</button>')
                resources = {r.get("id") for r in run.get("scene", {}).get("resources", [])}
                sections.append('<p>' + ', '.join(link_id(side, "resource", rid, resources) for rid in obj.get("resourceIds", [])) + '</p>')
                sections.append(json_detail(obj) + '</article>')
    if not count:
        sections.append('<p>нет данных: scene/resource/light index не импортирован; атрибуция unresolved.</p>')
    sections.append('</section><section id="next"><h2>Наблюдения и следующий эксперимент</h2>')
    observations = a.get("attribution", {}).get("observations", []) + b.get("attribution", {}).get("observations", [])
    sections.append(table(["Наблюдение", "Уверенность", "Следующий контролируемый эксперимент", "Владелец"],
                          [[esc(o.get(k, "нет данных")) for k in ("observation", "confidence", "nextExperiment", "owner")] for o in observations]))
    sections.append('<p>Проверить A/A разброс и повторить согласованные A/B пары в обратном порядке. '
                    'При отсутствии resource/capture связи выбрать один контролируемый вариант; '
                    'связь и изменение работы подтвердить отдельным capture.</p>'
                    '<p><a href="comparison.json">Полный comparison.json</a></p></section>')
    css = """*{box-sizing:border-box}body{margin:0;background:#111821;color:#dce6ef;font:16px/1.5 system-ui,sans-serif}main{max-width:1400px;margin:auto;padding:40px 24px}h1{font-size:42px;margin:5px 0 14px}h2{margin-top:0}h3{font-size:20px}.eyebrow{letter-spacing:.12em;color:#80d5bf;font-size:12px}header p{max-width:900px}nav{display:flex;flex-wrap:wrap;gap:20px;margin:26px 0}a{color:#7fd8fb}section{border:1px solid #344353;border-radius:12px;padding:22px;margin:22px 0;background:#18222e}article{border-top:1px solid #344353;padding:15px 8px;scroll-margin-top:20px}.changed{border-left:4px solid #f3bd68;padding-left:15px}.verdict{border-left:6px solid #e6b960}.comparable{border-left-color:#73d4b0}.invalid,.incomparable{border-left-color:#ef807c}.muted{color:#a5b6c6}.scroll{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:14px}th,td{text-align:left;border-bottom:1px solid #344353;padding:10px 12px;vertical-align:top}th{color:#acbdcc}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px}code{font-size:13px;overflow-wrap:anywhere}button,input{background:#243647;color:#dce6ef;border:1px solid #557186;padding:8px;border-radius:5px}button{cursor:pointer;margin-left:8px}input{width:min(600px,100%)}svg{width:100%;max-height:300px;background:#101923;border-radius:6px}svg text{fill:#a5b6c6;font-size:12px}.axis{stroke:#71869a;fill:none}.lineA,.lineB{fill:none;stroke-width:1.3}.lineA{stroke:#68d7ba}.lineB{stroke:#eeae66}@media(max-width:700px){main{padding:18px 10px}h1{font-size:30px}section{padding:14px}th,td{padding:8px}}"""
    script = """document.querySelectorAll('.copy').forEach(b=>b.addEventListener('click',async()=>{const id=b.previousElementSibling.textContent;try{await navigator.clipboard.writeText(id);b.textContent='Скопировано'}catch{window.prompt('Скопируйте ID',id)}}));document.getElementById('filter').addEventListener('input',e=>{const q=e.target.value.toLowerCase();document.querySelectorAll('.item').forEach(i=>i.hidden=!i.textContent.toLowerCase().includes(q))});"""
    document = '<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">' \
        '<title>GPU диагностика A/B</title><style>' + css + '</style><main>' + ''.join(sections) + \
        '</main><script>' + script + '</script></html>\n'
    path = out / "index.html"
    path.write_text(document, encoding="utf-8", newline="\n")
    return path
