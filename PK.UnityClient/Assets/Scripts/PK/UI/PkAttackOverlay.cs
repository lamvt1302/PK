using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PK.UI
{
    /// <summary>
    /// Attack overlay: hammer animation + target info + result. KhÃ´i phá»¥c tá»«
    /// HUD procedural cÅ© (Ä‘Ã£ bá»), tÃ¡ch thÃ nh panel riÃªng. Láº¯ng nghe PkBootstrap.AttackResolved.
    /// TODO: Prefab setup cáº§n táº¡o trong Unity Editor â€” gÃ¡n CanvasGroup, Animator
    /// (hammer), TextMeshProUGUI (target/result), Button (close) vÃ o Inspector.
    /// </summary>
    public class PkAttackOverlay : MonoBehaviour
    {
        [Header("Refs")]
        public CanvasGroup CanvasGroup;
        public Animator HammerAnimator;
        public TextMeshProUGUI TargetText;
        public TextMeshProUGUI ResultText;
        public Button CloseButton;

        [Header("Timing")]
        public float HammerDelay = 0.2f;
        public float ResultDuration = 2.5f;

        private Coroutine _routine;

        private void Awake()
        {
            SetVisible(false);
            if (CloseButton != null)
            {
                CloseButton.onClick.RemoveAllListeners();
                CloseButton.onClick.AddListener(Hide);
            }
        }

        /// <summary>Hiá»ƒn thá»‹ overlay attack vá»›i stolen gold + target name.</summary>
        public void Show(long stolenGold, string targetName)
        {
            SetVisible(true);
            if (TargetText != null) TargetText.text = string.IsNullOrEmpty(targetName) ? "Äáº£o Ä‘á»‹ch" : targetName;
            if (ResultText != null) ResultText.text = "";
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(RunAttack(stolenGold));
        }

        /// <summary>áº¨n overlay.</summary>
        public void Hide()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            SetVisible(false);
        }

        private IEnumerator RunAttack(long stolenGold)
        {
            // Hammer Ä‘á»£i rá»“i Ä‘Ã¡nh.
            if (HammerAnimator != null)
            {
                yield return new WaitForSeconds(HammerDelay);
                HammerAnimator.SetTrigger("Hit");
            }

            // Hiá»ƒn thá»‹ káº¿t quáº£ cÆ°á»›p Ä‘Æ°á»£c.
            if (ResultText != null)
            {
                ResultText.text = stolenGold > 0
                    ? $"+{FormatGold(stolenGold)} VÃ€NG!"
                    : "Bá»‹ khiÃªn cháº·n!";
            }

            yield return new WaitForSeconds(ResultDuration);
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