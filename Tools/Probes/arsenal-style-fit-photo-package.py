from pathlib import Path
base = Path('Tools/Probes/arsenal-visual-style-draft-photos.cs.txt').read_text(encoding='utf-8-sig')
fit = Path('Tools/Probes/arsenal-style-fit-proposal.cs.txt').read_text(encoding='utf-8-sig')
base = base.replace('const string phase = "draft-style";', 'const string phase = "fit-reviewed";')
base = base.replace('try {\n    var cameraObject', 'var fitSource=style;UnityEngine.Material fitMaterial=null;\ntry {\n' +
                    fit.replace('var fitSource=style;style=', 'style=').replace('var fitMaterial=new', 'fitMaterial=new') + '\n    var cameraObject', 1)
marker = '            bool shelf = slot.PresentationZone'
addition = '''            foreach(var support in presentation.Supports) {
                copies.Add(spawnGeometry(presentation.Style.SupportModule,slot.transform.TransformPoint(support.SlotPose.Position),
                    slot.transform.rotation*support.SlotPose.Rotation,presentation.Style.SupportModule.transform.localScale));
            }
'''
base = base.replace(marker, addition + marker, 1)
base = base.replace('    UnityEngine.RenderTexture.active = previousRenderTexture;',
                    '    if(style!=fitSource && style!=null)UnityEngine.Object.DestroyImmediate(style);\n    if(fitMaterial!=null)UnityEngine.Object.DestroyImmediate(fitMaterial);\n    UnityEngine.RenderTexture.active = previousRenderTexture;', 1)
Path('tmp/arsenal-visual-stage/fit-photo-package.cs.txt').write_text(base, encoding='utf-8')
print('Prepared render-only fit proposal: cloned Style, source geometry, twenty images; native outputs untouched.')
