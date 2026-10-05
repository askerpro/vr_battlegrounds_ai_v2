from pathlib import Path

base = Path('tmp/arsenal-visual-stage/native-style-photo-package.cs.txt').read_text(encoding='utf-8')
base = base.replace('const string phase = "native-style-input";', 'const string phase = "shelf-no-hooks-proposal";')
setup = '''var sourceStyle=style;
style=UnityEngine.Object.Instantiate(sourceStyle);style.hideFlags=UnityEngine.HideFlags.HideAndDontSave;
var proposalInput=new UnityEditor.SerializedObject(style);
foreach(var name in new[]{"_zones","_exceptions"}) {
    var entries=proposalInput.FindProperty(name);
    for(int index=0;index<entries.arraySize;index++) {
        var entry=entries.GetArrayElementAtIndex(index);
        if(entry.FindPropertyRelative("Zone").enumValueIndex!=(int)VrBattlegrounds.Arsenal.ArsenalPresentationZone.Shelf)continue;
        entry.FindPropertyRelative("Supports").arraySize=0;
        foreach(var pose in new[]{"ItemTarget","MagazineTarget"}) {
            var position=entry.FindPropertyRelative(pose).FindPropertyRelative("Position");
            position.vector3Value+=new UnityEngine.Vector3(0,-.022f,0);
        }
    }
}
proposalInput.ApplyModifiedPropertiesWithoutUndo();
'''
base = base.replace('try {\n    var cameraObject', setup + 'try {\n    var cameraObject', 1)
base = base.replace('    UnityEngine.RenderTexture.active = previousRenderTexture;',
                    '    if(style!=null)UnityEngine.Object.DestroyImmediate(style);\n    UnityEngine.RenderTexture.active = previousRenderTexture;', 1)
Path('tmp/arsenal-visual-stage/shelf-no-hooks-photo-package.cs.txt').write_text(base, encoding='utf-8')
print('Prepared render-only no-hooks Shelf proposal; native input unchanged.')
