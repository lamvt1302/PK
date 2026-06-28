using UnityEngine;
using UnityEngine.UI;

namespace PK.UI
{
    /// <summary>
    /// Right side icon buttons: Pet, Cards, Friends, Shop, Settings. CÅ© lÃ  text
    /// info panel, má»›i lÃ  icon buttons (giá»‘ng Coin Master).
    /// KhÃ´i phá»¥c tá»« HUD procedural cÅ© (Ä‘Ã£ bá»), tÃ¡ch thÃ nh panel riÃªng.
    /// TODO: Prefab setup cáº§n táº¡o trong Unity Editor â€” gÃ¡n 5 Button (icon) vÃ o
    /// Inspector. Icon sprites gÃ¡n trá»±c tiáº¿p trÃªn Image cá»§a má»—i Button.
    /// Logic chÆ°a implement, chá»‰ log â€” sáº½ hook sau.
    /// </summary>
    public class PkRightPanel : MonoBehaviour
    {
        [Header("Icon buttons")]
        public Button PetButton;
        public Button CardsButton;
        public Button FriendsButton;
        public Button ShopButton;
        public Button SettingsButton;

        /// <summary>Event raised when Pet button clicked.</summary>
        public event System.Action OnPet;
        /// <summary>Event raised when Cards button clicked.</summary>
        public event System.Action OnCards;
        /// <summary>Event raised when Friends button clicked.</summary>
        public event System.Action OnFriends;
        /// <summary>Event raised when Shop button clicked.</summary>
        public event System.Action OnShop;
        /// <summary>Event raised when Settings button clicked.</summary>
        public event System.Action OnSettings;

        private void Awake()
        {
            Bind(PetButton, "Pet", () => OnPet?.Invoke());
            Bind(CardsButton, "Cards", () => OnCards?.Invoke());
            Bind(FriendsButton, "Friends", () => OnFriends?.Invoke());
            Bind(ShopButton, "Shop", () => OnShop?.Invoke());
            Bind(SettingsButton, "Settings", () => OnSettings?.Invoke());
        }

        /// <summary>Enable/disable táº¥t cáº£ icon buttons (dÃ¹ng khi IsBusy).</summary>
        public void SetInteractable(bool interactable)
        {
            if (PetButton != null) PetButton.interactable = interactable;
            if (CardsButton != null) CardsButton.interactable = interactable;
            if (FriendsButton != null) FriendsButton.interactable = interactable;
            if (ShopButton != null) ShopButton.interactable = interactable;
            if (SettingsButton != null) SettingsButton.interactable = interactable;
        }

        private void Bind(Button btn, string label, System.Action action)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                Debug.Log($"[PkRightPanel] {label} clicked");
                action();
            });
        }
    }
}