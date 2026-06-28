using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PK.UI
{
    /// <summary>
    /// Raid chest picker: 3 chest buttons + reveal animation + reward display.
    /// KhÃ´i phá»¥c tá»« HUD procedural cÅ© (Ä‘Ã£ bá»), tÃ¡ch thÃ nh panel riÃªng. Láº¯ng nghe
    /// PkBootstrap.RaidChestPickReady (show picker) + RaidResolved (reveal).
    /// TODO: Prefab setup cáº§n táº¡o trong Unity Editor â€” gÃ¡n CanvasGroup, 3 Button
    /// (chest), 3 Animator/Image (chest reveal), TextMeshProUGUI (target/reward),
    /// Button (close) vÃ o Inspector.
    /// </summary>
    public class PkRaidOverlay : MonoBehaviour
    {
        [Header("Refs")]
        public CanvasGroup CanvasGroup;
        public Button[] ChestButtons = new Button[3];
        public Animator[] ChestAnimators = new Animator[3];
        public TextMeshProUGUI TargetText;
        public TextMeshProUGUI RewardText;
        public Button CloseButton;

        [Header("Timing")]
        public float RevealDuration = 2.5f;

        /// <summary>Event raised when player picks a chest (index 0-2).</summary>
        public event System.Action<int> OnChestPicked;

        private Coroutine _routine;
        private int _pendingPick = -1;

        private void Awake()
        {
            SetVisible(false);
            if (CloseButton != null)
            {
                CloseButton.onClick.RemoveAllListeners();
                CloseButton.onClick.AddListener(Hide);
            }
            for (int i = 0; i < ChestButtons.Length; i++)
            {
                int idx = i;
                if (ChestButtons[i] != null)
                {
                    ChestButtons[i].onClick.RemoveAllListeners();
                    ChestButtons[i].onClick.AddListener(() => PickChest(idx));
                }
            }
        }

        /// <summary>Hiá»ƒn thá»‹ picker 3 rÆ°Æ¡ng vá»›i target name.</summary>
        public void Show(string targetName)
        {
            SetVisible(true);
            _pendingPick = -1;
            if (TargetText != null) TargetText.text = string.IsNullOrEmpty(targetName) ? "Äáº£o Ä‘á»‹ch" : targetName;
            if (RewardText != null) RewardText.text = "Chá»n 1 trong 3 rÆ°Æ¡ng!";
            // Reset nÃºt chá»n Ä‘Æ°á»£c.
            for (int i = 0; i < ChestButtons.Length; i++)
                if (ChestButtons[i] != null) ChestButtons[i].interactable = true;
        }

        /// <summary>Player chá»n chest index 0-2. Notify PkHudManager -> PkBootstrap.ResolveRaid.</summary>
        public void PickChest(int chestIndex)
        {
            if (_pendingPick >= 0) return; // Ä‘Ã£ chá»n
            _pendingPick = chestIndex;
            // KhÃ³a nÃºt cÃ²n láº¡i.
            for (int i = 0; i < ChestButtons.Length; i++)
                if (ChestButtons[i] != null) ChestButtons[i].interactable = (i == chestIndex);
            OnChestPicked?.Invoke(chestIndex);
        }

        /// <summary>Reveal káº¿t quáº£ raid sau khi server resolve.</summary>
        public void ShowReveal(long stolenGold, float multiplier, bool shieldBlocked)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(RunReveal(stolenGold, multiplier, shieldBlocked));
        }

        /// <summary>áº¨n overlay.</summary>
        public void Hide()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            SetVisible(false);
        }

        private IEnumerator RunReveal(long stolenGold, float multiplier, bool shieldBlocked)
        {
            // Má»Ÿ rÆ°Æ¡ng Ä‘Ã£ chá»n.
            if (_pendingPick >= 0 && _pendingPick < ChestAnimators.Length && ChestAnimators[_pendingPick] != null)
                ChestAnimators[_pendingPick].SetTrigger("Open");

            // Hiá»ƒn thá»‹ reward.
            if (RewardText != null)
            {
                RewardText.text = shieldBlocked
                    ? "Bá»‹ khiÃªn cháº·n!"
                    : $"+{FormatGold(stolenGold)} VÃ€NG! (x{multiplier:0.0})";
            }

            yield return new WaitForSeconds(RevealDuration);
            Hide();
        }

        private void SetVisible(bool visible)
        {
            if (CanvasGroup != null)
            {
                CanvasGroup.alpha = visible ? 1f : 0f;
                CanvasGroup.interactable = visible;
                CanvasGroup.blocksRaycasts = visible;
            }
            else
            {
                gameObject.SetActive(visible);
            }
        }

        private static string FormatGold(long gold)
        {
            if (gold >= 1_000_000L) return (gold / 1_000_000.0).ToString("0.##") + "M";
            if (gold >= 1_000L) return (gold / 1_000.0).ToString("0.#") + "K";
            return gold.ToString();
        }
    }
}