using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public enum ContactReviewStatus { Draft, Accepted, Rejected }
    [Serializable] public sealed class ContactCriterion { public string id; public string title; }
    [Serializable] public sealed class ContactScore { public string criterion; public int value; }
    [Serializable] public sealed class ContactPositionReview
    {
        public string id; public string notes; public List<ContactScore> scores = new List<ContactScore>();
    }
    [Serializable] public sealed class ContactCompositionReview
    {
        public string prefabGuid; public string reviewedHash; public ContactReviewStatus status;
        public string notes; public List<ContactPositionReview> positions = new List<ContactPositionReview>();
    }
    [Serializable] public sealed class ContactReviewDatabase
    {
        public int version = 1;
        public List<ContactCriterion> criteria = new List<ContactCriterion>();
        public List<ContactCompositionReview> compositions = new List<ContactCompositionReview>();
    }

    /// <summary>Ручные авторские оценки; данные не являются генерируемым отчётом.</summary>
    public static class ContactCatalogReviews
    {
        public const string Path = "Tools/LevelDesign/ContactCatalog/reviews.json";
        public const string PrefabFolder = "Assets/Prefabs/LevelDesign/ContactCatalog";
        public const string LabPath = "Assets/Scenes/Tools/ContactCatalogLab.unity";

        public static ContactReviewDatabase Load(string path = Path)
        {
            if (!File.Exists(path)) return NewDatabase();
            var db = JsonUtility.FromJson<ContactReviewDatabase>(File.ReadAllText(path, Encoding.UTF8));
            if (db == null || db.version != 1 || db.criteria == null || db.compositions == null)
                throw new InvalidDataException("Неизвестный или повреждённый формат оценок. Файл не перезаписан.");
            if (db.criteria.Any(c => c == null || string.IsNullOrEmpty(c.id)) ||
                db.criteria.Select(c => c.id).Distinct().Count() != db.criteria.Count ||
                db.compositions.Any(c => c == null || c.positions == null || c.positions.Any(p => p == null || p.scores == null)))
                throw new InvalidDataException("Повреждённые критерии или позиции. Файл не перезаписан.");
            return db;
        }

        public static ContactReviewDatabase NewDatabase()
        {
            var db = new ContactReviewDatabase();
            var ids = new[] { "protection", "control", "mobility", "counterplay", "interest" };
            var titles = new[] { "Защита от основной угрозы", "Обзор и контроль", "Манёвр и отход",
                "Контригра для противника", "Интерес игры" };
            for (int i = 0; i < ids.Length; i++) db.criteria.Add(new ContactCriterion { id = ids[i], title = titles[i] });
            return db;
        }

        public static void Save(ContactReviewDatabase db, string path = Path)
        {
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(db, true), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static ContactCompositionReview Get(ContactReviewDatabase db, string guid)
        {
            var entry = db.compositions.FirstOrDefault(c => c.prefabGuid == guid);
            if (entry != null) return entry;
            entry = new ContactCompositionReview { prefabGuid = guid,
                reviewedHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GUIDToAssetPath(guid)).ToString() };
            db.compositions.Add(entry); return entry;
        }

        public static bool IsStale(ContactCompositionReview entry, string currentHash)
            => entry.reviewedHash != currentHash;
        public static bool IsCatalogMember(ContactCompositionReview entry, string currentHash)
            => entry.status == ContactReviewStatus.Accepted && !IsStale(entry, currentHash);

        public static ContactPositionReview Position(ContactCompositionReview entry, string id)
        {
            var position = entry.positions.FirstOrDefault(p => p.id == id);
            if (position != null) return position;
            position = new ContactPositionReview { id = id }; entry.positions.Add(position); return position;
        }

        public static ContactScore Score(ContactPositionReview position, string criterion)
        {
            var score = position.scores.FirstOrDefault(s => s.criterion == criterion);
            if (score != null) return score;
            score = new ContactScore { criterion = criterion }; position.scores.Add(score); return score;
        }
    }
}
