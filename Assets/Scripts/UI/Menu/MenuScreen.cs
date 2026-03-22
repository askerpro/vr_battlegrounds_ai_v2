using UnityEngine;
using UnityEngine.Events;

namespace VrBattlegrounds.UI.Menu
{
    public class MenuScreen : MonoBehaviour
    {
        [SerializeField] private MenuScreenType _screenType;
        
        public MenuScreenType ScreenType => _screenType;

        public UnityEvent OnShow;
        public UnityEvent OnHide;

        public virtual void Show()
        {
            gameObject.SetActive(true);
            OnShow?.Invoke();
        }

        public virtual void Hide()
        {
            gameObject.SetActive(false);
            OnHide?.Invoke();
        }
    }
}
