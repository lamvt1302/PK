using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PK.UI
{
    /// <summary>
    /// Big win popup: coin count-up + fanfare effect. KhÃ´i phá»¥c tá»« HUD procedural
    /// cÅ© (Ä‘Ã£ bá»), tÃ¡ch thÃ nh panel riÃªng. Láº¯ng nghe PkBootstrap.BigWin(long).
    /// TODO: Prefab setup cáº§n táº¡o trong Unity Editor â€” gÃ¡n CanvasGroup, TextMeshProUGUI
    /// (amount), Button (close), Animator (fanfare) vÃ o Inspector.
    /// </summary>
    public class PkBigWinPopup : MonoBehaviour
    {
        [Header("Refs")]
        public CanvasGroup CanvasGroup;
        public TextMeshProUGUI AmountText;
        public Button CloseButton;
        public Animator FanfareAnimator;

        [Header("Animation")]
        public float CountUpDuration = 1.2f;
        public float DisplayDuration = 2.5f;

        private Coroutine _routine;

        private void Awake()
        {
            // áº¨n máº·c Ä‘á»‹nh.
            SetVisible(false);
            if (CloseButton != null)
            {
                CloseButton.onClick.RemoveAllListeners();
                CloseButton.onClick.AddListener(Hide);
            }
        }

        /// <summary>Hiá»ƒn thá»‹ popup Big Win vá»›i amount gold, cháº¡y count-up + fanfare.</summary>
        public void Show(long amount)
        {
            SetVisible(true);
            if (FanfareAnimator != null) FanfareAnimator.SetTrigger("Fanfare");
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(RunShow(amount));
        }

        /// <summary>áº¨n popup.</summary>
        public void Hide()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            SetVisible(false);
        }

        private IEnumerator RunShow(long amount)
        {
            // Count-up 0 -> amount.
            float t = 0f;
            while (t < CountUpDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / CountUpDuration);
                long v = (long)Mathf.Lerp(0, amount, k);
                if (AmountText != null) AmountText.text = FormatGold(v);
                yield return null;
            }
            if (AmountText != null) AmountText.text = FormatGold(amount);

            // Hiá»ƒn thá»‹ rá»“i tá»± áº©n sau DisplayDuration.
            yield return new WaitForSeconds(DisplayDuration);
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