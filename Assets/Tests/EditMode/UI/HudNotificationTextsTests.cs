using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.UI.HUD;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Информационный HUD: каждая смена фазы и каждая смерть — сообщение со звуком
    /// (<see cref="HudNotificationTexts"/>, <see cref="HUDWidget_GameNotification"/>).
    /// </summary>
    public class HudNotificationTextsTests
    {
        private const string HudPrefabPath = "Assets/Prefabs/UI/HUD/EliminationHUD.prefab";

        [TestCase(RoundState.Setup)]
        [TestCase(RoundState.Equipment)]
        [TestCase(RoundState.Countdown)]
        [TestCase(RoundState.Combat)]
        [TestCase(RoundState.Resolution)]
        public void Смена_фазы_звучит(RoundState state)
        {
            Assert.AreEqual(HudSound.Beep, HudNotificationTexts.Phase(state, null).Sound, $"{state}: переход без звука");
        }

        [TestCase(RoundState.Setup)]
        [TestCase(RoundState.Equipment)]
        [TestCase(RoundState.Countdown)]
        [TestCase(RoundState.Combat)]
        [TestCase(RoundState.Scoreboard)]
        public void Смена_фазы_видна_на_HUD(RoundState state)
        {
            Assert.IsNotEmpty(HudNotificationTexts.Phase(state, null).Text, $"{state}: нет сообщения");
        }

        [Test]
        public void Итог_боя_без_текста_чтобы_не_затереть_победителя()
        {
            Assert.IsNull(HudNotificationTexts.Phase(RoundState.Resolution, null).Text);
        }

        [Test]
        public void Итоги_раунда_показывают_счёт()
        {
            StringAssert.Contains("CT 1 — 0 T", HudNotificationTexts.Phase(RoundState.Scoreboard, "CT 1 — 0 T").Text);
        }

        private static KillNotice Kill(uint victim, int victimTeam, uint killer, int killerTeam) =>
            new KillNotice(victim, "V" + victim, victimTeam, killer, killer != 0 ? "K" + killer : "", killerTeam);

        [Test]
        public void Своя_смерть_тревога_и_имя_убийцы()
        {
            HudMessage m = HudNotificationTexts.Kill(Kill(10, 1, 20, 2), localSessionNetId: 10, localTeam: 1);
            Assert.AreEqual(HudSound.Alert, m.Sound);
            StringAssert.Contains("Вы погибли", m.Text);
            StringAssert.Contains("K20", m.Text);
        }

        [Test]
        public void Своё_убийство_пик()
        {
            HudMessage m = HudNotificationTexts.Kill(Kill(10, 2, 20, 1), localSessionNetId: 20, localTeam: 1);
            Assert.AreEqual(HudSound.Beep, m.Sound);
            StringAssert.Contains("Вы убили V10", m.Text);
        }

        [Test]
        public void Смерть_союзника_и_противника_различаются()
        {
            StringAssert.StartsWith("Союзник", HudNotificationTexts.Kill(Kill(10, 1, 20, 2), 30, 1).Text);
            StringAssert.StartsWith("Противник", HudNotificationTexts.Kill(Kill(10, 2, 20, 1), 30, 1).Text);
        }

        [Test]
        public void Самоубийство_без_убийцы()
        {
            HudMessage m = HudNotificationTexts.Kill(Kill(10, 1, 10, 1), 10, 1);
            Assert.AreEqual("Вы погибли", m.Text);
        }

        [TestCase(false, false, true, TestName = "Мёртвый вне зоны — вернуться")]
        [TestCase(false, true, false, TestName = "Мёртвый в своей зоне — молчать")]
        [TestCase(true, false, false, TestName = "Живой вне зоны — молчать")]
        public void Напоминание_вернуться_на_базу(bool alive, bool inOwnZone, bool expected)
        {
            Assert.AreEqual(expected, HudNotificationTexts.AskReturnToBase(alive, inOwnZone));
        }

        [Test]
        public void У_виджета_в_HUD_назначены_оба_звука()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            Assert.IsNotNull(prefab, "Нет " + HudPrefabPath);

            var widget = prefab.GetComponentInChildren<HUDWidget_GameNotification>(true);
            Assert.IsNotNull(widget, "В HUD нет информационного виджета");

            // Объект был выключен в префабе (коммит 4da5ef4) — Start не выполнялся, и HUD молчал
            // обо всём. Невидимость без сообщения даёт CanvasGroup.alpha = 0, а не выключенный объект.
            // activeInHierarchy у ассета префаба всегда false (он не в сцене) — проверяем activeSelf по цепочке.
            for (Transform t = widget.transform; t != null; t = t.parent)
                Assert.IsTrue(t.gameObject.activeSelf, $"'{t.name}' выключен в префабе — информационный HUD молчит.");

            var so = new SerializedObject(widget);
            Assert.IsNotNull(so.FindProperty("_beep").objectReferenceValue, "Не назначен звук смены фазы (_beep)");
            Assert.IsNotNull(so.FindProperty("_alert").objectReferenceValue, "Не назначен звук своей смерти (_alert)");

            var audio = so.FindProperty("_audio").objectReferenceValue as AudioSource;
            Assert.IsNotNull(audio, "Нет AudioSource");
            Assert.AreEqual(0f, audio.spatialBlend, "Звук HUD должен быть 2D");
            Assert.IsFalse(audio.playOnAwake);
        }
    }
}
