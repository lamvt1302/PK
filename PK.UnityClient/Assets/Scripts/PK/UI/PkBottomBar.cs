using UnityEngine;
using UnityEngine.UI;

namespace PK.UI
{
    /// <summary>
    /// Bottom area: nÃºt phá»¥ (Events, Shop, Settings). KhÃ´i phá»¥c tá»« HUD procedural
    /// cÅ© (Ä‘Ã£ bá»), tÃ¡ch thÃ nh panel riÃªng.
    /// TODO: Prefab setup cáº§n táº¡o trong Unity Editor â€” gÃ¡n 3 Button vÃ o Inspector.
    /// Hook events lÃªn PkHudManager hoáº·c PkBootstrap.
    /// </summary>
    public class PkBottomBar : MonoBehaviour
    {
        [Header("Buttons")]
        public Button EventsButton;
        public Button ShopButton;
        public Button SettingsButton;

        /// <summary>Event raised when Events button clicked.</summary>
        public event System.Action OnEvents;
        /// <summary>Event raised when Shop button clicked.</summary>
        public event System.Action OnShop;
        /// <summary>Event raised when Settings button clicked.</summary>
        public event System.Action OnSettings;

        private void Awake()
        {
            Bind(EventsButton, () => OnEvents?.Invoke());
            Bind(ShopButton, () => OnShop?.Invoke());
            Bind(SettingsButton, () => OnSettings?.Invoke());
        }

        /// <summary>Enable/disable táº¥t cáº£ nÃºt (dÃ¹ng khi IsBusy).</summary>
        public void SetInteractable(bool interactable)
        {
            if (EventsButton != null) EventsButton.interactable = interactable;
            if (ShopButton != null) ShopButton.interactable = interactable;
            if (SettingsButton != null) SettingsButton.interactable = interactable;
        }

        private void Bind(Button btn, System.Action action)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => action());
        }
    }
}