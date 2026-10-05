"""Сопоставляет снятые кадры MEF/SDK/паков; нормы качества и игровые позы не меняет."""
import html
import json
import argparse
import statistics
from collections import defaultdict
from pathlib import Path
from datetime import datetime, timezone

ROOT = Path('Temp/HandPoseFit/weapon-comparison')
ZONES = ('thumb', 'index', 'middle', 'ring', 'little', 'palm_wrist')

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def metrics(report):
    zones = report['Zones']
    area = sum(z['SampledAreaMm2'] for z in zones)
    near = sum(z['SampledAreaMm2'] * z['NearSurfaceFraction'] for z in zones)
    normal = [(z['SampledAreaMm2'] * z['NearSurfaceFraction'], z['MeanNearNormalOpposition']) for z in zones if z['MeanNearNormalOpposition'] is not None]
    normal_area = sum(a for a, n in normal)
    return dict(NearFraction=near / area, NearAreaMm2=near, HandAreaMm2=area,
                Opposition=sum(a*n for a, n in normal) / normal_area if normal_area else None,
                UnknownSignFraction=sum(z['UnknownSignAreaMm2'] for z in zones) / area,
                CrossingFraction=report['IntersectingHandTriangles'] / report['HandTriangles'],
                SelfPairs=report['HandSelfIntersectionPairs'],
                MaxSamplingDeltaPp=max(abs(z['NearSurfaceFractionDelta'])*100 for z in report['SamplingConvergence']),
                ZoneNear={z['Zone']: z['NearSurfaceFraction'] for z in zones},
                ZoneP50={z['Zone']: z['DistanceP50Mm'] for z in zones},
                ContactShares={z['Zone']: z['SampledAreaMm2']*z['NearSurfaceFraction']/near if near else 0 for z in zones})

def load_cases(strict=True):
    rows = read(ROOT/'catalog.json')
    originals = read(ROOT/'pack-catalog.json')
    metadata = {r['Case']: r for r in rows}
    metadata.update({r['Case']: {**r, 'Avatar': 'original', 'Side': 'Left' if r['Side']==0 else 'Right'} for r in originals})
    cases = []
    for path in sorted(ROOT.glob('*/report.json')):
        report = read(path)
        meta = metadata[path.parent.name]
        assert not report['Error'], (path, report['Error'])
        s = report['Settings']
        assert report['AlgorithmVersion']=='mesh-fit-0.3-precision'
        assert s['SampleCount']==3000 and s['CheckSamplingConvergence'] and s['Seed']==42
        assert s['ProximityMm']==2 and s['ContactDistanceMinMm']==0 and not s['PalmarOnly']
        assert not s['ContactBoundsEnabled'] and not s['ContactRegions'] and not s['ContactRendererPaths']
        assert len(report['Cameras'])==24 and all((path.parent/c['Image']).is_file() for c in report['Cameras'])
        if strict and meta['Avatar']=='original':
            assert read(path.parent/'source-render-verified.json')['CaptureVersion']==2, path
        role = 'support' if 'Pump' in meta.get('Grabbable','') else meta['Role']
        pair = path.parent/'other-hand-contact.json'
        other = read(pair) if pair.exists() else None
        other_area = sum(z['AreaMm2'] for z in other['Zones']) if other else 0
        cases.append(dict(Case=path.parent.name, Weapon=meta['Weapon'], Avatar=meta['Avatar'], Role=role,
                          Side=meta['Side'], Directory=path.parent.as_posix(), **metrics(report),
                          OtherHandNearFraction=sum(z['AreaMm2']*z['NearOtherHandFraction'] for z in other['Zones'])/other_area if other_area else None,
                          OtherHandSource=other['Source'] if other else None,
                          CaptureSource=report['CaptureSource'], ObjectScale=report['ObjectScale'], ObjectScaleAxes=report['ObjectScaleAxes'],
                          AvatarIdentity=report['Avatar'], ObjectIdentity=report['Object'], PoseIdentity=report['Pose'],
                          HandTriangles=report['HandTriangles'], ObjectTriangles=report['ObjectTriangles'],
                          PoseType=report['PoseType'], Blend=report['AppliedBlend'], AlignToController=report['AlignToControllerEnabled'],
                          Settings=s, Zones=report['Zones'], Segments=report['Segments'], Joints=report['Joints'], Surfaces=report['Surfaces'],
                          Reliability=report['Reliability'], Findings=report['Findings']))
    return cases, rows, originals

def preliminary():
    cases, rows, originals = load_cases(False)
    native = { (c['Weapon'],c['Role'],c['Avatar']): c for c in cases
              if c['Side']==('Right' if c['Role']=='primary' else 'Left') }
    for weapon in sorted({c['Weapon'] for c in cases if c['Avatar']=='mef'}):
        for role in ('primary','support'):
            m=native.get((weapon,role,'mef')); o=native.get((weapon,role,'original'))
            if not m: continue
            values=lambda c: '-' if not c else f"near {c['NearFraction']*100:.2f}% normal {c['Opposition'] if c['Opposition'] is not None else 0:.3f} cross {c['CrossingFraction']*100:.2f}% other {c['OtherHandNearFraction']*100 if c['OtherHandNearFraction'] is not None else -1:.1f}%"
            print(f'{weapon:16} {role:7} MEF {values(m)} | source {values(o)}')

def summary_profile(cases):
    def stats(key):
        values=[c[key] for c in cases if c[key] is not None]
        return dict(min=min(values), median=statistics.median(values), max=max(values)) if values else None
    return dict(Count=len(cases), **{key:stats(key) for key in ('NearFraction','Opposition','CrossingFraction','SelfPairs','MaxSamplingDeltaPp')})

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--preliminary',action='store_true')
    args=parser.parse_args()
    if args.preliminary:
        preliminary();return
    cases, rows, originals=load_cases()
    requests=read(ROOT/'requests.json')
    assert len(cases)==len(requests)+len(originals)==123
    for r in requests:
        assert (ROOT/Path(r['OutputDirectory']).name/'report.json').exists()
    sdk=read(Path('Docs/audit/artifacts/hand-pose-fit-sdk-references-2026-10-04.json'))
    reference_cases=[dict(Case=r['Case'],Avatar=r['AvatarName'],Weapon=r['ObjectName'],Role='sdk-nonweapon',Side=r['Settings']['Side'],NearFraction=r['WholeHandNearFraction'],Opposition=r['MeanNormalOpposition'],CrossingFraction=r['IntersectingHandTriangleFraction'],SelfPairs=r['HandSelfIntersectionPairs'],MaxSamplingDeltaPp=r['MaxSamplingNearDeltaPp']) for r in sdk['Cases']]
    native={(c['Weapon'],c['Role'],c['Avatar']):c for c in cases if c['Side']==('Right' if c['Role']=='primary' else 'Left')}
    pairs=[]
    for original in (c for c in cases if c['Avatar']=='original'):
        mef=native[(original['Weapon'],original['Role'],'mef')]
        delta={key:mef[key]-original[key] if mef[key] is not None and original[key] is not None else None for key in ('NearFraction','Opposition','CrossingFraction','OtherHandNearFraction')}
        pairs.append(dict(Weapon=original['Weapon'],Role=original['Role'],Mef=mef['Case'],Original=original['Case'],Deltas=delta,
                          Interpretation='descriptive_different_hand_geometry_and_weapon_composition_or_scale'))
    profiles={}
    for avatar in ('mef','original','cyborg','bighands'):
        for role in ('primary','support'):
            selected=[c for c in cases if c['Avatar']==avatar and c['Role']==role and c['Side']==('Right' if role=='primary' else 'Left')]
            profiles[avatar+'-'+role]=summary_profile(selected)
    missing=[r for r in rows if r['Avatar']=='mef' and r['Role'] in ('primary','support') and r['Reason']]
    pair_facts={}
    for role in ('primary','support'):
        selected=[p for p in pairs if p['Role']==role]
        pair_facts[role]=dict(Count=len(selected),LowerOpposition=sum(p['Deltas']['Opposition']<0 for p in selected),
                             HigherNearFraction=sum(p['Deltas']['NearFraction']>0 for p in selected),
                             HigherCrossingFraction=sum(p['Deltas']['CrossingFraction']>0 for p in selected))
    evidence=dict(CreatedUtc=datetime.now(timezone.utc).isoformat(),AlgorithmVersion='mesh-fit-0.3-precision',
                  CalibrationStatus='descriptive_comparison_no_quality_score',Cases=cases,NativePairs=pairs,PairFacts=pair_facts,Profiles=profiles,
                  PriorSdkReferences=reference_cases,ExcludedMefGripRecords=missing,Requests=requests,OriginalCatalog=originals,Inventory=rows,
                  Counts=dict(Reports=len(cases),Png=len(cases)*24,Mef=sum(c['Avatar']=='mef' for c in cases),Original=len(originals),SdkWeapon=sum(c['Avatar'] in ('cyborg','bighands') for c in cases),SdkNonWeapon=len(reference_cases)),
                  Limits=['Статические saved grip transforms / кадры авторских клипов; AlignToController и двухручный IK не воспроизведены.',
                          'Рука, состав оружия и масштаб отличаются: NativePairs не являются ReferenceComparison accepted.',
                          'Открытые/самопересекающиеся меши оставляют знак неизвестным; crossing не измеряет глубину.',
                          'Near0–2mm unsigned: рост может означать проникновение, а не улучшение.',
                          'Исходная поддержка пистолета может касаться другой кисти; saved pair MEF не равен runtime two-hand grip.',
                          'Сырые числа пересечений нельзя сравнивать без учёта плотности/складок разных перчаток.'])
    artifact=Path('Docs/audit/artifacts/hand-pose-fit-mef-comparison-2026-10-04.json')
    artifact.write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding='utf-8')
    gallery(evidence)
    # Для отдельного node --check: проверка JS не требует браузера или сервера.
    document=(ROOT/'index.html').read_text(encoding='utf-8')
    (ROOT/'gallery-script-check.js').write_text(document.split('<script>',1)[1].split('</script>',1)[0],encoding='utf-8')
    print(json.dumps(dict(Counts=evidence['Counts'],PairFacts=pair_facts,Profiles=profiles,Pairs=len(pairs),UnknownSignCases=sum(c['UnknownSignFraction']>0 for c in cases),NegativeNormalMef=sum(c['Avatar']=='mef' and c['Opposition'] is not None and c['Opposition']<0 for c in cases),Gallery=str(ROOT/'index.html'),Artifact=str(artifact)),ensure_ascii=False,indent=2))

def gallery(evidence):
    small=[{k:c[k] for k in ('Case','Weapon','Avatar','Role','Side','NearFraction','Opposition','CrossingFraction','UnknownSignFraction','SelfPairs','MaxSamplingDeltaPp','OtherHandNearFraction','ContactShares','ZoneNear','ZoneP50')} for c in evidence['Cases']]
    payload=json.dumps(small,ensure_ascii=False).replace('</','<\\/')
    document='''<!doctype html><html lang="ru"><meta charset="utf-8"><title>Хваты MEF / оригиналы / SDK</title>
<style>body{font:15px system-ui;background:#10151d;color:#dbe6f0;max-width:1600px;margin:24px auto;padding:0 22px}h1{font-size:27px}p{line-height:1.5;color:#aebdcb}select,button{font:inherit;background:#253342;color:#eef5fa;border:1px solid #607285;padding:7px;border-radius:5px}a{color:#7ad7ea}table{border-collapse:collapse;width:100%;font-size:13px}td,th{padding:9px;border-bottom:1px solid #314253;text-align:left}th{background:#1b2734;position:sticky;top:0;cursor:pointer}tr:hover{background:#233448}small{color:#a5bbcd}.panels{display:grid;grid-template-columns:1fr 1fr;gap:18px}.panel{background:#18222e;padding:14px;border-radius:10px}.panel img{max-width:100%;width:512px}.controls{display:flex;gap:12px;flex-wrap:wrap;margin:18px 0}.warn{color:#ffc681}summary{cursor:pointer;padding:10px}.chips{display:flex;gap:20px;flex-wrap:wrap;background:#1b2734;padding:14px}.chips b{font-size:24px}@media(max-width:900px){.panels{grid-template-columns:1fr}table{font-size:11px}}</style>
<h1>MEF: основной хват и поддержка</h1><p>Измерено 123 случая: MEF 66, исходные Hands/KINEMATION 33, Cyborg/BigHands на SDK-оружии проекта 24. Ещё 24 неоружейных SDK-эталона используются как описательный фон. Контакт — близость поверхности 0–2 мм, 3000 → 6000 точек.</p>
<div class="chips"><span><b>17</b> оружий MEF</span><span><b>33</b> сопоставления с исходным кадром</span><span><b>2952</b> PNG</span><span class="warn">Итоговый балл не назначен</span></div>
<p>Контакт unsigned: включает точки с неизвестной стороной поверхности. Пересечение граней включает касание и не равно проникновению. Плотность меша/перчатка/масштаб отличаются. Поддержка пистолета может касаться основной руки. Отдельные static кадры не подтверждают контроллерное выравнивание или двухручный IK в игре.</p>
<h2>Изображения и точки контакта</h2><div class="controls"><label>Оружие <select id="weapon"></select></label><label>Роль <select id="role"><option value="primary">Основная</option><option value="support">Поддержка</option></select></label><label>Вид <select id="view"><option>front</option><option>back</option><option>left</option><option>right</option><option>top</option><option>bottom</option></select></label><label>Режим <select id="mode"><option>normal</option><option>xray</option><option>distance</option><option selected>section</option></select></label></div><div class="panels" id="panels"></div>
<p>Жёлтый — близость с неизвестной стороной; зелёный — известная внешняя; красный — известная внутренняя; голубое кольцо — соответствующая точка предмета. Подписи ракурсов относятся к осям соответствующего источника и не гарантируют одинакового направления камеры у пака и игрового префаба.</p>
<h2>Все измерения</h2><div class="controls"><label>Источник <select id="avatar"><option value="all">Все</option><option value="mef" selected>MEF</option><option value="original">Оригиналы</option><option value="cyborg">Cyborg</option><option value="bighands">BigHands</option></select></label><label>Выборка <select id="subset"><option value="native">Основная Right / поддержка Left</option><option value="all">Обе стороны</option></select></label></div><p>Нажмите заголовок для сортировки. Opp = +1 для встречных нормалей, −1 для одинаково направленных; это сигнал для просмотра, не порог качества. Δ — максимальное изменение доли контакта по зоне при удвоении выборки, в п.п.</p>
<table><thead><tr><th data-key="Case">Случай / отчёт</th><th data-key="NearFraction">Близость %</th><th data-key="Opposition">Opp</th><th data-key="CrossingFraction">Граней пересек. %</th><th data-key="UnknownSignFraction">Знак ? %</th><th data-key="OtherHandNearFraction">Другая кисть %</th><th data-key="MaxSamplingDeltaPp">Δ п.п.</th><th>Доли близости по пальцам</th></tr></thead><tbody id="rows"></tbody></table>
<details><summary>Пределы сравнения</summary><p>Оригиналы: авторская правая основная / левая поддержка, time=0 выбранного клипа. Для оружейных SMR используются все веса костей × bindpose в мировых координатах. Другая кисть оригинала измерена в том же кадре. Для MEF другая кисть измерена по двум независимо сохранённым grip transforms: игровой IK сюда не входит. Обычные static отчёты остаются одиночными.</p><p>SDK оружие Gun / Machinegun / Shotgun / Grenade в нашем проекте: явные штатные записи Cyborg/BigHands. MEF default/no explicit grip отмечены отдельно в JSON, не выставлены плохими позами. Перенесённые позы зеркалятся для противоположной руки: пары с оригиналом используют только авторскую сторону.</p><p><a href="../sdk-reference-library/index.html">Неоружейные SDK-эталоны и контрольные смещения</a></p></details>
<script>const cases=__DATA__;const $=id=>document.getElementById(id), pct=x=>x==null?'?':(100*x).toFixed(2), num=x=>x==null?'?':x.toFixed(3);const native=c=>c.Side===(c.Role==='primary'?'Right':'Left');let sort='Case',descending=false;const weapons=[...new Set(cases.filter(c=>c.Avatar==='mef').map(c=>c.Weapon))].sort();$('weapon').innerHTML=weapons.map(w=>`<option>${w}</option>`).join('');$('weapon').value='Herrington';function panels(){const w=$('weapon').value,r=$('role').value;const selected=['mef','original'].map(a=>cases.find(c=>c.Weapon===w&&c.Role===r&&c.Avatar===a&&native(c)));$('panels').innerHTML=selected.map((c,i)=>c?`<div class="panel"><b>${i?'Исходный пак':'MEF'} — ${c.Weapon} / ${c.Role} / ${c.Side}</b><p>Близость ${pct(c.NearFraction)}%; Opp ${num(c.Opposition)}; пересечений ${pct(c.CrossingFraction)}%<br>Контакт с другой кистью ${pct(c.OtherHandNearFraction)}%</p><a href="${c.Case}/index.html">24 ракурса</a> · <a href="${c.Case}/report.json">JSON</a><br><img src="${c.Case}/${$('view').value}-${$('mode').value}.png"></div>`:`<div class="panel">Нет отдельного измерения ${i?'оригинала':'MEF'} для этой роли.</div>`).join('')}function rows(){let values=cases.filter(c=>($('avatar').value==='all'||c.Avatar===$('avatar').value)&&($('subset').value==='all'||native(c)));values.sort((a,b)=>(typeof a[sort]==='string'?a[sort].localeCompare(b[sort]):((a[sort]??-999)-(b[sort]??-999)))*(descending?-1:1));$('rows').innerHTML=values.map(c=>`<tr><td><a href="${c.Case}/index.html">${c.Case}</a><br><small>${c.Role}</small></td><td>${pct(c.NearFraction)}</td><td>${num(c.Opposition)}</td><td>${pct(c.CrossingFraction)}</td><td>${pct(c.UnknownSignFraction)}</td><td>${pct(c.OtherHandNearFraction)}</td><td>${c.MaxSamplingDeltaPp.toFixed(2)}</td><td><small>${['thumb','index','middle','ring','little','palm_wrist'].map(z=>`${z}: ${pct(c.ContactShares[z]??0)}%`).join('<br>')}</small></td></tr>`).join('')}['weapon','role','view','mode'].forEach(id=>$(id).onchange=panels);['avatar','subset'].forEach(id=>$(id).onchange=rows);document.querySelectorAll('th[data-key]').forEach(t=>t.onclick=()=>{descending=sort===t.dataset.key?!descending:false;sort=t.dataset.key;rows()});panels();rows();</script></html>'''
    (ROOT/'index.html').write_text(document.replace('__DATA__',payload),encoding='utf-8')

if __name__=='__main__':
    main()
