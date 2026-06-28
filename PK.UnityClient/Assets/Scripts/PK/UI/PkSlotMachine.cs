using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PK.UI
{
    /// <summary>
    /// Slot machine panel: 3 reels + nÃºt Spin. KhÃ´i phá»¥c logic spin animation tá»«
    /// HUD procedural cÅ© (Ä‘Ã£ bá»), tÃ¡ch thÃ nh panel riÃªng.
    /// TODO: Prefab setup cáº§n táº¡o trong Unity Editor â€” gÃ¡n 3 Image (reel) + 1 Button
    /// (spin) + TextMeshProUGUI (result) vÃ o Inspector. Symbol sprites cáº§n gÃ¡n trong
    /// SymbolMap (key = "gold_big","shield","attack","raid","spin_bonus","gold_small").
    /// </summary>
    public class PkSlotMachine : MonoBehaviour
    {
        [Header("Reels")]
        public Image[] Reels = new Image[3];
        public float SpinDuration = 1.5f;
        public float ReelStopStagger = 0.25f;

        [Header("UI")]
        public Button SpinButton;
        public TextMeshProUGUI ResultText;

        [Header("Symbol sprites (server result type -> sprite)")]
        public List<SymbolEntry> SymbolMap = new();

        [Serializable]
        public class SymbolEntry
        {
            public string key;
            public Sprite sprite;
        }

        private Sprite[] _currentSymbols = new Sprite[3];
        private Coroutine _spinCoroutine;

        /// <summary>Äáº·t sprite cho 1 reel theo tÃªn symbol (key trong SymbolMap).</summary>
        public void SetReelSymbols(int reelIndex, string symbolName)
        {
            if (reelIndex < 0 || reelIndex >= Reels.Length) return;
            var sprite = ResolveSymbol(symbolName);
            _currentSymbols[reelIndex] = sprite;
            if (Reels[reelIndex] != null && sprite != null) Reels[reelIndex].sprite = sprite;
        }

        /// <summary>Äáº·t sprite cho 1 reel trá»±c tiáº¿p báº±ng Sprite.</summary>
        public void SetReelSprite(int reelIndex, Sprite sprite)
        {
            if (reelIndex < 0 || reelIndex >= Reels.Length) return;
            _currentSymbols[reelIndex] = sprite;
            if (Reels[reelIndex] != null) Reels[reelIndex].sprite = sprite;
        }

        /// <summary>Hiá»ƒn thá»‹ text káº¿t quáº£ spin (giá»‘ng LastSpinResultText cÅ©).</summary>
        public void SetResultText(string text, Color color)
        {
            if (ResultText != null)
            {
                ResultText.text = text;
                ResultText.color = color;
            }
        }

        /// <summary>Enable/disable nÃºt spin theo IsBusy / IsLoggedIn.</summary>
        public void SetSpinInteractable(bool interactable)
        {
            if (SpinButton != null) SpinButton.interactable = interactable;
        }

        /// <summary>
        /// Cháº¡y animation spin: cycle random symbols rá»“i dá»«ng trÃªn result symbols.
        /// onComplete gá»i khi 3 reel Ä‘Ã£ dá»«ng. resultKeys = 3 symbol key (server type).
        /// </summary>
        public void SpinAnimation(string[] resultKeys, Action onComplete)
        {
            if (_spinCoroutine != null) StopCoroutine(_spinCoroutine);
            _spinCoroutine = StartCoroutine(RunSpin(resultKeys, onComplete));
        }

        private IEnumerator RunSpin(string[] resultKeys, Action onComplete)
        {
            var sprites = new Sprite[3];
            for (int i = 0; i < 3; i++)
                sprites[i] = resultKeys != null && i < resultKeys.Length
                    ? ResolveSymbol(resultKeys[i])
                    : ResolveSymbol("gold_small");

            // Cycle random symbols trong SpinDuration.
            float t = 0f;
            float interval = 0.05f;
            var randomKeys = new[] { "gold_small", "shield", "attack", "raid", "spin_bonus", "gold_big" };
            while (t < SpinDuration)
            {
                for (int i = 0; i < 3; i++)
                    if (Reels[i] != null) Reels[i].sprite = ResolveSymbol(randomKeys[UnityEngine.Random.Range(0, randomKeys.Length)]);
                t += interval;
                yield return new WaitForSeconds(interval);
            }

            // Dá»«ng tá»«ng reel so le.
            for (int i = 0; i < 3; i++)
            {
                if (Reels[i] != null && sprites[i] != null) Reels[i].sprite = sprites[i];
                _currentSymbols[i] = sprites[i];
                yield return new WaitForSeconds(ReelStopStagger);
            }

            _spinCoroutine = null;
            onComplete?.Invoke();
        }

        private Sprite ResolveSymbol(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var e in SymbolMap)
                if (e.key == key) return e.sprite;
            return SymbolMap.Count > 0 ? SymbolMap[0].sprite : null;
        }
    }
}