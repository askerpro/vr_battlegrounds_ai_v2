import importlib.util
import pathlib
import copy
import unittest

MODULE = pathlib.Path(__file__).with_name('quality_calibration.py')


class QualityCalibrationRegression(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not MODULE.exists():
            raise AssertionError('quality_calibration.py отсутствует: pipeline не реализован')
        spec = importlib.util.spec_from_file_location('quality_calibration', MODULE)
        cls.q = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.q)

    def report(self):
        return {'SchemaVersion': 2, 'AlgorithmVersion': 'mesh-fit-0.4-local-volume',
                'LocalAnalysisVolumeApplied': True, 'Settings': {'AnalysisVolumeReviewed': True, 'ContactDistanceMinMm': 0, 'ProximityMm': 2,
                    'AnalysisVolumeReview': {'Accepted': True, 'ReviewId': 'manual', 'Scope': 'math',
                        'Units': 'meters', 'Frame': 'object_root', 'GeometrySha256': 'a'*64, 'ContextSha256': 'b'*64, 'EvidenceMode': 'manual_user_review'}},
                'LocalVolumeReviewAudit': {'Status': 'applied', 'Units': 'meters', 'Frame': 'object_root',
                    'GeometrySha256': 'a'*64, 'ContextSha256': 'b'*64},
                'LocalVolumeZones': [{'Zone': 'index', 'SampleCount': 2, 'SampledAreaMm2': 2,
                    'KnownExteriorNearAreaMm2': 2, 'KnownPenetrationAreaMm2': 0,
                    'UnknownSignSamples': 0, 'UnknownSignAreaMm2': 0, 'DistanceP50Mm': 1}],
                'AnalysisVolumeTopology': {'CanDetermineInside': True}}

    def test_unsigned_contact_and_missing_volume_abstain(self):
        self.assertEqual(self.q.classify({'Zones': [{'NearSurfaceFraction': 1}]}), ('abstain', 'incompatible_schema_or_algorithm'))
        report = self.report(); report['LocalAnalysisVolumeApplied'] = False
        self.assertEqual(self.q.classify(report)[0], 'abstain')

    def test_no_thumb_rule_and_penetration_priority(self):
        report = self.report()
        self.assertEqual(self.q.classify(report)[0], 'exterior_contact')
        report['LocalVolumeZones'][0]['KnownPenetrationAreaMm2'] = 1
        self.assertEqual(self.q.classify(report)[0], 'penetration_detected')

    def test_unknown_boundary_nonfinite_and_stale_hash_abstain(self):
        for field, value in [('UnknownSignSamples', 1), ('DistanceP50Mm', float('nan'))]:
            report = self.report(); report['LocalVolumeZones'][0][field] = value
            self.assertEqual(self.q.classify(report)[0], 'abstain')
        report = self.report(); report['LocalVolumeReviewAudit']['GeometrySha256'] = 'c'*64
        self.assertEqual(self.q.classify(report)[0], 'abstain')

    def test_manual_review_is_required(self):
        for field, value in [('Accepted', False), ('ReviewId', ''), ('Scope', ''), ('Units', 'mm'), ('Frame', 'world')]:
            report = self.report(); report['Settings']['AnalysisVolumeReview'][field] = value
            self.assertEqual(self.q.classify(report)[0], 'abstain')

    def test_wrong_contact_interval_and_nonboolean_flags_abstain(self):
        for key, value in [('ContactDistanceMinMm', .5), ('ProximityMm', 10), ('ProximityMm', float('nan'))]:
            report = self.report(); report['Settings'][key] = value
            self.assertEqual(self.q.classify(report)[0], 'abstain')
        for section, key in [('Settings', 'AnalysisVolumeReviewed'), ('AnalysisVolumeTopology', 'CanDetermineInside')]:
            report = self.report(); report[section][key] = 'false'
            self.assertEqual(self.q.classify(report)[0], 'abstain')
        report = self.report(); report['LocalAnalysisVolumeApplied'] = 'false'
        self.assertEqual(self.q.classify(report)[0], 'abstain')

    def test_known_penetration_remains_detectable_with_partial_unknown_sign(self):
        report = self.report(); zone = report['LocalVolumeZones'][0]
        zone.update(KnownPenetrationAreaMm2=1, UnknownSignSamples=1, UnknownSignAreaMm2=.1)
        self.assertEqual(self.q.classify(report)[0], 'penetration_detected')

    def test_malformed_report_sections_fail_closed(self):
        for section in ('Settings', 'LocalVolumeReviewAudit', 'AnalysisVolumeTopology'):
            report = self.report(); report[section] = ['unexpected']
            self.assertEqual(self.q.classify(report)[0], 'abstain')

    def test_synthetic_review_cannot_approve_real_capture(self):
        report = self.report(); report['Settings']['AnalysisVolumeReview']['EvidenceMode'] = 'synthetic_fixture_explicit_assumption'
        report['CaptureSource'] = 'prefab_static'; report['EvidenceMode'] = 'synthetic_fixture_explicit_assumption'
        self.assertEqual(self.q.classify(report)[0], 'abstain')
        report['CaptureSource'] = 'mathematical_fixture'
        self.assertEqual(self.q.classify(report)[0], 'exterior_contact')
        report['EvidenceMode'] = None
        self.assertEqual(self.q.classify(report)[0], 'abstain')

    def test_fresh_label_requires_accepted_capture_context(self):
        baseline = {'Settings': {'HandOffsetMm': [0,0,0], 'AlignmentMode': 'grip_reference', 'Blend': 0,
                                'OutputDirectory': 'old'},
                    'CaptureSource': 'prefab_static', 'AppliedBlend': 1, 'ObjectScale': 1,
                    'ObjectScaleAxes': None, 'AlignToControllerEnabled': False,
                    'ControllerAlignmentApplied': False, 'ControllerModel': None, 'StateName': 'hold'}
        fresh = copy.deepcopy(baseline); fresh['Settings']['OutputDirectory'] = 'fresh'
        fresh['ObjectScaleAxes'] = [1,1,1]
        self.assertTrue(self.q.fresh_source_matches(fresh, baseline))
        for section, key, value in [('Settings', 'HandOffsetMm', [100,0,0]),
                                    ('Settings', 'AlignmentMode', 'other_mode'),
                                    (None, 'AppliedBlend', .125), (None, 'ObjectScale', 2)]:
            changed = copy.deepcopy(fresh)
            (changed[section] if section else changed)[key] = value
            self.assertFalse(self.q.fresh_source_matches(changed, baseline))

    def test_fileid_is_exact_and_group_split_cannot_leak(self):
        identity = {'Guid': 'object', 'LocalFileId': 9163510237887065938}
        group = self.q.identity_group(identity)
        self.assertIn('9163510237887065938', group)
        rows = [{'group_id': group, 'split': 'tuning'}, {'group_id': group, 'split': 'holdout'}]
        with self.assertRaises(ValueError): self.q.validate_splits(rows)
        with self.assertRaises(ValueError): self.q.identity_group({'Guid': 'object', 'LocalFileId': float(identity['LocalFileId'])})

    def test_abstention_is_not_false_rejection_and_domains_are_separate(self):
        rows = [
            {'domain': 'real', 'split': 'holdout', 'label': 'positive', 'diagnostic': 'abstain'},
            {'domain': 'synthetic_closed', 'split': 'holdout', 'label': 'positive', 'diagnostic': 'exterior_contact'},
            {'domain': 'synthetic_closed', 'split': 'holdout', 'label': 'negative', 'diagnostic': 'penetration_detected'},
            {'domain': 'synthetic_closed', 'split': 'holdout', 'label': 'negative', 'diagnostic': 'exterior_contact'}]
        metrics = self.q.metrics(rows)
        self.assertEqual(metrics['real/holdout']['false_rejections'], 0)
        self.assertEqual(metrics['real/holdout']['abstentions'], 1)
        self.assertEqual(metrics['synthetic_closed/holdout']['TP'], 1)
        self.assertEqual(metrics['synthetic_closed/holdout']['FP'], 1)
        self.assertEqual(metrics['synthetic_closed/holdout']['TN'], 1)
        self.assertEqual(metrics['synthetic_closed/holdout']['false_acceptance_denominator'], 2)


if __name__ == '__main__':
    unittest.main()
