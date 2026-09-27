using UnityEngine;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Показывает перечисленные элементы экрана только админу (хост или сессия с флагом
    /// <c>IsAdmin</c>, <see cref="MenuPlayersTeams.IsLocalAdmin"/>). Лежит на самом экране,
    /// а не на скрываемой кнопке: выключенный объект свой <c>Update</c> уже не получит.
    /// Права это не проверяет — их проверяет сервер; здесь только чтобы не показывать лишнего.
    /// </summary>
    public class AdminOnlyElements : MonoBehaviour
    {
        [SerializeField] private GameObject[] _elements = new GameObject[0];

        private void OnEnable() => Apply();

        private void Update() => Apply();

        private void Apply()
        {
            bool admin = MenuPlayersTeams.IsLocalAdmin();
            foreach (GameObject element in _elements)
            {
                if (element != null && element.activeSelf != admin) element.SetActive(admin);
            }
        }
    }
}
