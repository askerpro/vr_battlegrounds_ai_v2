using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Стена арсенала звучит (решение пользователя): решётка и полки едут со звуком, закрытая стена щёлкает засовом.
    /// </summary>
    public class ArsenalSoundsTests
    {
        private const string Wall = "Assets/Prefabs/Arsenal/StandardArsenalWall.prefab";

        [Test]
        public void Стена_настроена_на_звук()
        {
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(Wall);
            var animator = wall.GetComponentInChildren<ArsenalAnimator>(true);
            Assert.IsNotNull(animator, "На стене нет ArsenalAnimator.");

            var sounds = animator.GetComponent<ArsenalWallSounds>();
            Assert.IsNotNull(sounds, "Рядом с ArsenalAnimator нет ArsenalWallSounds — стена открывается молча.");
            Assert.IsTrue(sounds.IsConfigured, "ArsenalWallSounds: не заданы источники или клипы (решётка, полки, засов).");
            Assert.AreNotSame(sounds.ShutterSource, sounds.ShelfSource, "Решётка и полки на одном источнике — один звук оборвёт другой.");
            foreach (AudioSource source in new[] { sounds.ShutterSource, sounds.ShelfSource })
            {
                Assert.IsFalse(source.playOnAwake, $"{source.name}: Play On Awake — стена звучит при спавне.");
                Assert.Greater(source.spatialBlend, 0.5f, $"{source.name}: звук не объёмный — стену слышно как в голове.");
            }
        }

        [Test]
        public void Открытие_и_закрытие_играют_звуки_а_закрытие_щёлкает_засовом()
        {
            var go = new GameObject("Wall", typeof(Animator));
            var played = new List<AudioClip>();
            System.Action<ArsenalWallSounds, AudioClip> handler = (s, clip) => played.Add(clip);
            ArsenalWallSounds.Played += handler;
            try
            {
                var animator = go.AddComponent<ArsenalAnimator>();
                var sounds = go.AddComponent<ArsenalWallSounds>();
                var clips = new Dictionary<string, AudioClip>();
                var so = new SerializedObject(sounds);
                so.FindProperty("_shutterSource").objectReferenceValue = go.AddComponent<AudioSource>();
                so.FindProperty("_shelfSource").objectReferenceValue = new GameObject("Shelf", typeof(AudioSource)).GetComponent<AudioSource>();
                foreach (string field in new[] { "_shutterOpen", "_shutterClose", "_shelfOpen", "_shelfClose", "_latchClosed" })
                {
                    clips[field] = AudioClip.Create(field, 441, 1, 44100, false);
                    so.FindProperty(field).objectReferenceValue = clips[field];
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                Call(sounds, "OnEnable");
                Raise(animator, "SequenceStarted", true);
                CollectionAssert.AreEquivalent(new[] { clips["_shutterOpen"], clips["_shelfOpen"] }, played, "Открытие: решётка и полки.");

                played.Clear();
                Raise(animator, "SequenceCompleted", true);
                Assert.IsEmpty(played, "Открывшаяся стена щёлкает засовом.");

                Raise(animator, "SequenceStarted", false);
                Raise(animator, "SequenceCompleted", false);
                CollectionAssert.AreEquivalent(new[] { clips["_shutterClose"], clips["_shelfClose"], clips["_latchClosed"] }, played,
                    "Закрытие: решётка, полки и засов в конце.");
            }
            finally
            {
                ArsenalWallSounds.Played -= handler;
                Object.DestroyImmediate(GameObject.Find("Shelf"));
                Object.DestroyImmediate(go);
            }
        }

        private static void Call(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        private static void Raise(ArsenalAnimator animator, string evt, bool open)
        {
            var field = typeof(ArsenalAnimator).GetField(evt, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"У ArsenalAnimator нет события {evt} — поправь тест.");
            ((System.Action<bool>)field.GetValue(animator))?.Invoke(open);
        }
    }
}
