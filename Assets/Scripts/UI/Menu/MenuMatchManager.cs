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
    /// (<see cref="AdminMapCommands.IsAvailable(MapCommand)"/> по реплицированному
    /// состоянию): «Пауза» — во время матча, «Продолжить» — на паузе, «Следующая карта» —
    /// в разминке идущей серии (на последней карте надпись «В лобби»). Нажатие уходит на
    /// сервер командой сессии (<c>PlayerSession.CmdAdminMapCommand</c>), право и
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
            Wire(_startMatchButton, MapCommand.GoLive);
            Wire(_pauseButton, MapCommand.Pause);
            Wire(_resumeButton, MapCommand.Resume);
            Wire(_stopButton, MapCommand.Stop);
            Wire(_nextMapButton, MapCommand.NextMap);

            if (_nextMapButton != null) _nextMapLabel = _nextMapButton.GetComponentInChildren<TMPro.TMP_Text>(true);
        }

        private void Wire(Button button, MapCommand command)
        {
            if (button != null) button.onClick.AddListener(() => Send(command));
        }

        private void Update()
        {
            bool admin = MenuPlayersTeams.IsLocalAdmin();
            bool any = false;

            any |= Show(_startMatchButton, admin && AdminMapCommands.IsAvailable(MapCommand.GoLive));
            any |= Show(_pauseButton, admin && AdminMapCommands.IsAvailable(MapCommand.Pause));
            any |= Show(_resumeButton, admin && AdminMapCommands.IsAvailable(MapCommand.Resume));
            any |= Show(_stopButton, admin && AdminMapCommands.IsAvailable(MapCommand.Stop));
            any |= Show(_nextMapButton, admin && AdminMapCommands.IsAvailable(MapCommand.NextMap));

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

        private static void Send(MapCommand command)
        {
            if (PlayerSession.LocalSession == null)
            {
                GameLog.UI.Warning($"[MenuMatchManager] {command}: нет локальной сессии.");
                return;
            }

            GameLog.UI.Info($"[MenuMatchManager] Админ: {command}.");
            PlayerSession.LocalSession.CmdAdminMapCommand(command);
        }
    }
}
