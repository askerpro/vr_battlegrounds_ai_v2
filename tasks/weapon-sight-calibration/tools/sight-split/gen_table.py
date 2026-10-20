# Генератор SightSplitTable.cs из islands.json + islands-2.json (разборы Blender, см. ../Readme.md).
# Запуск: python gen_table.py <папка с islands*.json> <выходной SightSplitTable.cs> table_template.cs.txt
import json, re, sys
sys.stdout.reconfigure(encoding='utf-8')
base = sys.argv[1]   # tasks/weapon-sight-calibration/tools/sight-split (islands.json, islands-2.json)
out = sys.argv[2]
d1 = json.load(open(base + '/islands.json', encoding='utf-8'))
d2 = json.load(open(base + '/islands-2.json', encoding='utf-8'))
TRAVEL = 0.003


def f(x):
    s = repr(float(x))
    if 'e' in s or 'E' in s:
        s = '%.9e' % float(x)
    return s + 'f'


def v3(a): return 'new Vector3(%s, %s, %s)' % (f(a[0]), f(a[1]), f(a[2]))
def q4(a): return 'new Quaternion(%s, %s, %s, %s)' % (f(a[0]), f(a[1]), f(a[2]), f(a[3]))
def parse_yaml_vec(s): return [float(m) for m in re.findall(r'[xyzw]: ([-0-9.eE]+)', s)]
def cs(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


def sel(i):
    s = i['selector']
    return 'S(%s, %d, %d, %s, %s)' % (cs(i['role']), s['unity_vertices'], s['triangles'], v3(s['window_min']), v3(s['window_max']))


def part(role, node, mesh_path, mesh_name, nv, nt, movable, point, point_pref, shift, islands):
    td = f(TRAVEL) if movable else '0f'
    sels = ''.join(',\n                    ' + sel(i) for i in islands)
    return ('P(SightPartRole.%s, %s, %s, %s, %d, %d, %s,\n                  %s, %s, %s, %s, %s%s)'
            % (role, cs(node), cs(mesh_path), '%dL' % mesh_name, nv, nt, 'true' if movable else 'false',
               v3(point), v3(point_pref) if point_pref else 'null', td, td,
               f(shift) if (movable and shift is not None) else 'null', sels))


weapons = []
# islands.json — Kinemation .asset; подвижная половина выбрана по механике оружия и решению «сведение — ходом столбика».
cfg1 = {
    'AK105': ('AK105', 'Front', {'RearSight': 'MeshContainer/Base', 'FrontSight': 'MeshContainer/Base'},
              'AK-105: высота сводится мушкой (столбик, остров 49); целик-планка — неподвижная опорная точка'),
    'Mk14': ('Mk14', 'Rear', {'RearSight': 'MeshContainer/Base', 'FrontSight': 'MeshContainer/Base'},
             'Mk14: высота — диоптр целика (острова 13+11); мушка M14 по высоте неподвижна'),
    'Viper': ('Viper', 'Rear', {'RearSight': 'Slide/Bolt', 'FrontSight': 'MeshContainer/Base'},
              'Viper: регулируемый целик на затворе (острова 3+4+2, едет с Slide/Bolt); мушка в пазу неподвижна по высоте'),
    'R08': ('R08', 'Rear', {'RearSight': 'MeshContainer/Base', 'FrontSight': 'MeshContainer/Base'},
            'R08: регулируемый целик (острова 1+21); мушка неподвижна'),
}
counts = d1['meta']['asset_counts']
for wid, (folder, moving, nodes, note) in cfg1.items():
    w = d1['weapons'][wid]
    ps = {}
    for key, role in (('RearSight', 'Rear'), ('FrontSight', 'Front')):
        p = w['parts'][key]
        asset = p['asset']
        c = counts[asset]
        mesh_path = 'Assets/Art/Weapons/Kinemation/%s/Meshes/%s.asset' % (folder, asset)
        ps[role] = part(role, nodes[key], mesh_path, 4300000, c['vertices'], c['triangles'], role == moving,
                        p['aim_point']['point'], None, None, p['islands'])
    weapons.append((wid, 'Assets/Prefabs/Weapons/%s/%s.prefab' % (folder, folder), note, [], ps))

# islands-2.json — подвижность и подъёмы из разбора; исключены ShotgunReal (не калибруется), M16 (будет комплект),
# SRM12/SniperRifle/MKR9 (донорские половины пересадит пользователь комплектом).
skip = {'ShotgunReal', 'M16', 'SRM12', 'SniperRifle', 'MKR9'}
notes2 = {
    'MP5K': 'MP5K: высота — столбик мушки (остров 16); барабан целика неподвижен',
    'Uzi': 'Uzi: мушка двигается целым островом 10 вместе с защитными ушками (решение пользователя)',
    'Herrington': 'Remington 11-87: подвижная передняя лопатка (острова 1+2), V-целик неподвижен (решение пользователя)',
    'Revolver': 'Revolver: подвижный целик (острова 2+4); мушка — гребень ствола, только точка',
    'Gun': 'Browning Hi-Power: подвижная мушка (остров 2), обе половины в затворе Slide/Gate',
    'PPK': 'PPK: прицел — одна оболочка с затвором; без подвижной детали, только точки (решение пользователя)',
    'Scar': 'SCAR-L: сначала подъём рамок целика и мушки, затем подвижный столбик мушки (остров 2)',
}
for wid, w in d2['weapons'].items():
    if wid in skip:
        continue
    root = w['prefab'].split('/')[-1][:-len('.prefab')]
    ps = {}
    raises = []
    shift = w.get('zeroing', {}).get('required_height_shift_mm')
    for key, role in (('RearSight', 'Rear'), ('FrontSight', 'Front')):
        p = w['parts'][key]
        mesh = p['mesh']
        node = mesh['node']
        assert node.split('/')[0] == root, (wid, node, root)
        node = node[len(root) + 1:]
        # Подвижность и состав подвижной детали — из w['movable'] (уровень ствола): у Herrington нет флага в части,
        # у Scar в части перечислена вся рамка мушки, а двигается только столбик (остров 2).
        wm = w.get('movable', {})
        movable = wm.get('part') == key and bool(wm.get('islands'))
        islands = p.get('islands', [])
        if movable:
            islands = [i for i in islands if i['island_index_in_asset_order'] in set(wm['islands'])]
            assert len(islands) == len(wm['islands']), (wid, key)
        if wid == 'MP5K' and role == 'Rear':
            stays = set(p.get('stays_in_body', []))
            islands = [i for i in islands if i['island_index_in_asset_order'] not in stays]
        if wid == 'PPK':
            movable = False
            islands = []
        ap = p['aim_point']
        ps[role] = part(role, node, mesh['mesh_asset'], int(mesh['mesh_local_file_id']), mesh['vertices'], mesh['triangles'], movable,
                        ap['point'], ap['point_prefab_m'], shift, islands)
        if 'raise' in p:
            r = p['raise']
            rn = r['node'][len(root) + 1:]
            cur = r['current_local']
            raises.append('R(%s, %s, %s, %s, %s, %s, %s, %s)' % (
                cs(rn), v3(parse_yaml_vec(cur['position'])), q4(parse_yaml_vec(cur['rotation'])),
                v3(r['raised_local']['position']), q4(r['raised_local']['rotation_xyzw']),
                v3(r['axis_point_prefab_m']), v3(r['axis_dir_prefab']), f(r['angle_deg'])))
    weapons.append((wid, w['prefab'], notes2[wid], raises, ps))

lines = []
for wid, prefab, note, raises, ps in weapons:
    rs = 'null' if not raises else 'new[]\n                {\n                    ' + ',\n                    '.join(raises) + '\n                }'
    lines.append('            new SightSplitSpec(%s, %s,\n                %s,\n                %s,\n                %s,\n                %s)'
                 % (cs(wid), cs(prefab), cs(note), rs, ps['Rear'], ps['Front']))

TEMPLATE = open(sys.argv[3], encoding='utf-8').read()
src = TEMPLATE.replace('/*ENTRIES*/', ',\n'.join(lines))
open(out, 'w', encoding='utf-8', newline='\n').write(src)
print('weapons', [w[0] for w in weapons])
