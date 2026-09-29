using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран админа «Матч» (<see cref="MenuScreenType.MatchManager"/>): «Начать матч»,
    /// «Пауза», «Продолжить», «Следующая карта», «Стоп».
    ///
    /// <para>
    /// Каждая кнопка видна только админу и только когда имеет смысл
    /// (<see cref="AdminMatchCommands.IsAvailable(MatchCommand)"/> по реплицированному
    /// состоянию): «Пауза» — во время матча, «Продолжить» — на паузе, «Следующая карта» —
    /// в разминке идущей серии (на последней карте надпись «В лобби»). Нажатие уходит на
    /// сервер командой сессии (<c>PlayerSession.CmdAdminMatchCommand</c>), право и
    /// уместность сервер проверяет ещё раз.
    /// </para>
    /// </summary>
    public class MenuMatchManager : MenuScreen
    {
        [SerializeField] private Button _startMatchButton;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _stopButton;

        [Tooltip("«Следующая карта»: серия сама после конца карты дальше не идёт — переход только по этой кнопке.")]
        [SerializeField] private Button _nextMapButton;

        private TMPro.TMP_Text _nextMapLabel;

        [Tooltip("Надпись для не-админа или когда кнопок нет.")]
        [SerializeField] private TMPro.TMP_Text _hint;

        private void Awake()
        {
            Wire(_startMatchButton, MatchCommand.StartMatch);
            Wire(_pauseButton, MatchCommand.Pause);
            Wire(_resumeButton, MatchCommand.Resume);
            Wire(_stopButton, MatchCommand.Stop);
            Wire(_nextMapButton, MatchCommand.NextMap);

            if (_nextMapButton != null) _nextMapLabel = _nextMapButton.GetComponentInChildren<TMPro.TMP_Text>(true);
        }

        private void Wire(Button button, MatchCommand command)
        {
            if (button != null) button.onClick.AddListener(() => Send(command));
        }

        private void Update()
        {
            bool admin = MenuPlayersTeams.IsLocalAdmin();
            bool any = false;

            any |= Show(_startMatchButton, admin && AdminMatchCommands.IsAvailable(MatchCommand.StartMatch));
            any |= Show(_pauseButton, admin && AdminMatchCommands.IsAvailable(MatchCommand.Pause));
            any |= Show(_resumeButton, admin && AdminMatchCommands.IsAvailable(MatchCommand.Resume));
            any |= Show(_stopButton, admin && AdminMatchCommands.IsAvailable(MatchCommand.Stop));
            any |= Show(_nextMapButton, admin && AdminMatchCommands.IsAvailable(MatchCommand.NextMap));

            if (_nextMapLabel != null)
            {
                string label = Series.Instance != null && Series.Instance.IsLastMap ? "В лобби" : "Следующая карта";
                if (_nextMapLabel.text != label) _nextMapLabel.text = label;
            }

            if (_hint != null)
            {
                _hint.gameObject.SetActive(!any);
                _hint.text = admin ? "Сейчас управлять нечем: серия не идёт." : "Управлять матчем может только админ.";
            }
        }

        private static bool Show(Button button, bool visible)
        {
            if (button != null && button.gameObject.activeSelf != visible) button.gameObject.SetActive(visible);
            return visible;
        }

        private static void Send(MatchCommand command)
        {
            if (PlayerSession.LocalSession == null)
            {
                GameLog.UI.Warning($"[MenuMatchManager] {command}: нет локальной сессии.");
                return;
            }

            GameLog.UI.Info($"[MenuMatchManager] Админ: {command}.");
            PlayerSession.LocalSession.CmdAdminMatchCommand(command);
        }

        /// <summary>«Начать матч» — для старых привязок кнопок в инспекторе.</summary>
        public void OnStartMatchPressed() => Send(MatchCommand.StartMatch);

        /// <summary>«Стоп» — конец всей серии, возврат в лобби.</summary>
        public void OnStopMatchPressed() => Send(MatchCommand.Stop);
    }
}
