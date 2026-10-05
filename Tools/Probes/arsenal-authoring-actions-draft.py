from pathlib import Path

source = Path('Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalEditorActions.cs').read_text(encoding='utf-8-sig')
source = source.replace('public static string EditorBlockReason => EditorApplication.isPlayingOrWillChangePlaymode',
                        'private static string RuntimeBlockReason => EditorApplication.isPlayingOrWillChangePlaymode', 1)
source = source.replace(': EditorApplication.isUpdating ? "Идёт импорт"\n            : ArsenalEditorStatus.DirtyScene',
                        ': EditorApplication.isUpdating ? "Идёт импорт" : null;\n        public static string EditorBlockReason => RuntimeBlockReason ?? (ArsenalEditorStatus.DirtyScene', 1)
source = source.replace('? "Есть несохранённая сцена: сохраните её самостоятельно"\n            : PrefabStageUtility',
                        '? "Есть несохранённая сцена: сохраните её самостоятельно" : null)\n            ?? (PrefabStageUtility', 1)
source = source.replace('? "Закройте режим редактирования префаба перед операцией" : null;',
                        '? "Закройте режим редактирования префаба перед операцией" : null);', 1)
context = '''        internal sealed class AuthoringContext
        {
            private readonly ArsenalLayoutAuthoringStand marker;
            private readonly int markerId;
            private readonly int sceneHandle;
            internal AuthoringContext(ArsenalLayoutAuthoringStand stand)
            {
                marker = stand; markerId = stand != null ? stand.GetInstanceID() : 0;
                sceneHandle = stand != null ? stand.gameObject.scene.handle : 0;
            }
            internal string BlockReason
            {
                get
                {
                    if (RuntimeBlockReason != null) return RuntimeBlockReason;
                    if (PrefabStageUtility.GetCurrentPrefabStage() != null) return "Закройте PrefabStage перед authoring операцией";
                    if (marker == null || marker.GetInstanceID() != markerId || marker._owner != ArsenalLayoutAuthoringStand.OwnerId)
                        return "Authoring marker утрачен или заменён";
                    var scene = marker.gameObject.scene;
                    if (!scene.isLoaded || scene.handle != sceneHandle || scene.path != ArsenalLayoutAuthoringStand.ScenePath || marker.transform.parent != null)
                        return "Authoring scene context утрачен или заменён";
                    if (scene.GetRootGameObjects().Length != 1 || scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalLayoutAuthoringStand>(true)).Count() != 1)
                        return "Authoring scene содержит чужие корни или дубли marker";
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var other = SceneManager.GetSceneAt(i);
                        if (other.handle != sceneHandle && other.isDirty) return "Есть несохранённая чужая сцена: authoring исключение её не разрешает";
                    }
                    return null;
                }
            }
        }

'''
source = source.replace('        public sealed class Lease : IDisposable\n', context + '        public sealed class Lease : IDisposable\n', 1)
source = source.replace('internal Lease(string token) { this.token = token; }',
                        'private readonly AuthoringContext authoring;\n            internal Lease(string token, AuthoringContext authoring) { this.token = token; this.authoring = authoring; }', 1)
source = source.replace('string blocked = EditorBlockReason;\n                if (blocked != null)',
                        'string blocked = authoring != null ? authoring.BlockReason : EditorBlockReason;\n                if (blocked != null)', 1)
source = source.replace('''        public static Lease AcquireLease(string title)
        {
            string blocked = BlockReason;''', '''        public static Lease AcquireLease(string title) => AcquireLeaseCore(title, null);
        public static Lease AcquireAuthoringLease(string title, ArsenalLayoutAuthoringStand stand) => AcquireLeaseCore(title, new AuthoringContext(stand));
        private static Lease AcquireLeaseCore(string title, AuthoringContext authoring)
        {
            string blocked = authoring != null ? authoring.BlockReason ?? (Directory.Exists(LockPath) ? "Редактор занят: " + LockStatus : null) : BlockReason;''', 1)
source = source.replace('var lease = new Lease(token);', 'var lease = new Lease(token, authoring);', 1)
Path('tmp/arsenal-visual-stage/ArsenalEditorActions.authoring.cs').write_text(source, encoding='utf-8')
print('Prepared additive authoring context; one existing lock/token writer, default path retained.')
