"""Проверка происхождения и локальная геометрическая диагностика, без оценки качества хвата.

Старые смещения остаются unlabelled. Синтетические fixtures и принятые SDK-примеры
считаются отдельно; holdout не используется для подбора порогов.
"""
import argparse
import hashlib
import html
import json
import math
import os
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

VERSION = 'mesh-fit-0.4-local-volume'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def identity_group(identity):
    guid = identity.get('Guid')
    file_id = identity.get('LocalFileId')
    if not isinstance(guid, str) or not guid or type(file_id) is not int:
        raise ValueError('unknown_identity_or_noninteger_fileid')
    return f'{guid}:{file_id}'


def split_for(group):
    return 'holdout' if int(hashlib.sha256(group.encode()).hexdigest()[:8], 16) % 3 == 0 else 'tuning'


def validate_splits(rows):
    splits = defaultdict(set)
    for row in rows:
        if row['split'] not in ('tuning', 'holdout'):
            raise ValueError('invalid_split')
        splits[row['group_id']].add(row['split'])
    if any(len(values) != 1 for values in splits.values()):
        raise ValueError('source_group_leaks_between_splits')


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def classify(report):
    """Fail closed. Exterior contact is geometric evidence, never an accepted grip."""
    if not isinstance(report, dict):
        return 'abstain', 'invalid_report_shape'
    if report.get('SchemaVersion') != 2 or report.get('AlgorithmVersion') != VERSION:
        return 'abstain', 'incompatible_schema_or_algorithm'
    if report.get('Error') or report.get('LocalAnalysisVolumeApplied') is not True:
        return 'abstain', 'local_volume_not_applied'
    if any(not isinstance(report.get(key), dict) for key in ('Settings', 'LocalVolumeReviewAudit', 'AnalysisVolumeTopology')):
        return 'abstain', 'invalid_report_shape'
    audit = report.get('LocalVolumeReviewAudit') or {}
    review = (report.get('Settings') or {}).get('AnalysisVolumeReview') or {}
    settings = report.get('Settings') or {}
    if not isinstance(review, dict):
        return 'abstain', 'invalid_review_shape'
    if settings.get('AnalysisVolumeReviewed') is not True:
        return 'abstain', 'volume_not_reviewed'
    if not finite(settings.get('ContactDistanceMinMm')) or not finite(settings.get('ProximityMm')) or settings['ContactDistanceMinMm'] != 0 or settings['ProximityMm'] != 2:
        return 'abstain', 'incompatible_contact_interval'
    if (audit.get('Status') != 'applied' or review.get('Accepted') is not True
            or not isinstance(review.get('ReviewId'), str) or not review['ReviewId'].strip()
            or not isinstance(review.get('Scope'), str) or not review['Scope'].strip()):
        return 'abstain', 'manual_review_not_bound'
    mode = review.get('EvidenceMode')
    if mode != 'manual_user_review' and not (mode == 'synthetic_fixture_explicit_assumption'
            and report.get('CaptureSource') == 'mathematical_fixture'
            and report.get('EvidenceMode') == 'synthetic_fixture_explicit_assumption'):
        return 'abstain', 'invalid_review_evidence_mode'
    for field, expected in [('Units', 'meters'), ('Frame', 'object_root')]:
        if review.get(field) != expected or audit.get(field) != expected:
            return 'abstain', 'incompatible_units_or_frame'
    for key in ('GeometrySha256', 'ContextSha256'):
        value = audit.get(key)
        if (not isinstance(value, str) or len(value) != 64
                or any(c not in '0123456789abcdef' for c in value)
                or value != review.get(key)):
            return 'abstain', 'stale_or_invalid_review_hash'
    if (report.get('AnalysisVolumeTopology') or {}).get('CanDetermineInside') is not True:
        return 'abstain', 'unknown_topology'
    zones = report.get('LocalVolumeZones')
    if not isinstance(zones, list) or not zones:
        return 'abstain', 'missing_local_samples'
    needed = ('SampleCount', 'SampledAreaMm2', 'KnownExteriorNearAreaMm2',
              'KnownPenetrationAreaMm2', 'UnknownSignSamples', 'UnknownSignAreaMm2', 'DistanceP50Mm')
    if any(not isinstance(z, dict) or any(not finite(z.get(k)) or z[k] < 0 for k in needed)
           or z['SampleCount'] <= 0 or z['SampledAreaMm2'] <= 0 for z in zones):
        return 'abstain', 'nonfinite_or_invalid_local_metric'
    if any(z['KnownPenetrationAreaMm2'] > 0 for z in zones):
        partial = any(z['UnknownSignSamples'] or z['UnknownSignAreaMm2'] for z in zones)
        return 'penetration_detected', 'known_inside_samples_with_partial_unknown_sign' if partial else 'known_inside_samples'
    if any(z['UnknownSignSamples'] or z['UnknownSignAreaMm2'] for z in zones):
        return 'abstain', 'unknown_local_sign_samples'
    if any(z['KnownExteriorNearAreaMm2'] > 0 for z in zones):
        return 'exterior_contact', 'known_exterior_samples_in_0_to_2_mm'
    return 'no_contact', 'no_exterior_samples_in_0_to_2_mm'


def metrics(rows):
    groups = defaultdict(list)
    for row in rows:
        groups[f"{row['domain']}/{row['split']}"].append(row)
    result = {}
    for key, cases in sorted(groups.items()):
        m = dict(TP=0, FP=0, TN=0, FN=0, total=len(cases), labelled=0, covered=0,
                 abstentions=0, labelled_abstentions=0, unlabelled=0,
                 false_acceptances=0, false_rejections=0,
                 false_acceptance_denominator=0, false_rejection_denominator=0)
        for case in cases:
            abstain = case['diagnostic'] == 'abstain'
            m['abstentions' if abstain else 'covered'] += 1
            label = case.get('label')
            if label not in ('positive', 'negative'):
                m['unlabelled'] += 1
                continue
            m['labelled'] += 1
            if abstain:
                m['labelled_abstentions'] += 1
                continue
            positive = case['diagnostic'] == 'exterior_contact'
            m[('TP' if positive else 'FN') if label == 'positive' else ('FP' if positive else 'TN')] += 1
        m['false_acceptances'] = m['FP']; m['false_rejections'] = m['FN']
        m['false_acceptance_denominator'] = m['FP'] + m['TN']
        m['false_rejection_denominator'] = m['FN'] + m['TP']
        m['false_acceptance_rate'] = m['FP'] / m['false_acceptance_denominator'] if m['false_acceptance_denominator'] else None
        m['false_rejection_rate'] = m['FN'] / m['false_rejection_denominator'] if m['false_rejection_denominator'] else None
        m['coverage'] = m['covered'] / m['total']
        m['abstention_rate'] = m['abstentions'] / m['total']
        result[key] = m
    return result


def capture_axes(report):
    value = report.get('ObjectScaleAxes')
    if value is None and report.get('CaptureSource') == 'prefab_static' and finite(report.get('ObjectScale')):
        # В старом static snapshot был проверяемый инвариант uniform scale.
        value = [report['ObjectScale']] * 3
    if isinstance(value, dict):
        value = [value.get(k) for k in ('x','y','z')]
    return value if isinstance(value, list) and len(value) == 3 and all(finite(x) and x > 0 for x in value) else None


def fresh_source_matches(current, accepted):
    if not isinstance(current.get('Settings'), dict) or not isinstance(accepted.get('Settings'), dict):
        return False
    axes = capture_axes(current)
    if axes is None or axes != capture_axes(accepted):
        return False
    for key in ('CaptureSource', 'AppliedBlend', 'ObjectScale', 'AlignToControllerEnabled',
                'ControllerAlignmentApplied', 'ControllerModel', 'StateName', 'DefaultGrip',
                'PoseInherited', 'PoseType', 'GrabbablePath', 'GripAvatarGuid'):
        if current.get(key) != accepted.get(key):
            return False
    for key in ('AppliedBlend','ObjectScale'):
        if not finite(current.get(key)):
            return False
    # Все поля захвата/маски/состояния сохраняются; меняется только каталог результата.
    a = current['Settings']; b = accepted['Settings']
    return all(a.get(key) == b.get(key) for key in set(a) | set(b) if key != 'OutputDirectory')


def build(sdk_path, comparison_path, fixture_path, fresh_library=None):
    sdk = read(sdk_path); comparison = read(comparison_path)
    rows = []
    sources = [{'path': str(p).replace('\\', '/'), 'sha256': digest(p)} for p in (sdk_path, comparison_path)]

    def add(case, source, label, basis, domain='real', fixture=False, expected_version=None):
        identity = case.get('Object') or case.get('ObjectIdentity')
        provenance_error = None
        try:
            # Все аватары/стороны одного предмета объединены; оружейные pack/SDK/MEF по Weapon.
            group = ('synthetic:' + case['SourceGroup']) if fixture else (('weapon:' + case['Weapon']) if case.get('Weapon') else identity_group(identity or {}))
        except (ValueError, KeyError, TypeError) as error:
            group = 'invalid:' + case.get('Case', 'unknown'); provenance_error = str(error)
        path = Path(case['ReportPath']) if fixture else Path(case['Directory']) / 'report.json'
        row = dict(case_id=case['Case'], source=source, domain=domain, label=label, label_basis=basis,
                   group_id=group, split=case.get('Split', split_for(group)), object_identity=identity,
                   report_path=path.as_posix(), report_sha256=None, provenance_status='valid',
                   diagnostic='abstain', diagnostic_reason=None, accepted_quality=None)
        try:
            report = read(path); row['report_sha256'] = digest(path)
            if not isinstance(report, dict) or report.get('Error'):
                raise ValueError('invalid_or_failed_report')
            if report.get('AlgorithmVersion') != expected_version:
                raise ValueError('raw_report_algorithm_does_not_match_source')
            actual_identity = report.get('Object')
            if fixture:
                row['object_identity'] = actual_identity
            if not fixture and identity_group(actual_identity or {}) != identity_group(identity or {}):
                raise ValueError('report_identity_does_not_match_source_catalog')
            if not fixture and (actual_identity.get('DependencyHash') != identity.get('DependencyHash') or not identity.get('DependencyHash')):
                raise ValueError('report_dependency_does_not_match_source_catalog')
            if not fixture:
                for name in ('Avatar', 'Pose'):
                    expected = case.get(name) if isinstance(case.get(name), dict) else case.get(name+'Identity')
                    if expected and (report.get(name) != expected):
                        raise ValueError('report_'+name.lower()+'_identity_does_not_match_source_catalog')
            if provenance_error:
                raise ValueError(provenance_error)
            row['diagnostic'], row['diagnostic_reason'] = classify(report)
            row['evidence_mode'] = report.get('EvidenceMode', 'saved_real_report')
            row['unsigned_near_fraction'] = unsigned_fraction(report)
            row['local_unsigned_near_fraction'] = unsigned_fraction(report, 'LocalVolumeZones')
            row['baseline_case'] = case.get('BaselineCase')
            row['perturbation'] = case.get('Perturbation')
            row['png_paths'] = [(path.parent / c['Image']).as_posix() for c in report.get('Cameras', [])
                                if c.get('Image') and (path.parent / c['Image']).is_file()]
            row['gallery_path'] = (path.parent / 'index.html').as_posix() if row['png_paths'] and (path.parent / 'index.html').exists() else None
        except (OSError, ValueError, TypeError, KeyError) as error:
            row.update(provenance_status='error', diagnostic='abstain', diagnostic_reason=str(error))
        rows.append(row)

    for case in sdk['Cases']:
        add(case, 'accepted_sdk_reference', 'positive', 'user_accepted_stock_sdk_reference_not_local_volume_review', expected_version=sdk['AlgorithmVersion'])
    for case in sdk['Controls']:
        add(case, 'legacy_translation_control', None, 'unlabelled_offsets_no_known_bad_grip_label', expected_version=sdk['AlgorithmVersion'])
    for case in comparison['Cases']:
        add(case, 'weapon_candidate', None, 'unlabelled_pack_or_mef_or_project_sdk_candidate', expected_version=comparison['AlgorithmVersion'])
    if fresh_library:
        def source_key(report):
            settings = report['Settings']
            return (identity_group(report['Object']), identity_group(report['Avatar']), settings['Side'], settings['GrabPoint'], settings['GrabbableIndexPath'])
        known = {source_key(case): case for case in sdk['Cases']}
        for path in sorted(Path(fresh_library).glob('*/report.json')):
            report = read(path); original = known.get(source_key(report))
            if original and (report.get('Pose') != original.get('Pose') or report.get('Avatar') != original.get('Avatar')):
                original = None
            if original:
                baseline_path = Path(original['Directory']) / 'report.json'
                if not baseline_path.is_file() or not fresh_source_matches(report, read(baseline_path)):
                    original = None
            case = dict(Case='fresh-'+path.parent.name, Directory=path.parent.as_posix(), Object=(original or report)['Object'])
            add(case, 'fresh_sdk_probe', 'positive' if original else None,
                'matched_stock_sdk_identity_and_full_accepted_capture_context' if original else 'unlabelled_unmatched_or_changed_fresh_capture', expected_version=VERSION)
    fixture_status = 'missing'
    if Path(fixture_path).is_file():
        fixture_index = read(fixture_path)
        sources.append({'path': str(fixture_path).replace('\\', '/'), 'sha256': digest(fixture_path)})
        for case in fixture_index['Fixtures']:
            add(case, 'mathematical_fixture', case.get('Label'), case['LabelBasis'],
                case.get('Domain', 'synthetic_closed'), True, VERSION)
        fixture_status = 'present'
    validate_splits(rows)
    by_case = {r['case_id']: r for r in rows}
    for row in rows:
        baseline = by_case.get(row.get('baseline_case'))
        if baseline and row.get('local_unsigned_near_fraction') is not None and baseline.get('local_unsigned_near_fraction') is not None:
            row['local_unsigned_contact_delta'] = row['local_unsigned_near_fraction']-baseline['local_unsigned_near_fraction']
    manifest = dict(schema_version=1, sources=sources, entries=rows,
                    split_policy='deterministic_source_object_group_sha256_mod3; no threshold fitting',
                    synthetic_fixture_status=fixture_status)
    ambiguous = sdk.get('ControlsWithNonDecreasingNearFraction', [])
    summary = dict(created_utc=datetime.now(timezone.utc).isoformat(), algorithm_version=VERSION,
                   accepted_quality=None, diagnostic_only=True, synthetic_fixture_status=fixture_status,
                   count=len(rows), metrics=metrics(rows), provenance_errors=sum(r['provenance_status']=='error' for r in rows),
                   unsigned_growth_ambiguity=dict(non_decreasing=len(ambiguous), total=len(sdk['Controls']),
                       cases=ambiguous, labels='unlabelled; increasing unsigned proximity does not establish grip quality'),
                   confusion_convention='positive=known_exterior_contact; negative=known_geometric_containment_or_no_contact; TP=positive exterior, FP=negative exterior, TN=negative detected/no_contact, FN=positive detected/no_contact; abstention excluded from covered confusion denominators',
                   limitations=['real manual volume reviews absent in historical reports; real abstention is expected',
                       'synthetic geometric diagnostics do not establish real grip quality, Quest comfort or two-hand SDK IK',
                       'no learned thresholds; holdout is evaluated separately and never used for fitting',
                       'false acceptance/rejection rates refer to covered labelled geometric/reference cases, not accepted grips'],
                   entries=rows)
    return manifest, summary


def unsigned_fraction(report, field='Zones'):
    zones = report.get(field) or []
    if not zones or any(not finite(z.get('SampledAreaMm2')) or not finite(z.get('NearSurfaceFraction')) for z in zones):
        return None
    total = sum(z['SampledAreaMm2'] for z in zones)
    return sum(z['SampledAreaMm2']*z['NearSurfaceFraction'] for z in zones)/total if total > 0 else None


def write_json(path, value):
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False)+'\n', encoding='utf-8')


def gallery(path, summary):
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    def link(target, text):
        return f'<a href="{html.escape(os.path.relpath(target,path.parent).replace(chr(92), chr(47)),quote=True)}">{html.escape(text)}</a>'
    rows = []
    for row in summary['entries']:
        links = link(row['report_path'], 'JSON')
        if row.get('gallery_path') and Path(row['gallery_path']).resolve() != path.resolve():
            links += ' ' + link(row['gallery_path'], 'PNG gallery')
        if row.get('png_paths'): links += ' ' + link(row['png_paths'][0], 'PNG')
        cells = [row['case_id'], row['domain'], row['split'], row['label'] or 'unlabelled', row['diagnostic'], row['diagnostic_reason']]
        rows.append('<tr>'+''.join('<td>'+html.escape(str(c))+'</td>' for c in cells)+'<td>'+links+'</td></tr>')
    page = ('<!doctype html><meta charset="utf-8"><title>Hand fit geometric diagnostics</title>'
            '<style>body{background:#172029;color:#e9eff5;font:14px sans-serif;padding:24px}td,th{padding:8px;border-bottom:1px solid #46515c}a{color:#71c9ef}</style>'
            '<h1>Локальная геометрическая диагностика</h1><p>Accepted quality = null. Реальные хваты без ручной volume binding: abstain. '
            'Математические fixtures не принимают игровой хват. PNG только из существующих галерей.</p>'
            '<p>Unsigned growth ambiguity: '+str(summary['unsigned_growth_ambiguity']['non_decreasing'])+'/'+str(summary['unsigned_growth_ambiguity']['total'])+'</p>'
            '<pre>'+html.escape(json.dumps(summary['metrics'],ensure_ascii=False,indent=2))+'</pre>'
            '<table><tr><th>Case</th><th>Domain</th><th>Split</th><th>Label</th><th>Diagnostic</th><th>Reason</th><th>Sources</th></tr>'
            +''.join(rows)+'</table>')
    path.write_text(page, encoding='utf-8')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--sdk', default='Docs/audit/artifacts/hand-pose-fit-sdk-references-2026-10-04.json')
    parser.add_argument('--comparison', default='Docs/audit/artifacts/hand-pose-fit-mef-comparison-2026-10-04.json')
    parser.add_argument('--fixtures', default='tmp/HandPoseFitQuality20261005/fixtures.json')
    parser.add_argument('--fresh-library')
    parser.add_argument('--manifest', default='Tools/hand-pose-fit/quality-manifest-2026-10-05.json')
    parser.add_argument('--artifact', default='tmp/hand-pose-fit-quality-2026-10-05.json')
    parser.add_argument('--gallery', default='tmp/HandPoseFitQuality20261005/index.html')
    args = parser.parse_args()
    manifest, summary = build(args.sdk, args.comparison, args.fixtures, args.fresh_library)
    write_json(args.manifest, manifest); write_json(args.artifact, summary); gallery(args.gallery, summary)
    print(json.dumps({k:summary[k] for k in ('count','provenance_errors','synthetic_fixture_status','metrics','unsigned_growth_ambiguity')},ensure_ascii=True))
    return 1 if summary['provenance_errors'] or summary['synthetic_fixture_status'] != 'present' else 0


if __name__ == '__main__':
    raise SystemExit(main())
