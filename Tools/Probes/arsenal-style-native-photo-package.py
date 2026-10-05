from pathlib import Path

# Только render-only копии: native Style читается, сценовые проекции не записываются.
base = Path('Tools/Probes/arsenal-visual-style-draft-photos.cs.txt').read_text(encoding='utf-8-sig')
base = base.replace('const string phase = "draft-style";', 'const string phase = "native-style-input";')
marker = '            bool shelf = slot.PresentationZone'
supports = '''            foreach (var support in presentation.Supports) {
                copies.Add(spawnGeometry(presentation.Style.SupportModule,
                    slot.transform.TransformPoint(support.SlotPose.Position),
                    slot.transform.rotation * support.SlotPose.Rotation,
                    presentation.Style.SupportModule.transform.localScale));
            }
'''
base = base.replace(marker, supports + marker, 1)
Path('tmp/arsenal-visual-stage/native-style-photo-package.cs.txt').write_text(base, encoding='utf-8')
angle = base.replace('const string phase = "native-style-input";', 'const string phase = "native-style-angle";')
angle = angle.replace('new UnityEngine.Vector3(0f, 2.3f, -2.3f)', 'new UnityEngine.Vector3(.95f, 1.7f, -1.7f)')
angle = angle.replace('new UnityEngine.Vector3(0f, .12f, -3f)', 'new UnityEngine.Vector3(1.2f, .5f, -2.6f)')
Path('tmp/arsenal-visual-stage/native-style-angle-package.cs.txt').write_text(angle, encoding='utf-8')
print('Prepared twenty native-Style input views and twenty angled module views; no persistent writes.')
