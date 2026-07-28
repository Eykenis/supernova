using UnityEngine;

namespace Supernova.UI
{
    [DisallowMultipleComponent]
    public sealed class SciFiUiStyler : MonoBehaviour
    {
        [SerializeField] private SciFiUiScope scope = SciFiUiScope.MainMenu;

        public SciFiUiScope Scope => scope;

        public void Configure(SciFiUiScope value)
        {
            scope = value;
        }

        private void OnEnable()
        {
            if (scope == SciFiUiScope.GameHud)
                SciFiUiSkin.ApplyGameHud(transform);
            else
                SciFiUiSkin.ApplyMainMenu(transform);
        }
    }
}
