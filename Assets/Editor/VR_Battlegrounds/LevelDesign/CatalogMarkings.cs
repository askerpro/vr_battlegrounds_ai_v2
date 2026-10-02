using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Ручные соответствия; живут отдельно от сцен и префабов паков.</summary>
    public sealed class CatalogMarkings : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id, blockGuid, title, theme, notes;
            public string[] objectIds;
            public string[] sourceNames;
        }

        public List<Entry> entries = new List<Entry>();
    }
}
