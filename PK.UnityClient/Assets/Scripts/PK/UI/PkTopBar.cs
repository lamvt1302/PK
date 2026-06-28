using System.Collections;
using TMPro;
using UnityEngine;

namespace PK.UI
{
    /// <summary>
    /// Top bar panel: hiá»ƒn thá»‹ gold, spins, shield.
    /// KhÃ´i phá»¥c logic wallet display tá»« HUD procedural cÅ© (Ä‘Ã£ bá»), tÃ¡ch thÃ nh panel riÃªng.
    /// TODO: Prefab setup cáº§n táº¡o trong Unity Editor â€” gÃ¡n 3 TextMeshProUGUI vÃ o Inspector.
    /// </summary>
    public class PkTopBar : MonoBehaviour
    {
        [Header("Text displays")]
        public TextMeshProUGUI GoldText;
        public TextMeshProUGUI SpinsText;
        public TextMeshProUGUI ShieldText;

        [Header("Count-up animation")]
        public float CountUpDuration = 0.6f;

        private long _displayedGold;
        private int _displayedSpins;
        private int _displayedShield;
        private Coroutine _goldCoroutine;

        /// <summary>
        /// Cáº­p nháº­t hiá»ƒn thá»‹ tá»« PkBootstrap state. Gá»i tá»« PkHudManager.Refresh().
        /// </summary>
        public void UpdateDisplay(long gold, int spins, int shields)
        {
            // Gold cháº¡y count-up animation, spins/shield set ngay.
            if (GoldText != null)
            {
                if (_goldCoroutine != null) StopCoroutine(_goldCoroutine);
                _goldCoroutine = StartCoroutine(CountUpGold(_displayedGold, gold));
            }
            _displayedGold = gold;

            _displayedSpins = spins;
            _displayedShield = shields;

            if (SpinsText != null) SpinsText.text = spins.ToString();
            if (ShieldText != null) ShieldText.text = shields.ToString();
        }

        /// <summary>Set text trá»±c tiáº¿p (khÃ´ng animation) â€” dÃ¹ng cho init.</summary>
        public void SetImmediate(long gold, int spins, int shields)
        {
            _displayedGold = gold;
            _displayedSpins = spins;
            _displayedShield = shields;
            if (GoldText != null) GoldText.text = FormatGold(gold);
            if (SpinsText != null) SpinsText.text = spins.ToString();
            if (ShieldText != null) ShieldText.text = shields.ToString();
        }

        private IEnumerator CountUpGold(long from, long to)
        {
            if (GoldText == null) yield break;
            float t = 0f;
            while (t < CountUpDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / CountUpDuration);
                long v = (long)Mathf.Lerp(from, to, k);
                GoldText.text = FormatGold(v);
                yield return null;
            }
            GoldText.text = FormatGold(to);
            _goldCoroutine = null;
        }

        private static string FormatGold(long gold)
        {
            // Äá»‹nh dáº¡ng ngáº¯n gá»n: 1.2M / 15K cho sá»‘ lá»›n (giá»‘ng Coin Master).
            if (gold >= 1_000_000L) return (gold / 1_000_000.0).ToString("0.##") + "M";
            if (gold >= 1_000L) return (gold / 1_000.0).ToString("0.#") + "K";
            return gold.ToString();
        }
    }
}