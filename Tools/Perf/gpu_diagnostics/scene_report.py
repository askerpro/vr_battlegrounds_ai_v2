"""Автономный структурный просмотр scene.json без run.json и GPU-таймингов."""

import hashlib
import html
import json
import math
from pathlib import Path

from .model import PackageError


def _esc(value):
    return html.escape(str(value), quote=True)


def _require(condition, reason):
    if not condition:
        raise PackageError(reason)


def _text(value):
    return isinstance(value, str) and bool(value.strip())


def _load(source):
    def unique(pairs):
        value = {}
        for key, item in pairs:
            _require(key not in value, f"scene.json: повторное поле {key}")
            value[key] = item
        return value

    def finite(value):
        if isinstance(value, float):
            _require(math.isfinite(value), "scene.json: NaN/Inf недопустимы; unknown должен быть null")
        elif isinstance(value, dict):
            for item in value.values():
                finite(item)
        elif isinstance(value, list):
            for item in value:
                finite(item)

    try:
        raw = source.read_bytes()
        scene = json.loads(raw.decode("utf-8-sig"), object_pairs_hook=unique)
        finite(scene)
    except (OSError, UnicodeError, ValueError, RecursionError) as error:
        raise PackageError(f"Не удалось прочитать scene.json: {error}") from error
    _require(isinstance(scene, dict), "scene.json: требуется object")
    _require(type(scene.get("schemaVersion")) is int and scene["schemaVersion"] == 1,
             "scene.json: требуется schemaVersion=1")
    for key in ("objects", "resources", "lights"):
        _require(isinstance(scene.get(key), list), f"scene.json.{key}: требуется array")
        for item in scene[key]:
            _require(isinstance(item, dict) and _text(item.get("id")) and _text(item.get("path")),
                     f"scene.json.{key}: каждый элемент требует object/id/path")
    for key in ("source", "sceneDetail", "observations"):
        _require(scene.get(key) is None or isinstance(scene[key], dict), f"scene.json.{key}: требуется object/null")
    _require(isinstance(scene.get("lodGroups", []), list), "scene.json.lodGroups: требуется array")
    return scene, raw


def _anchor(identity):
    return "id-" + hashlib.sha256(identity.encode("utf-8")).hexdigest()


def _table(headers, rows):
    if not rows:
        return '<p class="muted">Нет данных</p>'
    return ('<div class="scroll"><table><thead><tr>' + ''.join(f'<th>{_esc(h)}</th>' for h in headers) +
            '</tr></thead><tbody>' + ''.join('<tr>' + ''.join(f'<td>{cell}</td>' for cell in row) + '</tr>'
                                             for row in rows) + '</tbody></table></div>')


def _all_fields(value):
    return ('<details><summary>Все исходные поля</summary><pre>' +
            _esc(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False)) + '</pre></details>')


def _array(row, key):
    value = row.get(key)
    _require(value is None or isinstance(value, list), f"{key}: требуется array/null")
    return value or []


def _rows(row, key):
    items = _array(row, key)
    _require(all(isinstance(item, dict) for item in items), f"{key}: требуются object элементы")
    return items


def render_scene_report(source: Path, out: Path) -> Path:
    """Создаёт новый каталог с HTML и точной локальной копией исходного scene.json."""
    source, out = Path(source).resolve(), Path(out).resolve()
    _require(source.is_file(), f"scene.json отсутствует: {source}")
    _require(not out.exists(), f"Каталог просмотра уже существует; укажите новый: {out}")
    scene, raw = _load(source)
    targets, destinations, consumers, pages, catalog = {}, {}, {}, {}, []
    records_per_page = 32

    def register(identity, filename):
        _require(_text(identity), "Структурный элемент требует непустой id")
        _require(identity not in targets, f"Неоднозначный повторный id: {identity}")
        targets[identity] = _anchor(identity)
        destinations[identity] = filename + "#" + targets[identity]

    for key in ("objects", "resources", "lights"):
        for index, item in enumerate(scene[key]):
            register(item["id"], f"{key}-{index // records_per_page + 1:04}.html")
    for index, item in enumerate(scene["objects"]):
        for renderer in _rows(item, "renderers"):
            identity = renderer.get("identity")
            _require(isinstance(identity, dict), "renderer.identity: требуется object")
            register(identity.get("id"), f"objects-{index // records_per_page + 1:04}.html")
        for identity in _array(item, "resourceIds"):
            _require(_text(identity), "object.resourceIds: требуются строки")
            consumers.setdefault(identity, []).append(item["id"])
    for index, group in enumerate(scene.get("lodGroups", [])):
        _require(isinstance(group, dict) and isinstance(group.get("identity"), dict),
                 "lodGroups: требуется object с identity")
        register(group["identity"].get("id"), f"lod-{index // records_per_page + 1:04}.html")

    def show(value, reference=False):
        if value is None:
            return '<span class="muted">нет данных</span>'
        if isinstance(value, list):
            return '<div class="values">' + ''.join('<div>' + show(v, reference) + '</div>' for v in value) + '</div>' if value else '[]'
        if isinstance(value, dict) and _text(value.get("id")):
            return show(value["id"], True) + ('<span class="muted"> ' + _esc(value.get("path") or value.get("name") or "") + '</span>')
        if isinstance(value, str) and reference:
            label = '<code>' + _esc(value) + '</code>'
            return f'<a href="{destinations[value]}">{label}</a>' if value in targets else label + ' <span class="muted">unresolved</span>'
        if isinstance(value, bool):
            return 'да' if value else 'нет'
        if isinstance(value, dict):
            return '<pre>' + _esc(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False)) + '</pre>'
        return _esc(value)

    def fields(row, keys):
        return _table(["Поле", "Исходное значение"], [[_esc(key), show(row.get(key))] for key in keys])

    def heading(identity, path, label):
        return (f'<h3>{_esc(path or label)}</h3><div class="identity">' + show(identity, True) +
                f' <button type="button" data-copy="{_esc(identity)}">Копировать ID</button></div>')

    def index_record(identity, path, kind, label):
        catalog.append({"id": identity, "path": path or label, "kind": kind,
                        "href": destinations[identity], "label": label})

    def card(row, body, identity=None, path=None):
        identity = identity or row["id"]
        path = path or row.get("path")
        filename = destinations[identity].split("#", 1)[0]
        kind = filename.split("-", 1)[0]
        label = row.get("type") or row.get("name") or {"objects": "GameObject", "lod": "LODGroup", "resources": "Resource", "lights": "Light"}[kind]
        index_record(identity, path, kind, label)
        article = (f'<article class="record" id="{targets[identity]}">' +
                heading(identity, path or row.get("path"), row.get("name", identity)) + body + _all_fields(row) + '</article>')
        pages.setdefault(filename, []).append(article)

    def levels(value):
        if value is None:
            return show(None)
        _require(isinstance(value, list), "LOD levels: требуется array/null")
        rows = []
        for level in value:
            if level is None:
                rows.append([show(None)] * 4)
                continue
            _require(isinstance(level, dict), "LOD level: требуется object/null")
            rows.append([show(level.get("index")), show(level.get("screenRelativeTransitionHeight")),
                         show(level.get("fadeTransitionWidth")), show(level.get("renderers"), True)])
        return _table(["Индекс в этом списке", "Порог высоты экрана", "Fade width", "Renderer IDs"], rows)

    meta = scene.get("source") or {}
    mode = meta.get("mode") if meta.get("mode") in ("editTime", "runtime") else "unknown"
    digest = hashlib.sha256(raw).hexdigest()
    parts = ['<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">',
             '<title>Структурный просмотр сцены · VR Battlegrounds</title><style>' + _CSS + '</style><body><main>',
             '<header><p class="eyebrow">VR BATTLEGROUNDS · SCENE MANIFEST v1</p><h1>Структура сцены</h1>',
             '<p>structural-only · источник: <strong>' + _esc(mode) + '</strong></p>',
             '<p>Геометрия, связи ресурсов, LOD и свет из одного scene.json. GPU-время, actual draw/pass, '
             'видимость и выбранный камерой LOD этим просмотром не измеряются.</p></header>',
             '<nav><a href="#passport">Источник</a><a href="#objects">Объекты</a><a href="#resources">Ресурсы</a>'
             '<a href="#lod">LOD</a><a href="#lights">Свет</a></nav>',
             '<section id="passport"><h2>Источник и ограничения</h2>',
             _table(["Поле", "Значение"], [["Файл", _esc(str(source))], ["SHA-256 точных bytes scene.json", '<code>' + digest + '</code>'],
                 ["Локальное evidence", '<a href="raw/scene.json" download="scene.json">Скачать исходный scene.json</a>'],
                 ["Capture ID", show(scene.get("captureId"))], ["Режим", _esc(mode)]]),
             fields(meta, ("scenePath", "sceneName", "sceneDirty", "unityVersion", "capturedUtc", "frame", "realtime",
                           "sceneFile", "globalTextureMipmapLimit", "lodBias", "authoringIdentityPolicy")),
             '<p class="muted">SHA подтверждает bytes экспортированного JSON. Это не hash APK и не подтверждение GPU capture. '
             'Asset paths показаны как текст: просмотр не открывает внешние файлы или URL.</p>',
             '<h3>Ограничения источника</h3>',
             fields(scene.get("observations") or {}, ("reason", "snapshotAvailable", "mappingQuality")),
             fields(scene.get("sceneDetail") or {}, ("unavailableReason",)),
             '<p class="muted">Полный snapshot ниже сохраняет исходные limitations и неоднозначные записи; '
             'отсутствующие original/effective связи не восстанавливаются по authored LOD.</p>',
             _all_fields(scene.get("observations")), _all_fields(scene.get("sceneDetail")),
             '<p>editTime показывает авторское состояние. runtime показывает состояние на момент экспорта; '
             'его runLocal IDs требуют отдельной сверки с authoring manifest. Null сохраняет неизвестность.</p></section>',
             '<div class="search"><label for="search">Поиск по path, ID и исходным полям</label>'
             '<input id="search" type="search" placeholder="Объект, mesh, материал, текстура…">'
             '<select id="kind"><option value="">Все типы</option><option value="objects">Объекты</option>'
             '<option value="renderers">Рендереры</option><option value="resources">Ресурсы</option>'
             '<option value="lod">LOD</option><option value="lights">Свет</option></select>'
             '<span id="found" aria-live="polite"></span><span id="copied" role="status"></span></div>',
             f'<p>{len(scene["objects"])} объектов · {len(scene["resources"])} ресурсов · '
             f'{len(scene.get("lodGroups", []))} LOD-групп · {len(scene["lights"])} источников света.</p>',
             '<p class="muted">Поиск по имени/пути/ID. Полные исходные поля и связи открываются на '
             'странице записи; все записи сохранены. Каталог работает без сети.</p>',
             '<section><h2>Каталог сцены</h2><div id="results"></div><div class="pager">'
             '<button id="previous" type="button">← Назад</button> <span id="page"></span> '
             '<button id="next" type="button">Далее →</button></div></section>']
    for item in scene["objects"]:
        body = fields(item, ("activeSelf", "activeInHierarchy", "identityConfidence", "prefabSource"))
        body += '<h4>Связанные ресурсы</h4>' + show(item.get("resourceIds"), True)
        body += '<h4>LOD groups</h4>' + show(item.get("lodGroups"), True)
        for renderer in _rows(item, "renderers"):
            identity = renderer["identity"]["id"]
            index_record(identity, item.get("path"), "renderers", renderer.get("type") or "Renderer")
            body += f'<div class="renderer" id="{targets[identity]}">' + heading(identity, None, renderer.get("type"))
            body += fields(renderer, ("enabled", "isPartOfStaticBatch", "subMeshStartIndex", "submeshRange", "submeshRangeReason",
                                      "shadowCastingMode", "receiveShadows", "sceneDetailStateReason", "projectionReason"))
            body += '<h4>Shared mesh</h4>' + show(renderer.get("mesh"), True)
            body += _table(["Material slot", "Материал"], [[show(slot.get("slot")), show(slot.get("material"), True)]
                           for slot in _rows(renderer, "materialSlots")])
            body += _table(["Submesh", "Mesh", "Slot", "Material", "Связь", "Actual draw"], [
                [show(binding.get("submesh")), show(binding.get("mesh"), True), show(binding.get("materialSlot")),
                 show(binding.get("material"), True), show(binding.get("mappingQuality")) + '<br>' + show(binding.get("reason")),
                 show(binding.get("actualDraw"))] for binding in _rows(renderer, "submeshBindings")])
            body += _all_fields(renderer) + '</div>'
        card(item, body)
    for item in scene["resources"]:
        body = fields(item, ("type", "identityConfidence", "guid", "localId", "contentHash", "contentHashMeaning"))
        body += '<h4>Объекты, ссылающиеся на ресурс</h4>' + show(consumers.get(item["id"]), True)
        if "vertexCount" in item:
            body += fields(item, ("vertexCount", "geometryCountScope", "indexFormat", "isReadable"))
            body += _table(["Submesh", "Index count", "Topology"], [[show(row.get(key)) for key in ("index", "indexCount", "topology")]
                           for row in _rows(item, "submeshes")])
        if "shader" in item:
            body += '<h4>Shader</h4>' + show(item.get("shader"), True)
            body += fields(item, ("renderQueue", "keywords", "enableInstancing", "passCount", "passes"))
            body += _table(["Texture property", "Текстура", "Scale", "Offset"], [
                [show(binding.get("property")), show(binding.get("texture"), True), show(binding.get("scale")), show(binding.get("offset"))]
                for binding in _rows(item, "textureBindings")])
        if "width" in item:
            body += fields(item, ("width", "height", "depthOrLayers", "dimension", "graphicsFormat", "format", "mipmapCount",
                                  "mipmapLimitGroup", "ignoreMipmapLimit", "activeMipmapLimit", "mipMapBias", "filterMode", "wrapMode", "anisoLevel"))
        card(item, body)
    for group in scene.get("lodGroups", []):
        identity = group["identity"]["id"]
        body = '<h4>Объект</h4>' + show(group.get("object"), True)
        body += fields(group, ("enabled", "effectiveSource", "originalMeaning", "effectiveToOriginal", "mappingVerified", "reason",
                               "arenaEdgeDistance", "arenaDistanceSource", "band", "rule", "projectionReason"))
        for key, label in (("authored", "Authored — editTime"), ("original", "Original — сохранённый baseline"),
                           ("effective", "Effective — текущий список")):
            body += '<h4>' + label + '</h4>' + levels(group.get(key))
        card(group, body, identity, group["identity"].get("path"))
    for item in scene["lights"]:
        body = '<h4>Объект</h4>' + show(item.get("object"), True)
        body += fields(item, ("type", "enabled", "activeInHierarchy", "lightmapBakeType", "bakingOutput", "intensity", "range",
                              "color", "shadows", "shadowStrength", "shadowResolution", "shadowCustomResolution", "cullingMask",
                              "renderingLayerMask", "casters", "shadowAtlasAllocation", "captureEvents", "unresolvedReason"))
        body += '<h4>Cookie</h4>' + show(item.get("cookie"), True)
        card(item, body)
    # Сразу загружается только компактный каталог. Подробности доступны на локальных страницах.
    catalog_json = json.dumps(catalog, ensure_ascii=False, allow_nan=False, separators=(",", ":"))
    catalog_json = catalog_json.replace("&", "\\u0026").replace("<", "\\u003c").replace(">", "\\u003e")
    parts.extend(['<script type="application/json" id="catalog-data">', catalog_json,
                  '</script></main><script>', _SCRIPT, _COPY_SCRIPT, '</script></body></html>'])
    rendered = '\n'.join(parts)
    # Все входные данные уже разобраны и отрисованы до первой записи. Evidence не меняется.
    try:
        out.mkdir(parents=True, exist_ok=False)
        (out / "raw").mkdir()
        (out / "raw" / "scene.json").write_bytes(raw)
        for filename, records in pages.items():
            detail = ('<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" '
                      'content="width=device-width,initial-scale=1"><title>Записи сцены · VR Battlegrounds</title>'
                      '<style>' + _CSS + '</style><body><main><nav><a href="index.html">← Каталог сцены</a>'
                      '<a href="raw/scene.json" download="scene.json">Исходный scene.json</a></nav>'
                      '<h1>Записи сцены</h1><p>Структурные данные · не GPU timing и не подтверждение draw.</p>'
                      '<span id="copied" role="status"></span>' + '\n'.join(records) +
                      '</main><script>' + _COPY_SCRIPT + '</script></body></html>')
            (out / filename).write_text(detail, encoding="utf-8", newline="\n")
        destination = out / "index.html"
        destination.write_text(rendered, encoding="utf-8", newline="\n")
    except OSError as error:
        raise PackageError(f"Не удалось сохранить структурный просмотр: {error}") from error
    return destination


_CSS = """
:root{color-scheme:dark;font:15px/1.5 system-ui;background:#111820;color:#e3ebf1}*{box-sizing:border-box}
body{margin:0}main{max-width:1400px;margin:auto;padding:24px}h1{font-size:2.4rem;margin:8px 0}h2{margin-top:0}
a{color:#8ed9e2;overflow-wrap:anywhere}nav{display:flex;gap:18px;flex-wrap:wrap;margin:24px 0}
section,article{border:1px solid #354450;border-radius:10px;padding:18px;margin:18px 0;background:#17212b}
article{background:#1c2833}h3{overflow-wrap:anywhere}.renderer{border-left:3px solid #617f96;margin:16px 0;padding:0 12px}
.muted{color:#a6b6c5}.eyebrow{letter-spacing:.12em;color:#8ed9e2}.identity{font-size:.85rem;overflow-wrap:anywhere}
button,input,select{font:inherit;background:#101923;color:inherit;border:1px solid #65879a;border-radius:5px;padding:7px}
button{cursor:pointer}.search{position:sticky;top:0;background:#111820;padding:16px 0;z-index:2;display:flex;gap:12px;align-items:center;flex-wrap:wrap}
input{flex:1;min-width:240px}.scroll{overflow:auto}table{width:100%;border-collapse:collapse;font-size:.9rem}
th,td{padding:8px;text-align:left;vertical-align:top;border-bottom:1px solid #354450}th{color:#b3d5e3}
pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:.8rem;max-height:34rem;overflow:auto}code{overflow-wrap:anywhere}
details{margin-top:14px}summary{cursor:pointer;color:#b3d5e3}[hidden]{display:none!important}:target{outline:2px solid #91dbe2;scroll-margin-top:100px}
.result{padding:10px 14px;margin:8px 0}.result h3{font-size:1rem;margin:0}.result code{font-size:.76rem}.pager{display:flex;gap:12px;align-items:center;margin-top:18px}
"""

_SCRIPT = """
const records=JSON.parse(document.getElementById('catalog-data').textContent);
for(const row of records)row.search=[row.id,row.path,row.label].join(' ').toLocaleLowerCase();
const search=document.getElementById('search'),kind=document.getElementById('kind');
const results=document.getElementById('results'),previous=document.getElementById('previous'),next=document.getElementById('next');
const pageSize=60;let page=0;
function render(){const q=search.value.trim().toLocaleLowerCase();
const matches=records.filter(row=>(!kind.value||row.kind===kind.value)&&row.search.includes(q));
const pages=Math.max(1,Math.ceil(matches.length/pageSize));page=Math.min(page,pages-1);results.replaceChildren();
for(const row of matches.slice(page*pageSize,(page+1)*pageSize)){
const article=document.createElement('article');article.className='result';const title=document.createElement('h3');
const link=document.createElement('a');link.href=row.href;link.textContent=row.path||row.label;title.append(link);
const description=document.createElement('div');description.className='muted';description.textContent=row.kind+' · '+row.label;
const id=document.createElement('code');id.textContent=row.id;article.append(title,description,id);results.append(article);}
document.getElementById('found').textContent=`Найдено: ${matches.length} / ${records.length}`;
document.getElementById('page').textContent=`Страница ${page+1} / ${pages}`;previous.disabled=page===0;next.disabled=page>=pages-1;}
for(const control of [search,kind])control.addEventListener('input',()=>{page=0;render();});
previous.addEventListener('click',()=>{page--;render();});next.addEventListener('click',()=>{page++;render();});
function category(){const name=location.hash.slice(1);if(['objects','resources','lod','lights'].includes(name)){kind.value=name;page=0;}render();}
window.addEventListener('hashchange',category);category();
"""

_COPY_SCRIPT = """
document.addEventListener('click',async event=>{const button=event.target.closest('button[data-copy]');if(!button)return;
const value=button.dataset.copy;try{await navigator.clipboard.writeText(value);}
catch(error){const field=document.createElement('textarea');field.value=value;document.body.appendChild(field);field.select();
let ok=false;try{ok=document.execCommand('copy');}catch(error){}field.remove();
if(!ok){document.getElementById('copied').textContent='Выделите ID и скопируйте вручную';return;}}
document.getElementById('copied').textContent='ID скопирован';});
"""
