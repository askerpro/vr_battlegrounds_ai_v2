using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Отклик на попадание по игроку (T-37): пятно крови на одежде и звук.
    ///
    /// <para>
    /// Что доказывает. Отклик собран (<c>Resources/PlayerHitEffects</c>: материалы подтёка и отверстия на URP,
    /// звуки). Отменённый урон (разминка) отклика не даёт; пятно — только на хитбоксе, у своего игрока не в
    /// голову. Отверстие — точно в точке попадания; пятно ходит с частью тела (дочерний объект хитбокса), на
    /// теле их не больше предела, звуки — в пуле.
    /// </para>
    /// </summary>
    public class HitEffectsTests
    {
        private static PlayerHitEffects Service()
        {
            var service = Resources.Load<PlayerHitEffects>(PlayerHitEffects.Resource);
            Assert.IsNotNull(service, $"Нет Resources/{PlayerHitEffects.Resource} — собери: Tools/VR Battlegrounds/Gameplay/Build Hit Effects.");
            return service;
        }

        [Test]
        public void Отклик_собран_пятно_и_звуки()
        {
            var so = new UnityEditor.SerializedObject(Service());
            foreach (string field in new[] { "_streakMaterial", "_holeMaterial" })
            {
                var material = so.FindProperty(field).objectReferenceValue as Material;
                Assert.IsNotNull(material, $"У отклика нет материала {field}.");
                StringAssert.StartsWith("Universal Render Pipeline/", material.shader.name, $"{field} не на URP.");
                Assert.IsNotNull(material.GetTexture("_BaseMap"), $"У {field} нет текстуры.");
            }

            foreach (string field in new[] { "_clips", "_headClips" })
            {
                UnityEditor.SerializedProperty clips = so.FindProperty(field);
                Assert.Greater(clips.arraySize, 0, $"У отклика нет звуков {field}.");
                for (int i = 0; i < clips.arraySize; i++)
                    Assert.IsNotNull(clips.GetArrayElementAtIndex(i).objectReferenceValue, $"Пустой звук {field} #{i}.");
            }
        }

        [Test]
        public void В_голову_свой_звук()
        {
            var body = new AudioClip[1];
            var head = new AudioClip[1];
            Assert.AreSame(head, PlayerHitEffects.ClipsFor(true, body, head), "Попадание в голову звучит как в тело.");
            Assert.AreSame(body, PlayerHitEffects.ClipsFor(false, body, head), "Попадание в тело звучит как в голову.");
            Assert.AreSame(body, PlayerHitEffects.ClipsFor(true, body, new AudioClip[0]), "Без звуков головы попадание в голову беззвучно.");
        }

        [Test]
        public void Правила_отклика()
        {
            Assert.IsTrue(PlayerHitEffects.Plays(false), "Прошедший урон без отклика.");
            Assert.IsFalse(PlayerHitEffects.Plays(true), "Отменённый урон (разминка) даёт кровь.");

            Assert.IsTrue(PlayerHitEffects.ShowsStain(false, true, HitZone.Head), "Попадание в голову чужого — без пятна.");
            Assert.IsFalse(PlayerHitEffects.ShowsStain(true, true, HitZone.Head), "Своему игроку пятно в голову — прямо в камеру.");
            Assert.IsTrue(PlayerHitEffects.ShowsStain(true, true, HitZone.Arm), "Своему игроку нет пятна на руке.");
            Assert.IsFalse(PlayerHitEffects.ShowsStain(false, false, HitZone.Torso), "Пятно не на хитбоксе — повиснет в воздухе.");
        }

        [Test]
        public void Пятно_в_точке_попадания_ходит_с_телом_и_их_не_больше_предела()
        {
            PlayerHitEffects instance = Object.Instantiate(Service());
            var body = new GameObject("Body");
            var part = new GameObject("Hitbox_Torso");
            try
            {
                part.transform.SetParent(body.transform, false);
                part.transform.localPosition = Vector3.up;

                Vector3 hitPoint = part.transform.position + Vector3.forward * 0.15f;
                GameObject stain = instance.PlaceStain(part.transform, hitPoint, Vector3.forward);
                Assert.IsNotNull(stain, "Пятно не поставлено.");
                Assert.AreSame(part.transform, stain.transform.parent, "Пятно не дочернее части тела — не пойдёт за костью.");

                Transform hole = stain.transform.Find(PlayerHitEffects.HoleName);
                Assert.IsNotNull(hole, "Нет отверстия в точке попадания.");
                Assert.Less(Vector3.Distance(hole.position, hitPoint), 0.01f, "Отверстие не в точке попадания.");

                for (int i = 0; i < 20; i++) instance.PlaceStain(part.transform, hitPoint, Vector3.forward);
                int max = new UnityEditor.SerializedObject(instance).FindProperty("_maxStainsPerBody").intValue;
                Assert.LessOrEqual(PlayerHitEffects.CountStains(body.transform), max, "Пятна копятся на теле без предела.");

                for (int i = 0; i < 30; i++) instance.PlaySound(Vector3.zero, false, false);
                Assert.LessOrEqual(instance.VoiceCount, new UnityEditor.SerializedObject(instance).FindProperty("_maxVoices").intValue,
                    "Пул звуков растёт без предела.");
            }
            finally
            {
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(instance.gameObject);
            }
        }
    }
}
