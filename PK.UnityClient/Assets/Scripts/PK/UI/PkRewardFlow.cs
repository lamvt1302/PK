using System.Collections;
using UnityEngine;

namespace PK.UI
{
    /// <summary>
    /// Reward flow orchestrator: shake -> coin burst -> big win popup (neu isBigWin) -> haptic.
    /// Type: "gold_big", "gold_small", "shield", "attack", "raid", "spin_bonus".
    /// Moi type co intensity khac nhau (gold_big = biggest).
    /// TODO: Prefab setup can tao trong Unity Editor â€” gan PkBigWinPopup, PkCoinBurst,
    /// PkScreenShake vao Inspector. Dat script len GameObject "RewardFlow" trong HUD.
    /// </summary>
    public class PkRewardFlow : MonoBehaviour
    {
        [Header("Refs")]
        public PkBigWinPopup BigWinPopup;
        public PkCoinBurst CoinBurst;
        public PkScreenShake ScreenShake;

        [Header("Coin count per type")]
        public int CoinCountBigWin = 20;
        public int CoinCountNormal = 8;

        private Coroutine _flowRoutine;

        /// <summary>Thuc hien reward flow theo amount + type + isBigWin flag.</summary>
        public void ShowReward(long amount, string type, bool isBigWin)
        {
            if (_flowRoutine != null) StopCoroutine(_flowRoutine);
            _flowRoutine = StartCoroutine(RunFlow(amount, type, isBigWin));
        }

        private IEnumerator RunFlow(long amount, string type, bool isBigWin)
        {
            var cfg = GetConfig(type);

            // 1) Screen shake.
            if (ScreenShake != null) ScreenShake.Shake(cfg.shakeIntensity, cfg.shakeDuration);

            // 2) Coin burst tu giua canvas (origin = (0,0) trong container).
            if (CoinBurst != null)
            {
                int count = isBigWin ? CoinCountBigWin : CoinCountNormal;
                CoinBurst.Spawn(count, Vector2.zero);
            }

            // 3) Big win popup (chi khi isBigWin).
            if (isBigWin && BigWinPopup != null)
            {
                yield return new WaitForSeconds(cfg.shakeDuration * 0.6f);
                BigWinPopup.Show(amount);
            }

            // 4) Haptic feedback â€” chua implement, chi log.
            // TODO: goi Handheld.Vibrate() hoac native haptic khi port mobile.
            Debug.Log($"[PkRewardFlow] Haptic (TODO) type={type} amount={amount} bigWin={isBigWin}");

            yield break;
        }

        private readonly struct RewardConfig
        {
            public readonly float shakeIntensity;
            public readonly float shakeDuration;
            public RewardConfig(float si, float sd) { shakeIntensity = si; shakeDuration = sd; }
        }

        private static RewardConfig GetConfig(string type)
        {
            // gold_big = biggest. Cac type khac giam dan.
            return type switch
            {
                "gold_big"    => new RewardConfig(18f, 0.45f),
                "raid"        => new RewardConfig(15f, 0.40f),
                "attack"      => new RewardConfig(12f, 0.35f),
                "gold_small"  => new RewardConfig(8f,  0.25f),
                "shield"      => new RewardConfig(6f,  0.22f),
                "spin_bonus"  => new RewardConfig(6f,  0.22f),
                _             => new RewardConfig(8f,  0.25f),
            };
        }
    }
}