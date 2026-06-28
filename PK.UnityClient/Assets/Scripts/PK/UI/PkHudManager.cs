using TMPro;
using UnityEngine;

namespace PK.UI
{
    /// <summary>
    /// MonoBehaviour chÃ­nh quáº£n lÃ½ táº¥t cáº£ UI panels. KhÃ´i phá»¥c orchestration logic
    /// tá»« HUD procedural cÅ© (168KB, Ä‘Ã£ bá») nhÆ°ng tÃ¡ch nhá» â€” má»—i panel 1 script.
    /// Láº¯ng nghe cÃ¡c event tá»« PkBootstrap: Changed, SpinStarted, SpinResultReceived,
    /// BigWin, AttackResolved, RaidChestPickReady, RaidResolved, IslandUpgraded.
    /// TODO: Prefab setup cáº§n táº¡o trong Unity Editor â€” gÃ¡n references tá»›i cÃ¡c panel
    /// component + PkBootstrap vÃ o Inspector. Táº¡o HUD prefab rá»—ng chá»©a cÃ¡c panel.
    /// </summary>
    public class PkHudManager : MonoBehaviour
    {
        [Header("Bootstrap (game logic)")]
        public PK.PkBootstrap Bootstrap;

        [Header("Panels")]
        public PkTopBar TopBar;
        public PkSlotMachine SlotMachine;
        public PkBottomBar BottomBar;
        public PkRightPanel RightPanel;
        public PkBigWinPopup BigWinPopup;
        public PkAttackOverlay AttackOverlay;
        public PkRaidOverlay RaidOverlay;

        [Header("Status")]
        public TextMeshProUGUI StatusText;

        private void Awake()
        {
            // Hook PkBootstrap events.
            if (Bootstrap != null)
            {
                Bootstrap.Changed += OnBootstrapChanged;
                Bootstrap.SpinStarted += OnSpinStarted;
                Bootstrap.SpinResultReceived += OnSpinResultReceived;
                Bootstrap.BigWin += OnBigWin;
                Bootstrap.AttackResolved += OnAttackResolved;
                Bootstrap.RaidChestPickReady += OnRaidChestPickReady;
                Bootstrap.RaidResolved += OnRaidResolved;
                Bootstrap.IslandUpgraded += OnIslandUpgraded;
            }

            // Hook Raid chest pick -> Bootstrap.ResolveRaid.
            if (RaidOverlay != null) RaidOverlay.OnChestPicked += OnRaidChestPicked;
        }

        private void Start()
        {
            // Initial refresh.
            Refresh();
        }

        private void OnDestroy()
        {
            if (Bootstrap != null)
            {
                Bootstrap.Changed -= OnBootstrapChanged;
                Bootstrap.SpinStarted -= OnSpinStarted;
                Bootstrap.SpinResultReceived -= OnSpinResultReceived;
                Bootstrap.BigWin -= OnBigWin;
                Bootstrap.AttackResolved -= OnAttackResolved;
                Bootstrap.RaidChestPickReady -= OnRaidChestPickReady;
                Bootstrap.RaidResolved -= OnRaidResolved;
                Bootstrap.IslandUpgraded -= OnIslandUpgraded;
            }
            if (RaidOverlay != null) RaidOverlay.OnChestPicked -= OnRaidChestPicked;
        }

        /// <summary>
        /// Update toÃ n bá»™ UI tá»« PkBootstrap state. Gá»i khi Bootstrap.Changed fire.
        /// KhÃ´i phá»¥c logic hiá»ƒn thá»‹ wallet + status + buttons enable/disable.
        /// </summary>
        public void Refresh()
        {
            if (Bootstrap == null) return;

            // Top bar: gold, spins, shield.
            if (TopBar != null)
                TopBar.UpdateDisplay(Bootstrap.Gold, Bootstrap.Spins, Bootstrap.ShieldCount);

            // Status text.
            if (StatusText != null) StatusText.text = Bootstrap.StatusMessage;

            // Slot machine result text + color.
            if (SlotMachine != null)
                SlotMachine.SetResultText(Bootstrap.LastSpinResultText, Bootstrap.LastSpinResultColor);

            // Buttons enable/disable theo IsBusy / IsLoggedIn.
            bool canAct = Bootstrap.IsLoggedIn && !Bootstrap.IsBusy;
            if (SlotMachine != null) SlotMachine.SetSpinInteractable(canAct && Bootstrap.Spins > 0);
            if (BottomBar != null) BottomBar.SetInteractable(!Bootstrap.IsBusy);
            if (RightPanel != null) RightPanel.SetInteractable(!Bootstrap.IsBusy);
        }

        /// <summary>Hiá»ƒn thá»‹ Big Win popup (gá»i tá»« event hoáº·c manual).</summary>
        public void ShowBigWin(long amount)
        {
            if (BigWinPopup != null) BigWinPopup.Show(amount);
        }

        /// <summary>Hiá»ƒn thá»‹ Attack overlay (gá»i tá»« event hoáº·c manual).</summary>
        public void ShowAttack(long stolenGold, string targetName)
        {
            if (AttackOverlay != null) AttackOverlay.Show(stolenGold, targetName);
        }

        /// <summary>Hiá»ƒn thá»‹ Raid chest picker (gá»i tá»« event hoáº·c manual).</summary>
        public void ShowRaid(string targetName)
        {
            if (RaidOverlay != null) RaidOverlay.Show(targetName);
        }

        // === Event handlers tá»« PkBootstrap ===

        private void OnBootstrapChanged() => Refresh();

        private void OnSpinStarted()
        {
            // Animation cháº¡y reel random, result set khi SpinResultReceived tá»›i.
            // resultKeys null = cycle random rá»“i dá»«ng default.
            if (SlotMachine != null) SlotMachine.SpinAnimation(null, null);
        }

        private void OnSpinResultReceived(string resultType, long amount)
        {
            // Dá»«ng 3 reel trÃªn symbol tÆ°Æ¡ng á»©ng result type (3 reel cÃ¹ng symbol).
            if (SlotMachine != null)
            {
                var keys = new[] { resultType, resultType, resultType };
                // SpinAnimation Ä‘ang cháº¡y sáº½ dá»«ng trÃªn keys; náº¿u khÃ´ng cháº¡y thÃ¬ set trá»±c tiáº¿p.
                SlotMachine.SetReelSymbols(0, resultType);
                SlotMachine.SetReelSymbols(1, resultType);
                SlotMachine.SetReelSymbols(2, resultType);
            }
        }

        private void OnBigWin(long amount) => ShowBigWin(amount);

        private void OnAttackResolved(long stolen, string target) => ShowAttack(stolen, target);

        private void OnRaidChestPickReady(string target) => ShowRaid(target);

        private void OnRaidResolved(long stolen, string target, float mult, bool blocked)
        {
            if (RaidOverlay != null) RaidOverlay.ShowReveal(stolen, mult, blocked);
        }

        private void OnIslandUpgraded(int slot, int newLevel)
        {
            // TODO: flash slot label green + "NÃ‚NG Cáº¤P!" popup (cÃ³ thá»ƒ thÃªm PkUpgradePopup).
            Debug.Log($"[PkHudManager] Island upgraded: slot {slot} -> Lv.{newLevel}");
        }

        private void OnRaidChestPicked(int chestIndex)
        {
            if (Bootstrap != null) Bootstrap.ResolveRaid(chestIndex);
        }
    }
}