from pathlib import Path

source = Path('Assets/Editor/VR_Battlegrounds/Arsenal')
draft = Path('tmp/arsenal-visual-stage/marker-relocation')
draft.mkdir(exist_ok=True)
marker = (source / 'ArsenalLayoutAuthoringStand.cs').read_text(encoding='utf-8-sig')
marker = marker.replace('Только editor fixture: transforms — несохранённая рабочая проекция единственного Style.',
                        'Пассивный сериализуемый carrier editor-стенда; поведение находится в Editor. Сцена исключена из build.')
marker = marker.replace('internal const string OwnerId', 'public const string OwnerId')
marker = marker.replace('] internal ', '] private ')
marker = marker.replace('        public ArsenalPreset Preset', '''        public string Owner => _owner;
        public string SourceBaseline => _sourceBaseline;
        public string StagedBaseline => _stagedBaseline;
        public ArsenalPreset Preset''')
(draft / 'ArsenalLayoutAuthoringStand.cs').write_text(marker, encoding='utf-8')
editor = (source / 'ArsenalLayoutAuthoringStandEditor.cs').read_text(encoding='utf-8-sig')
editor = editor.replace('var stand = root.AddComponent<ArsenalLayoutAuthoringStand>(); stand._preset = preset; stand._style = style;', '''var stand = root.AddComponent<ArsenalLayoutAuthoringStand>();
            if (stand == null) throw new InvalidOperationException("Data carrier не прикрепился: проверьте runtime assembly boundary.");
            WriteSources(stand, preset, style);''')
for old, new in [('stand._owner', 'stand.Owner'), ('stand._sourceBaseline', 'stand.SourceBaseline'), ('stand._stagedBaseline', 'stand.StagedBaseline')]:
    editor = editor.replace(old, new)
editor = editor.replace('''                stand.SourceBaseline = SourceFingerprint(stand);
                foreach (var slot in stand.Slots.Where((s, index) => selected < 0 || index == selected)) slot.CommittedBaseline = SlotFingerprint(slot);
                stand.StagedBaseline = Hash(string.Join("|", stand.Slots.Select(s => s.CommittedBaseline)));''',
'''                WriteBaselines(stand, selected);''')
editor = editor.replace('foreach (var slot in stand._slots)', 'foreach (var slot in stand.Slots)')
editor = editor.replace('stand._slots.Clear(); int peg = 0, shelf = 0;', 'var cache = new List<ArsenalLayoutAuthoringStand.SlotHandles>(); int peg = 0, shelf = 0;')
editor = editor.replace('stand._slots.Add(staged);', 'cache.Add(staged);')
editor = editor.replace('stand.SourceBaseline = SourceFingerprint(stand); stand.StagedBaseline = StagedFingerprint(stand); EditorUtility.SetDirty(stand);',
                        'WriteSlots(stand, cache); WriteBaselines(stand, -1); EditorUtility.SetDirty(stand);')
helpers = '''
        // Единственный Editor writer сериализуемого derived cache. Carrier не содержит editor API/setters.
        private static void WriteSources(ArsenalLayoutAuthoringStand stand, ArsenalPreset preset, ArsenalPresentationStyle style)
        {
            var input = new SerializedObject(stand);
            input.FindProperty("_preset").objectReferenceValue = preset;
            input.FindProperty("_style").objectReferenceValue = style;
            input.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void WriteBaselines(ArsenalLayoutAuthoringStand stand, int selected)
        {
            var stamps = stand.Slots.Select((slot, index) => selected < 0 || selected == index ? SlotFingerprint(slot) : slot.CommittedBaseline).ToArray();
            var input = new SerializedObject(stand); var slots = input.FindProperty("_slots");
            for (int i = 0; i < stamps.Length; i++) slots.GetArrayElementAtIndex(i).FindPropertyRelative("CommittedBaseline").stringValue = stamps[i];
            input.FindProperty("_sourceBaseline").stringValue = SourceFingerprint(stand);
            input.FindProperty("_stagedBaseline").stringValue = Hash(string.Join("|", stamps));
            input.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void WriteSlots(ArsenalLayoutAuthoringStand stand, List<ArsenalLayoutAuthoringStand.SlotHandles> cache)
        {
            var input = new SerializedObject(stand); var slots = input.FindProperty("_slots"); slots.arraySize = cache.Count;
            for (int i = 0; i < cache.Count; i++)
            {
                var source = cache[i]; var row = slots.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("Weapon").objectReferenceValue = source.Weapon;
                row.FindPropertyRelative("Zone").enumValueIndex = (int)source.Zone;
                row.FindPropertyRelative("Frame").objectReferenceValue = source.Frame;
                row.FindPropertyRelative("Item").objectReferenceValue = source.Item;
                row.FindPropertyRelative("Magazine").objectReferenceValue = source.Magazine;
                row.FindPropertyRelative("Card").objectReferenceValue = source.Card;
                row.FindPropertyRelative("CommittedBaseline").stringValue = source.CommittedBaseline;
                var supports = row.FindPropertyRelative("Supports"); supports.arraySize = source.Supports.Count;
                for (int j = 0; j < source.Supports.Count; j++)
                {
                    var support = supports.GetArrayElementAtIndex(j); var data = source.Supports[j];
                    support.FindPropertyRelative("Role").stringValue = data.Role;
                    support.FindPropertyRelative("AnchorKind").enumValueIndex = (int)data.AnchorKind;
                    support.FindPropertyRelative("Handle").objectReferenceValue = data.Handle;
                }
            }
            input.ApplyModifiedPropertiesWithoutUndo();
        }
'''
editor = editor.replace('        public static string SourceFingerprint', helpers + '        public static string SourceFingerprint')
assert 'stand._' not in editor
(draft / 'ArsenalLayoutAuthoringStandEditor.cs').write_text(editor, encoding='utf-8')
actions = (source / 'ArsenalEditorActions.cs').read_text(encoding='utf-8-sig').replace('marker._owner', 'marker.Owner')
(draft / 'ArsenalEditorActions.cs').write_text(actions, encoding='utf-8')
print('Prepared data-only carrier move + private cache SerializedObject Editor writer; native files unchanged.')
