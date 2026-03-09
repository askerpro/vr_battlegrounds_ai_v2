using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Player;
using Mirror;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Виджет для отображения здоровья.
    /// Вешается на элемент HUD префаба.
    /// </summary>
    public class HUDWidget_Health : MonoBehaviour
    {
        [SerializeField] private Slider _healthBar;
        [SerializeField] private Text _healthText;

        private PlayerController _localPlayer;

        private void Start()
        {
            // Поскольку HUD спавнится локальному игроку локальным PlayerHUDManager'ом,
            // NetworkClient.localPlayer - это 100% владелец этого интерфейса.
            if (NetworkClient.localPlayer != null)
            {
                _localPlayer = NetworkClient.localPlayer.GetComponent<PlayerController>();
            }
        }

        private void Update()
        {
            if (_localPlayer == null) return;

            float currentHealth = _localPlayer.Health;
            float maxHealth = 100f; 

            if (_healthBar != null)
            {
                _healthBar.value = currentHealth / maxHealth;
            }

            if (_healthText != null)
            {
                _healthText.text = $"{Mathf.CeilToInt(currentHealth)} HP";
            }
        }
    }
}