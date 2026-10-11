"""Ограниченный статический аудит и расчёт footprint по native-источникам; Unity не запускается."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[4])
args = parser.parse_args()
repo = args.repo
code_path = repo / 'Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyCheckpointZoneBuilder.cs'
code = code_path.read_text(encoding='utf-8')
report_dir = repo / 'tasks/lobby-decoration/reports/implementation/checkpoint-zone'
report_dir.mkdir(parents=True, exist_ok=True)
native_path = repo / 'tasks/lobby-decoration/reports/implementation/paid-native-import.json'
native = json.loads(native_path.read_text(encoding='utf-8-sig'))
tower = next(row for row in native if row['name'] == 'SM_WatchTower_01')
scale = 8 / tower['size'][1]
footprint = [tower['size'][0] * scale, tower['size'][2] * scale]
tower_x = [34 - footprint[0] / 2, 34 + footprint[0] / 2]
tower_z = [-31 - footprint[1] / 2, -31 + footprint[1] / 2]
sources = [
    'SM_WatchTower_01', 'SM_Building_wall_4x4m_A', 'SM_Building_wall_window_2m_A',
    'SM_Building_roof_4x4_01', 'SM_Door_single', 'SM_Fence_long_4m',
    'SM_Concrete_Barier_01', 'SM_Concrete_Barier_02', 'SM_Door_lamp_a',
]
prefab_root = repo / 'Assets/Art/Lobby/Imported/MilitaryCamp'
checks = {
    'required_prefabs_unique': all(len(list(prefab_root.rglob(stem + '.prefab'))) == 1 for stem in sources),
    'api_build_present': 'Build(Transform parent, Bounds arena, Bounds tent)' in code,
    'world_mesh_matrix_measurement': 'filter.transform.localToWorldMatrix' in code and 'renderer.bounds' not in code,
    'transaction_rollback_present': 'DestroyImmediate(root.gameObject)' in code and 'AssetDatabase.DeleteAsset(path)' in code,
    'no_debug_or_runtime_loop': all(token not in code for token in ['Debug.Log', 'void Update(', 'void FixedUpdate(']),
    'tower_uniform_height': '8f / native.size.y' in code and 'Vector3.one * scale' in code,
    'road_declared_24_32': 'Envelope(origin, 24, 32, -38, -27, 12)' in code,
    'tower_predicted_inside_reserve': tower_x[0] >= 32.2 and tower_x[1] <= 35.9 and tower_z[0] >= -33 and tower_z[1] <= -29,
    'tower_predicted_outside_road': tower_x[0] > 32,
    'gate_predicted_clear_road': 23.7 + .475830078 / 2 < 24 and 32.3 - .475830078 / 2 > 32,
    'barriers_predicted_clear_road': 23.62 + .614501953 / 2 < 24 and 35.3 - .614501953 / 2 > 32,
}
result = {
    'scope': 'static_source_audit_and_prediction_from_existing_native_reports_not_scene_execution',
    'sha256': hashlib.sha256(code_path.read_bytes()).hexdigest(),
    'checks': checks,
    'passed': sum(checks.values()), 'total': len(checks),
    'source_evidence': str(native_path.relative_to(repo)),
    'tower_prediction': {'uniform_scale': scale, 'height': 8, 'footprint_xz': footprint, 'x': tower_x, 'z': tower_z, 'road_gap': tower_x[0] - 32},
    'pending': ['native_build_and_world_bounds', 'actual_lod0_cost', 'art_review', 'Unity_Quest_acceptance'],
}
(report_dir / 'offline-audit.json').write_text(json.dumps(result, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps(result, ensure_ascii=False))
raise SystemExit(0 if all(checks.values()) else 1)
