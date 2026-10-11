"""Выборочный перенос URP-источников с замыканием GUID и проверкой коллизий."""
import hashlib, json, pathlib, re, shutil

BASE = pathlib.Path(__file__).resolve().parents[3]
CACHE = BASE / 'tasks/lobby-decoration/reports/asset-evaluation/military-base-megapack-372662/urp-selected'
DEST = BASE / 'Assets/Art/Lobby/Imported/MilitaryCamp'
PREFIX = 'Assets/Hivemind/MilitaryCamp/URP/'
manifest = json.loads((CACHE / 'manifest.json').read_text(encoding='utf-8'))
entries = {e['guid']: e for e in manifest['assets'] if e.get('extracted') and e.get('sha256')}
roots = {'SM_AmmoCartridgeBoxes_01','SM_AmmoCartridgeBoxes_02','SM_AmmoCase_01','SM_AmmoCase_02',
 'SM_ArmoredVehicle','SM_Barrel_01','SM_CardboardTarget_01','SM_Chair','SM_ClothesCabinet',
 'SM_Container_a','SM_Fence_long_2m','SM_Fence_long_4m','SM_FuelCanister','SM_Lamp_long',
 'SM_MilitaryCrate_01','SM_MilitaryCrate_02','SM_PropaneTank_01','SM_StrappedContainer',
 'SM_Table','SM_TarpCrate_01','SM_Tent_a','SM_ToolRack','SM_WatchTower_01','SM_cabinet_a','SM_cabinet_b',
 'T_Ground_BC','T_Ground_N','T_Ground_ORMH','T_GroundIntense_BC','T_GroundIntense_N','T_GroundIntense_ORMH',
 'T_Tent_a_BC','T_Tent_a_N','T_Tent_a_ORM','SM_SandBarrier_01','SM_SandBarrier_03',
 'SM_Cliff_01','SM_Cliff_02','SM_Cliff_03','SM_Cliff_04','SM_Rock_01','SM_Rock_02','SM_Rock_03',
 'SM_Container_b','SM_Fence_2m',
 'SM_Building_wall_4x4m_A','SM_Building_wall_2m_A','SM_Building_wall_corner_90d_A','SM_Building_wall_entrance_2m_A',
 'SM_Building_wall_window_2m_A','SM_Building_wall_window_4m_A','SM_Building_roof_4x4_01','SM_Building_roof_4x4_02',
 'SM_Door_single','SM_Hangar_wall_8x6_a','SM_Hangar_wall_window_a','SM_Hangar_entrance',
 'SM_Hangar_roof','SM_Hangar_WallRoof','SM_Container_a_door_left','SM_Container_a_door_right',
 'SM_Container_b_door_left','SM_Container_b_door_right_','SM_Door_lamp_a','SM_AlarmLight','SM_Tent_b',
 'SM_Concrete_beam_3m_A','SM_Concrete_pile_A','SM_Concrete_pile_B','SM_Concrete_support_A',
 'SM_Concrete_Barier_01','SM_Concrete_Barier_02','SM_pipe_01','SM_pipe_02_a',
 'SM_pipe_15','SM_pipe_1','SM_pipe_04_a','SM_pipe_05',
 # 2026-10-11: реквизит стройки, стрельбища, мастерской и транспортной зоны.
 'SM_CinderBlocks_01','SM_CinderBlocks_02','SM_CinderBlocks_03','SM_CinderBlocks_04','SM_CinderBlocks_05',
 'SM_CinderBlocks_06','SM_CinderBlocks_07','SM_CinderBlocks_08','SM_HescoBastion_01','SM_AmmoCartridgeBoxes_03',
 'SM_MilitaryCrate_03','SM_WoodCrate_big_01','SM_WoodCrate_big_02','SM_WoodCrate_small_01','SM_WoodCrate_small_02',
 'SM_Generator_01','SM_Generator_02','SM_MobileGenerator_01','SM_MobileGenerator_02','SM_SandBarrier_02',
 'SM_SandBarrier_04','SM_SandBarrier_05','SM_SandBarrier_06','SM_Barrel_02','SM_PropaneTank_02','SM_WaterStorageTank',
 'SM_Palete_01','SM_Palete_02','SM_Palete_03','SM_Palete_04','SM_ArmyCot','SM_BunkBed','SM_Stairs_2m_A','SM_InteriorStairs_01',
 # 2026-10-11: бетонная стена за габионами у стрельбища.
 'SM_Wall_4x4','SM_Wall_6x4','SM_Wall_2x4','SM_Wall_Pillar_a','SM_Wall_Concrete_Trim_01',
 'SM_Concrete_Barier_03','SM_Concrete_Barier_04','SM_Concrete_Barier_05','SM_Concrete_Barier_06','SM_Concrete_Barier_07',
 # 2026-10-11: признаки жизни на крышах казарм.
 'SM_Antenna_a','SM_Antenna_c','SM_Antenna_d','SM_Antenna_e','SM_Antenna_f','SM_Antenna_g','SM_Antenna_h',
 'SM_SatelliteDish_01','SM_MilitaryRadio',
 # 2026-10-11: вышки связи по образцу демо-сцены пака.
 'SM_Antenna_i','SM_Antenna_j','SM_Antenna_k',
 # 2026-10-11: пост КПП по образцу демо-сцены пака (без интерьера).
 'SM_Border_2m','SM_Border_90d','SM_Building_roof_1x2_02','SM_Building_roof_2x4_01','SM_Building_wall_1m_A_2','SM_Building_wall_corner_90d_A_2','SM_Building_wall_entrance_2m_B','SM_Building_wall_window_2m_A_2','SM_Building_wall_window_2m_B_2','SM_Cornice_1m_A','SM_Cornice_2m_A','SM_Cornice_3m_A','SM_Cornice_90d_A','SM_Fence_1m','SM_Fence_90d','SM_Tire_02',
 'SM_Tire_01'}
pending = [g for g,e in entries.items() if pathlib.Path(e['path']).stem in roots]
chosen, external = set(), set()
while pending:
    guid = pending.pop()
    if guid in chosen: continue
    chosen.add(guid)
    raw = (CACHE / entries[guid]['path']).read_bytes()
    if b'\0' not in raw[:1000]:
        for dep in re.findall(rb'(?<![0-9a-f])([0-9a-f]{32})(?![0-9a-f])', raw):
            dep = dep.decode()
            if dep in entries: pending.append(dep)
            elif dep != '0'*32: external.add(dep)
main = {}
for meta in (BASE/'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([0-9a-f]{32})', meta.read_text(encoding='utf-8', errors='ignore'), re.M)
    if match: main.setdefault(match[1], []).append(str(meta)[:-5])
for guid in chosen:
    target = DEST / entries[guid]['path'].removeprefix(PREFIX)
    collisions = [p for p in main.get(guid,[]) if pathlib.Path(p) != target]
    if collisions: raise RuntimeError(f'GUID collision {guid}: {collisions}')
rows = []
copied = 0
for guid in sorted(chosen):
    e=entries[guid]; source=CACHE/e['path']; target=DEST/e['path'].removeprefix(PREFIX)
    raw=source.read_bytes()
    if hashlib.sha256(raw).hexdigest().lower()!=e['sha256'].lower(): raise RuntimeError(f'SHA mismatch {source}')
    target.parent.mkdir(parents=True,exist_ok=True)
    reused = target.exists()
    if not reused:
        shutil.copyfile(source,target); shutil.copyfile(str(source)+'.meta',str(target)+'.meta'); copied += 1
    rows.append({'guid':guid,'path':str(target.relative_to(BASE)).replace('\\','/'),'sourceSha256':e['sha256'],
                 'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'reusedOwned':reused})
report=BASE/'tasks/lobby-decoration/reports/implementation/militarycamp-copy.json'
report.write_text(json.dumps({'files':rows,'external':sorted(external),'collisions':0},indent=2),encoding='utf-8')
print(json.dumps({'copiedNew':copied,'resolved':len(rows),'bytes':sum((BASE/r['path']).stat().st_size for r in rows),'collisions':0,'report':str(report)}))
