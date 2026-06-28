using System;
using UnityEngine;

namespace PK
{
    public class PkBootstrap : MonoBehaviour
    {
        public ApiConfig Config;
        // r23: SystemInfo.deviceUniqueIdentifier cannot be called from a field
        // initializer (Unity throws in editor). Populate it in Awake() instead.
        public string DeviceId = "";

        private AuthState _auth;
        private RequestIdStore _spinRequestIds;
        private RequestIdStore _upgradeRequestIds;
        private RequestIdStore _attackStartRequestIds;
        private RequestIdStore _attackResolveRequestIds;
        private PkApiClient _api;

        public long Gold { get; private set; }
        public int Spins { get; private set; }
        public int ShieldCount { get; private set; }
        public int CurrentIsland { get; private set; }
        public IslandStateResponse IslandState { get; private set; }
        public string CurrentAttackSessionId { get; private set; }
        public AttackTarget CurrentAttackTarget { get; private set; }
        public string StatusMessage { get; private set; } = "Sẵn sàng";

        // === Bug #6 (r16): raid mini-game state ===
        // When a spin lands "raid", we start an attack session but DON'T resolve
        // it immediately — instead we stash the session id + target name and
        // raise RaidChestPickReady so the HUD shows the 3-chest picker overlay.
        // The player taps a chest; ResolveRaid(chestIndex) sends chest_index to
        // the server, which applies the per-chest multiplier (1x/1.5x/2x).
        public string PendingRaidSessionId { get; private set; }
        public string PendingRaidTargetName { get; private set; }
        /// <summary>True while a raid chest-pick is waiting for the player.</summary>
        public bool IsRaidPending => !string.IsNullOrWhiteSpace(PendingRaidSessionId);

        // === Bug #4: slot selection for upgrade ===
        /// <summary>
        /// The slot currently selected in the right-panel slot picker. The Upgrade
        /// button upgrades this slot (defaults to 1). Cycled via the HUD's
        /// left/right arrows.
        /// </summary>
        public int SelectedUpgradeSlot { get; private set; } = 1;

        // === UX state (fixes from user-agent test) ===

        /// <summary>
        /// True once a guest login has succeeded. The HUD uses this to disable the
        /// SPIN button until logged in (bug #2) and to hide the Login button after
        /// success / reshow it on failure (bug #15).
        /// </summary>
        public bool IsLoggedIn { get; private set; }

        /// <summary>
        /// Global in-flight flag. While true, all HUD action buttons are disabled
        /// to prevent double-tap duplicate requests (bugs #7, #8).
        /// </summary>
        public bool IsBusy { get; private set; }

        /// <summary>
        /// Human-readable text describing the most recent spin result, shown in the
        /// slot-machine placeholder instead of the static "coming soon" string
        /// (bug #3). Empty until the first successful spin.
        /// </summary>
        public string LastSpinResultText { get; private set; } = "";

        /// <summary>
        /// Accent color for <see cref="LastSpinResultText"/>, chosen by reward type
        /// (gold = gold, shield = green, attack/raid = red, spins = blue).
        /// </summary>
        public Color LastSpinResultColor { get; private set; } = Color.white;

        // === Bug #1 (hardcore-r2): the raw result type + amount of the most
        // recent spin, exposed so the slot reel animation can stop its reels on
        // symbols that correspond to the actual server result instead of random
        // decorative symbols. Empty until the first successful spin response.
        // ===
        /// <summary>The server result type of the most recent spin (e.g. "gold_big").</summary>
        public string LastSpinResultType { get; private set; } = "";

        /// <summary>The numeric amount of the most recent spin reward (0 for attack/raid).</summary>
        public long LastSpinAmount { get; private set; }

        // === Bug #6 (r16): true when the most recent spin landed "raid". The HUD
        // uses this to decide whether the Attack button starts a raid (chest pick)
        // or a plain attack (instant resolve).
        // ===
        public bool LastSpinIsRaid { get; private set; }

        /// <summary>
        /// True when there is a pending spin request that can be retried with the
        /// same request id (i.e. the last spin attempt failed and we have not yet
        /// cleared the idempotency key). The HUD uses this to disable the Retry
        /// button when there is nothing to retry (bug from user-agent).
        /// </summary>
        public bool CanRetrySpin => _spinRequestIds.Current != null;

        // Sprint-5: Events (agent-15-unity-lead)
        public GameEventInfo[] ActiveEvents { get; private set; } = Array.Empty<GameEventInfo>();

        public event Action Changed;

        /// <summary>
        /// Bug #2: raised whenever a spin is submitted (SpinOnce / RetryLastSpin).
        /// The HUD uses this to kick off the slot spin animation coroutine, which
        /// cycles random symbols for ~1.5s before settling on the actual result.
        /// </summary>
        public event Action SpinStarted;

        // === Bug #1 (hardcore-r2): raised when the spin API response arrives,
        // carrying the server result type + amount. The HUD's reel animation
        // coroutine listens for this so it can stop the reels on symbols that
        /// match the actual result (instead of random decorative symbols). If the
        // reels are still spinning when this fires, they stop on result symbols;
        // if this fires before the animation starts, the result is cached.
        // ===
        public event Action<string, long> SpinResultReceived;

        // === Bug #4 (hardcore-r2): raised when a gold_big reward of at least 200
        // gold lands, so the HUD can show a celebratory Big Win popup.
        // ===
        public event Action<long> BigWin;

        // === Bug #3 (r10): raised when an attack resolves, carrying the gold
        // stolen and a friendly target name. The HUD listens to this so it can
        // play the attack scene overlay (dark bg + explosion + reward count-up)
        // instead of just snapping a status string.
        // ===
        public event Action<long, string> AttackResolved;

        // === Bug #6 (r16): raised when a spin lands "raid" and the attack session
        // has been started. The HUD shows the 3-chest picker overlay and calls
        // ResolveRaid(chestIndex) when the player picks a chest.
        // ===
        public event Action<string> RaidChestPickReady;

        // === Bug #6 (r16): raised when the raid resolves, carrying the gold
        // stolen, the target name, the chosen chest multiplier, and whether the
        // target's shield blocked the raid. The HUD plays a chest-reveal overlay.
        // ===
        public event Action<long, string, float, bool> RaidResolved;

        // === Bug #5 (r10): raised when an island slot upgrades, carrying the slot
        // index and the new level. The HUD listens to this so it can flash the slot
        // label green and show a brief "NÂNG CẤP!" popup.
        // ===
        public event Action<int, int> IslandUpgraded;

        private void Awake()
        {
            // r23: moved here from field initializer — Unity forbids calling
            // SystemInfo.deviceUniqueIdentifier in a field initializer/constructor.
            if (string.IsNullOrEmpty(DeviceId))
            {
                DeviceId = SystemInfo.deviceUniqueIdentifier;
            }
            if (Config == null)
            {
                Config = ScriptableObject.CreateInstance<ApiConfig>();
            }

            _auth = new AuthState();
            _spinRequestIds = new RequestIdStore();
            _upgradeRequestIds = new RequestIdStore();
            _attackStartRequestIds = new RequestIdStore();
            _attackResolveRequestIds = new RequestIdStore();
            _api = new PkApiClient(Config, _auth);

            // Ensure we always have a unique, non-empty DeviceId per install.
            // SystemInfo.deviceUniqueIdentifier can be empty on some platforms;
            // fall back to a persisted GUID stored in PlayerPrefs so the device
            // keeps a stable identity across sessions.
            if (string.IsNullOrWhiteSpace(DeviceId))
            {
                DeviceId = PlayerPrefs.GetString("PK.DeviceId", "");
                if (string.IsNullOrWhiteSpace(DeviceId))
                {
                    DeviceId = System.Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString("PK.DeviceId", DeviceId);
                    PlayerPrefs.Save();
                }
            }
        }

        private void Start()
        {
            // Bug #1: Auto-login on Start so the user doesn't have to hunt for the
            // Login button. We show a brief "Connecting..." state while the request
            // is in flight; the HUD reflects this via StatusMessage/IsBusy.
            SetStatus("Đang kết nối...");
            Login();
        }

        public void Login()
        {
            // Bug #7/#8: guard against double-tap while a request is in flight.
            if (IsBusy)
            {
                return;
            }

            SetBusy(true);
            SetStatus("Đang đăng nhập...");
            StartCoroutine(_api.GuestLogin(DeviceId, onOk: r =>
            {
                ApplyProfile(r.profile);
                IsLoggedIn = true; // bug #2/#15
                SetStatus("Đã đăng nhập");
                RefreshIsland();
                // Bug #11: auto-fetch active events right after login so the events
                // panel is populated without the user pressing the Events button.
                FetchActiveEvents();
                SetBusy(false);
            }, onErr: e =>
            {
                IsLoggedIn = false; // bug #15: keep Login button visible on failure
                SetStatus(FriendlyError(e));
                Debug.LogError($"Login failed: {e}");
                SetBusy(false);
            }));
        }

        public void RefreshProfile()
        {
            if (IsBusy)
            {
                return;
            }

            SetBusy(true);
            SetStatus("Đang tải hồ sơ...");
            StartCoroutine(_api.GetProfile(onOk: p =>
            {
                ApplyProfile(p);
                SetStatus("Đã cập nhật hồ sơ");
                SetBusy(false);
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Profile failed: {e}");
                SetBusy(false);
            }));
        }

        public void RefreshIsland()
        {
            SetStatus("Đang tải đảo...");
            StartCoroutine(_api.GetIslandState(onOk: r =>
            {
                IslandState = r;
                CurrentIsland = r.current_island;
                SetStatus("Đã cập nhật đảo");
                NotifyChanged();
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Island failed: {e}");
            }));
        }

        public void RefreshAll()
        {
            RefreshProfile();
            RefreshIsland();
        }

        /// <summary>
        /// Bug #2 (r4): claims the daily free-spin reward from the server (5 spins,
        /// idempotent per UTC day). Replaces the dead "Tính năng đang phát triển!"
        /// placeholder so players always have a path to more spins.
        /// </summary>
        public void ClaimDailyFreeSpins()
        {
            if (IsBusy)
            {
                return;
            }

            SetBusy(true);
            SetStatus("Đang nhận lượt quay miễn phí...");
            StartCoroutine(_api.ClaimDailyReward(onOk: r =>
            {
                if (r != null && r.balances != null)
                {
                    ApplyBalances(r.balances);
                }
                int granted = r != null ? r.spins_granted : 5;
                bool replayed = r != null && r.replayed;
                // Bug #2: show a clear success message. A replayed claim (same day)
                // means the player already claimed today, so reflect that.
                SetStatus(replayed
                    ? "Hôm nay đã nhận rồi, quay lại ngày mai nha!"
                    : $"Nhận được {granted} lượt quay!");
                Debug.Log($"Daily reward ok: granted={granted} replayed={replayed}");
                SetBusy(false);
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Daily reward failed: {e}");
                SetBusy(false);
            }));
        }

        public void SpinOnce()
        {
            if (IsBusy)
            {
                return;
            }

            // Bug #4: block spin client-side when the player has no spins left,
            // so we don't even fire a request that the server will reject.
            if (Spins <= 0)
            {
                SetStatus("Hết lượt quay! Chờ nhận lượt miễn phí");
                return;
            }

            SetBusy(true);
            var rid = _spinRequestIds.NewAction();
            SetStatus("Đang quay...");
            SpinStarted?.Invoke();
            StartCoroutine(_api.Spin(rid, onOk: r =>
            {
                ApplyBalances(r.balances);
                _spinRequestIds.Clear();
                SetStatus($"Quay: {r.result.type}");
                // Bug #3: surface the spin result to the slot-machine area.
                SetSpinResultDisplay(r.result);
                Debug.Log($"Spin ok: {r.result.type}");
                SetBusy(false);
            }, onErr: e =>
            {
                // Bug #1: when the server returns 429 RATE_LIMITED (spam spin), the
                // RateLimitErrorMiddleware now emits a friendly JSON body. FriendlyError
                // maps it to "Quay nhanh quá! Chờ vài giây nha." instead of the old
                // generic "Spin failed, can retry" which made players think the game
                // was lagging/broken.
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Spin failed (can retry with same request id): {e}");
                SetBusy(false);
            }));
        }

        public void RetryLastSpin()
        {
            if (IsBusy)
            {
                return;
            }

            if (_spinRequestIds.Current == null)
            {
                SetStatus("Không có lượt quay đang chờ");
                return;
            }

            SetBusy(true);
            var rid = _spinRequestIds.Current.Value;
            SetStatus("Đang quay lại...");
            SpinStarted?.Invoke();
            StartCoroutine(_api.Spin(rid, onOk: r =>
            {
                ApplyBalances(r.balances);
                _spinRequestIds.Clear();
                SetStatus($"Quay lại: {r.result.type}");
                SetSpinResultDisplay(r.result);
                SetBusy(false);
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Spin retry failed: {e}");
                SetBusy(false);
            }));
        }

        /// <summary>
        /// Bug #4: upgrades the currently selected slot (defaults to slot 1). The
        /// HUD cycles SelectedUpgradeSlot via the left/right arrow buttons.
        /// </summary>
        public void UpgradeSelectedSlot()
        {
            UpgradeIsland(SelectedUpgradeSlot <= 0 ? 1 : SelectedUpgradeSlot);
        }

        public void UpgradeIsland(int slot)
        {
            if (IsBusy)
            {
                return;
            }

            SetBusy(true);
            var rid = _upgradeRequestIds.NewAction();
            SetStatus($"Đang nâng slot {slot}...");
            StartCoroutine(_api.UpgradeIsland(rid, slot, onOk: r =>
            {
                ApplyBalances(r.balances);
                _upgradeRequestIds.Clear();
                RefreshIsland();
                int newLevel = r.upgraded != null ? r.upgraded.level : 0;
                int upgradedSlot = r.upgraded != null ? r.upgraded.slot : slot;
                SetStatus($"Đã nâng slot {upgradedSlot} lên Lv.{newLevel}");
                // Bug #5 (r10): notify the HUD so it can flash the slot label green
                // and show a brief "NÂNG CẤP!" popup.
                IslandUpgraded?.Invoke(upgradedSlot, newLevel);
                SetBusy(false);
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Upgrade failed: {e}");
                SetBusy(false);
            }));
        }

        /// <summary>
        /// Bug #4: cycle the selected upgrade slot. direction &lt; 0 goes to the
        /// previous slot, otherwise the next slot. Wraps within [1, maxSlot].
        /// </summary>
        public void CycleUpgradeSlot(int direction)
        {
            // Determine the maximum slot from the island state buildings (if known).
            int maxSlot = 1;
            if (IslandState?.buildings != null && IslandState.buildings.Length > 0)
            {
                foreach (var b in IslandState.buildings)
                {
                    if (b.slot > maxSlot) maxSlot = b.slot;
                }
            }
            else
            {
                // Default to 5 slots so the player can still cycle before island
                // data has loaded (matches the placeholder Coin Master 5-slot island).
                maxSlot = 5;
            }

            int next = SelectedUpgradeSlot + (direction < 0 ? -1 : 1);
            if (next < 1) next = maxSlot;
            if (next > maxSlot) next = 1;
            SelectedUpgradeSlot = next;
            SetStatus($"Đã chọn slot {next}");
            NotifyChanged();
        }

        public void StartAttack()
        {
            if (IsBusy)
            {
                return;
            }

            SetBusy(true);
            var rid = _attackStartRequestIds.NewAction();
            SetStatus("Đang tấn công...");
            StartCoroutine(_api.StartAttack(rid, onOk: r =>
            {
                CurrentAttackSessionId = r.attack_session_id;
                CurrentAttackTarget = r.target;
                _attackStartRequestIds.Clear();
                NotifyChanged();
                // Bug #6 (r16): raid triggers the chest-pick mini-game instead of
                // resolving instantly. Stash the session + target name and raise
                // RaidChestPickReady so the HUD shows the 3-chest picker overlay.
                // The player picks a chest; ResolveRaid(chestIndex) then resolves.
                if (LastSpinIsRaid)
                {
                    PendingRaidSessionId = r.attack_session_id;
                    PendingRaidTargetName = FormatAttackTargetName(r.target);
                    SetStatus("Chọn 1 trong 3 rương kho báu!");
                    RaidChestPickReady?.Invoke(PendingRaidTargetName);
                    SetBusy(false);
                }
                else
                {
                    // Bug #3: merge Attack + Resolve into a single tap. Immediately
                    // resolve the attack we just started so the player doesn't have
                    // to tap a separate Resolve button.
                    ResolveAttack();
                }
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Attack start failed: {e}");
                SetBusy(false);
            }));
        }

        /// <summary>
        /// Bug #3: single-tap attack flow. Convenience wrapper used by the HUD's
        /// Attack button so the player only taps once.
        /// </summary>
        public void AttackAndResolve()
        {
            StartAttack();
        }

        /// <summary>
        /// Bug #6 (r16): resolves a pending raid with the player's chosen chest
        /// index (0-2). The server applies a per-chest multiplier (1x/1.5x/2x) to
        /// the rolled gold payout. Falls back to the plain ResolveAttack path if
        /// no raid is pending.
        /// </summary>
        public void ResolveRaid(int chestIndex)
        {
            if (IsBusy)
            {
                return;
            }

            // If no raid is pending, fall back to the plain attack resolve path.
            if (string.IsNullOrWhiteSpace(PendingRaidSessionId))
            {
                ResolveAttack();
                return;
            }

            SetBusy(true);
            var rid = _attackResolveRequestIds.NewAction();
            var sessionId = PendingRaidSessionId;
            var targetName = PendingRaidTargetName;
            SetStatus("Đang mở rương...");
            StartCoroutine(_api.ResolveAttack(rid, sessionId, chestIndex, onOk: r =>
            {
                ApplyBalances(r.balances);
                _attackResolveRequestIds.Clear();
                long stolen = r.result != null ? r.result.gold_stolen : 0;
                bool shieldBlocked = r.result != null && r.result.shield_consumed;
                float mult = r.result != null ? r.result.chest_multiplier : 1f;
                SetStatus(shieldBlocked
                    ? "Bị khiên chặn! Không cướp được vàng."
                    : $"Cướp được {stolen} vàng! (x{mult:0.0})");
                // Clear raid + attack state.
                PendingRaidSessionId = null;
                PendingRaidTargetName = null;
                CurrentAttackSessionId = null;
                CurrentAttackTarget = null;
                NotifyChanged();
                RaidResolved?.Invoke(stolen, targetName, mult, shieldBlocked);
                SetBusy(false);
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Raid resolve failed: {e}");
                if (e != null && e.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PendingRaidSessionId = null;
                    PendingRaidTargetName = null;
                    CurrentAttackSessionId = null;
                    CurrentAttackTarget = null;
                    NotifyChanged();
                }
                SetBusy(false);
            }));
        }

        public void ResolveAttack()
        {
            if (IsBusy)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentAttackSessionId))
            {
                // Bug #3: friendly message when there is no active attack session
                // instead of a scary raw validation error.
                SetStatus("Chưa có mục tiêu! Bấm Tấn công trước nha.");
                return;
            }

            SetBusy(true);
            var rid = _attackResolveRequestIds.NewAction();
            SetStatus("Đang tấn công...");
            StartCoroutine(_api.ResolveAttack(rid, CurrentAttackSessionId, onOk: r =>
            {
                ApplyBalances(r.balances);
                _attackResolveRequestIds.Clear();
                long stolen = r.result != null ? r.result.gold_stolen : 0;
                bool shieldBlocked = r.result != null && r.result.shield_consumed;
                SetStatus(shieldBlocked
                    ? "Bị khiên chặn! Không cướp được vàng."
                    : $"Cướp được {stolen} vàng!");
                // Bug #3 (r10): capture the target name BEFORE clearing the
                // current target, then fire the event so the HUD can play the
                // attack scene animation.
                string targetName = CurrentAttackTarget != null
                    ? FormatAttackTargetName(CurrentAttackTarget)
                    : "Đảo địch";
                CurrentAttackSessionId = null;
                CurrentAttackTarget = null;
                NotifyChanged();
                AttackResolved?.Invoke(stolen, targetName);
                SetBusy(false);
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Attack resolve failed: {e}");
                // Bug #13: if the server can no longer find the session (e.g. it
                // expired or was already resolved), drop the local session so the
                // player isn't stuck trying to resolve a ghost attack forever.
                if (e != null && e.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CurrentAttackSessionId = null;
                    CurrentAttackTarget = null;
                    NotifyChanged();
                }
                SetBusy(false);
            }));
        }

        private void ApplyProfile(Profile p)
        {
            Gold = p.gold;
            Spins = p.spins;
            ShieldCount = p.shield_count;
            CurrentIsland = p.current_island;
            NotifyChanged();
        }

        private void ApplyBalances(Balances b)
        {
            Gold = b.gold;
            Spins = b.spins;
            ShieldCount = b.shield_count;
            NotifyChanged();
        }

        private void SetStatus(string message)
        {
            StatusMessage = message;
            NotifyChanged();
        }

        /// <summary>
        /// Bug #9: public hook so the HUD can surface a friendly status message
        /// (e.g. when the "Get Free Spins" placeholder is tapped).
        /// </summary>
        public void SetStatusPublic(string message) => SetStatus(message);

        /// <summary>
        /// Toggles the global busy flag and notifies the HUD so it can disable/enable
        /// all action buttons during in-flight requests (bugs #7, #8).
        /// </summary>
        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            NotifyChanged();
        }

        /// <summary>
        /// Builds the human-readable spin-result string and accent color shown in
        /// the slot-machine placeholder (bug #3). Reward types follow the server's
        /// SpinService.RollReward table: gold_small, gold_big, attack, raid,
        /// shield, spin_bonus.
        ///
        /// Hardcore-r2 fixes:
        ///   - Bug #1: caches the raw result type + amount and raises
        ///     <see cref="SpinResultReceived"/> so the HUD's slot reel animation
        ///     can stop its reels on symbols that correspond to the actual result.
        ///   - Bug #3: for attack/raid, the server no longer sends a fake 100
        ///     amount; the sub text now reads "ATTACK! Bấm Attack để cướp." with no
        ///     fake gold number. The real reward is shown after the attack resolves.
        ///   - Bug #4: raises <see cref="BigWin"/> for gold_big rewards of at least
        ///     200 gold so the HUD can show a celebratory popup.
        ///   - Bug #5: all user-facing strings are now Vietnamese.
        /// </summary>
        private void SetSpinResultDisplay(SpinResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.type))
            {
                LastSpinResultText = "";
                LastSpinResultColor = Color.white;
                LastSpinResultType = "";
                LastSpinAmount = 0;
                LastSpinIsRaid = false;
                NotifyChanged();
                return;
            }

            // Prefer the top-level result.amount (server now populates it directly;
            // see Models.cs SpinResult). Fall back to parsing the payload entries
            // only if amount was not provided (e.g. older server builds).
            long amount = result.amount;
            if (amount == 0 && result.payload?.entries != null)
            {
                foreach (var entry in result.payload.entries)
                {
                    if (long.TryParse(entry.value, out var v))
                    {
                        amount = v;
                        break;
                    }
                }
            }

            // Bug #3 (hardcore-r2): for attack/raid, the server now returns
            // amount=null/0 (the real reward comes from resolving the attack), so
            // we must NOT show a fake "TRÚNG 100 VÀNG!" line. The reward will be
            // shown after the attack resolves.
            bool isAttackOrRaid = result.type == "attack" || result.type == "raid";

            // Bug #1 (hardcore-r2): cache the raw type + amount and notify the
            // HUD so the reels can stop on matching symbols. For attack/raid the
            // amount is intentionally 0 (pending).
            LastSpinResultType = result.type;
            LastSpinAmount = isAttackOrRaid ? 0 : amount;
            SpinResultReceived?.Invoke(LastSpinResultType, LastSpinAmount);

            switch (result.type)
            {
                case "gold_small":
                case "gold_big":
                    // Bug #5 (hardcore-r2): Vietnamese.
                    LastSpinIsRaid = false;
                    LastSpinResultText = $"TRÚNG {amount} VÀNG!";
                    LastSpinResultColor = ColorGold;
                    break;
                case "spin_bonus":
                    // Bug #5 (hardcore-r2): Vietnamese.
                    LastSpinIsRaid = false;
                    LastSpinResultText = $"TRÚNG {amount} LƯỢT QUAY MIỄN PHÍ!";
                    LastSpinResultColor = ColorBlue;
                    break;
                case "shield":
                    // Bug #5 (hardcore-r2): Vietnamese.
                    LastSpinIsRaid = false;
                    LastSpinResultText = "NHẬN ĐƯỢC KHIÊN!";
                    LastSpinResultColor = ColorGreen;
                    break;
                case "attack":
                    // Bug #3 (hardcore-r2): no fake amount; the real reward comes
                    // from resolving the attack. Bug #5: Vietnamese.
                    // Bug #5 (r7): phân biệt rõ attack vs raid về mặt label để người
                    // chơi cảm thấy hai chế độ khác nhau (Coin Master có mini-game
                    // pick chest cho raid; PK chưa có nên ít nhất khác nhãn).
                    // Bug #6 (r16): attack resolves instantly (no chest pick).
                    LastSpinIsRaid = false;
                    LastSpinResultText = "TẤN CÔNG! Bấm Tấn công để cướp.";
                    LastSpinResultColor = ColorRed;
                    break;
                case "raid":
                    // Bug #3 (hardcore-r2): no fake amount. Bug #5: Vietnamese.
                    // Bug #5 (r7): raid dùng nhãn "CƯỚP KHO BÀU!" riêng.
                    // Bug #6 (r16): raid triggers the chest-pick mini-game — the
                    // player taps Tấn công to start the raid, then picks 1 of 3
                    // chests. The server applies a per-chest multiplier (1x/1.5x/2x).
                    LastSpinIsRaid = true;
                    LastSpinResultText = "CƯỚP KHO BÀU! Bấm Tấn công để chọn rương.";
                    LastSpinResultColor = ColorRed;
                    break;
                default:
                    LastSpinIsRaid = false;
                    LastSpinResultText = $"QUAY: {result.type}";
                    LastSpinResultColor = Color.white;
                    break;
            }

            // Bug #4 (hardcore-r2): for a big gold win, fire the BigWin event so
            // the HUD can show a celebratory popup. We also surface the amount via
            // a public hook so the HUD can read it without re-parsing.
            if (result.type == "gold_big" && amount >= 200)
            {
                BigWin?.Invoke(amount);
            }

            NotifyChanged();
        }

        // Palette mirrored from PkRuntimeHud so the HUD can just use the exposed
        // color without re-deriving it.
        private static readonly Color ColorGold = HexColor("#FFD700");
        private static readonly Color ColorRed = HexColor("#E53935");
        private static readonly Color ColorBlue = HexColor("#1976D2");
        private static readonly Color ColorGreen = HexColor("#43A047");

        private static Color HexColor(string hex)
        {
            if (hex == null || hex.Length < 7) return Color.white;
            byte r = System.Convert.ToByte(hex.Substring(1, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(3, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(5, 2), 16);
            return new Color32(r, g, b, 255);
        }

        private void NotifyChanged()
        {
            Changed?.Invoke();
        }

        /// <summary>
        /// Bug #3 (r10): builds a friendly target name for the attack scene overlay
        /// from an AttackTarget (e.g. "Người chơi a1b2c3d4"). Mirrors the HUD's
        /// FormatPlayerName so the overlay text matches the info panel.
        /// Bug #3 (r11): now prefers the server-provided Name (themed Vietnamese
        /// pirate names for bots) and only falls back to a short id when no name
        /// was sent (e.g. older server builds).
        /// </summary>
        private static string FormatAttackTargetName(AttackTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.player_id))
            {
                return "Đảo địch";
            }
            // Bug #3 (r11): prefer the human-readable name from the server.
            if (!string.IsNullOrWhiteSpace(target.name))
            {
                return target.name;
            }
            var trimmed = target.player_id.Trim();
            var shortId = trimmed.Length > 8 ? trimmed.Substring(0, 8) : trimmed;
            return "Người chơi " + shortId;
        }

        /// <summary>
        /// Bug #5: maps raw server/network error strings to friendly Vietnamese
        /// messages shown to the player. Recognises the known ApiError codes
        /// (RATE_LIMITED, NO_SPINS, MISSING_REQUEST_ID, INVALID_ARGUMENT / UUID)
        /// that the server returns in the JSON body, as well as the raw text that
        /// UnityWebRequest.error can produce (e.g. "429 Too Many Requests"). Falls
        /// back to a generic friendly message for anything unknown so technical
        /// details never leak to the player.
        /// </summary>
        private static string FriendlyError(string rawError)
        {
            if (string.IsNullOrWhiteSpace(rawError))
            {
                return "Có lỗi xíu, thử lại nha!";
            }

            // Normalise to uppercase for substring matching so casing from the
            // server JSON body or Unity's error text doesn't matter.
            var e = rawError.ToUpperInvariant();

            if (e.Contains("RATE_LIMITED") || e.Contains("429"))
            {
                return "Quay nhanh quá! Chờ vài giây nha.";
            }
            if (e.Contains("NO_SPINS"))
            {
                return "Hết lượt quay! Chờ nhận lượt miễn phí nha.";
            }
            if (e.Contains("MISSING_REQUEST_ID") || e.Contains("X-REQUEST-ID"))
            {
                return "Lỗi kết nối, thử lại nha!";
            }
            if (e.Contains("INVALID_ARGUMENT") || e.Contains("UUID") || e.Contains("NO_ATTACK_SESSION"))
            {
                return "Thao tác không hợp lệ, thử lại nha!";
            }
            if (e.Contains("EVENT_ALREADY_CLAIMED"))
            {
                return "Sự kiện này đã nhận rồi!";
            }
            return "Có lỗi xíu, thử lại nha!";
        }

        // === Sprint-5: Event methods (agent-15-unity-lead) ===

        public void FetchActiveEvents()
        {
            SetStatus("Đang tải sự kiện...");
            StartCoroutine(_api.GetActiveEvents(onOk: events =>
            {
                ActiveEvents = events;
                SetStatus($"Tìm thấy {events.Length} sự kiện đang hoạt động");
                Debug.Log($"Events fetched: {events.Length}");
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Event fetch failed: {e}");
            }));
        }

        public void ClaimFirstEvent()
        {
            if (IsBusy)
            {
                return;
            }

            if (ActiveEvents == null || ActiveEvents.Length == 0)
            {
                SetStatus("Không có sự kiện để nhận");
                return;
            }

            SetBusy(true);
            var claimedEvent = ActiveEvents[0];
            var eventId = claimedEvent.id;
            // Bug #7: parse the reward amount from the event config so we can show
            // the player exactly what they received.
            long rewardAmount = ParseEventRewardAmount(claimedEvent);
            SetStatus($"Đang nhận sự kiện {eventId}...");
            StartCoroutine(_api.ClaimEvent(eventId, onOk: r =>
            {
                // Bug #2 (hardcore-r2): the server now returns a numeric
                // reward_amount in the claim response. Prefer it over our local
                // parse when available so the displayed amount always matches the
                // gold actually credited by the server.
                long serverAmount = r.reward_amount;
                long displayAmount = serverAmount > 0 ? serverAmount : rewardAmount;

                // Bug #7: show the actual reward the player received (parsed from
                // the event config) and trigger the gold number animation by
                // refreshing the profile (balances update).
                if (displayAmount > 0)
                {
                    SetStatus($"Nhận được {displayAmount} vàng!");
                }
                else
                {
                    // Bug #5 (hardcore-r2): Vietnamese.
                    SetStatus($"Đã nhận sự kiện: {r.status}");
                }
                Debug.Log($"Event claim ok: id={r.claimId} status={r.status} reward={displayAmount}");

                // Bug #6: remove the claimed event from the displayed list
                // immediately so the UI no longer shows it (the player can't try
                // to claim it again). A full FetchActiveEvents() refresh follows
                // which will also reflect the server-side exclusion.
                if (ActiveEvents != null && ActiveEvents.Length > 0)
                {
                    var remaining = new System.Collections.Generic.List<GameEventInfo>();
                    foreach (var ev in ActiveEvents)
                    {
                        if (ev.id != eventId)
                        {
                            remaining.Add(ev);
                        }
                    }
                    ActiveEvents = remaining.ToArray();
                    NotifyChanged();
                }

                // Bug #12/#7: refresh events (server now excludes claimed) and
                // balances (gold jumps) so the UI reflects the new state.
                FetchActiveEvents();
                RefreshProfile();
                SetBusy(false);
            }, onErr: e =>
            {
                SetStatus(FriendlyError(e));
                Debug.LogWarning($"Event claim failed: {e}");
                SetBusy(false);
            }));
        }

        /// <summary>
        /// Bug #7: parses the reward amount (gold) from an event's config string.
        /// Looks for common keys like "reward_amount" or "gold" in the JSON config.
        /// Returns 0 if no amount could be parsed.
        ///
        /// Bug #2 (hardcore-r2): now also handles the compact "gold_500" /
        /// "spins_10" reward format where the value is a string of the form
        /// "&lt;type&gt;_&lt;number&gt;" (e.g. {"reward":"gold_500"}). The number
        /// after the underscore is extracted and returned. Direct numeric keys
        /// ("reward_amount", "gold", "amount") are still handled first.
        /// </summary>
        private static long ParseEventRewardAmount(GameEventInfo evt)
        {
            if (evt == null || string.IsNullOrWhiteSpace(evt.config))
            {
                return 0;
            }

            // The config is a JSON object string. JsonUtility can't parse arbitrary
            // dictionaries, so do a simple substring search for the reward fields.
            // This is a pragmatic placeholder parser; a real impl would use a JSON
            // parser, but we keep the dependency-free style of the client.
            var config = evt.config;
            long amount = ExtractLongValue(config, "\"reward_amount\"");
            if (amount <= 0)
            {
                amount = ExtractLongValue(config, "\"gold\"");
            }
            if (amount <= 0)
            {
                amount = ExtractLongValue(config, "\"amount\"");
            }

            // Bug #2 (hardcore-r2): handle the compact "gold_500" / "spins_10"
            // format stored under the "reward" key, where the value is a string
            // of the form "<type>_<number>". Extract the trailing number.
            if (amount <= 0)
            {
                amount = ParseTypedRewardAmount(config, "\"reward\"");
            }
            // Also try the "reward_type" key for completeness.
            if (amount <= 0)
            {
                amount = ParseTypedRewardAmount(config, "\"reward_type\"");
            }

            return amount;
        }

        /// <summary>
        /// Bug #2 (hardcore-r2): extracts the trailing number from a typed reward
        /// string value such as "gold_500" or "spins_10" found under the given JSON
        /// key. Returns 0 if the key is absent or the value doesn't end in a number.
        /// </summary>
        private static long ParseTypedRewardAmount(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return 0;
            }
            int idx = json.IndexOf(key, System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return 0;
            }
            // Scan forward from the key to find the string value (between quotes).
            int i = idx + key.Length;
            // Skip whitespace and the colon.
            while (i < json.Length && (json[i] == ' ' || json[i] == ':' || json[i] == '\t'))
            {
                i++;
            }
            if (i >= json.Length || json[i] != '"')
            {
                return 0; // not a string value
            }
            i++; // skip opening quote
            int start = i;
            while (i < json.Length && json[i] != '"')
            {
                i++;
            }
            if (i >= json.Length)
            {
                return 0; // unterminated string
            }
            string value = json.Substring(start, i - start);
            // Extract the trailing number after the last underscore (e.g. "gold_500" -> 500).
            int underscore = value.LastIndexOf('_');
            if (underscore >= 0 && underscore + 1 < value.Length)
            {
                string numPart = value.Substring(underscore + 1);
                if (long.TryParse(numPart, out var n) && n > 0)
                {
                    return n;
                }
            }
            // Also accept a pure-numeric string value.
            if (long.TryParse(value, out var direct) && direct > 0)
            {
                return direct;
            }
            return 0;
        }

        /// <summary>
        /// Bug #7: extracts an integer value following a JSON key like "reward_amount".
        /// Finds "key" then scans forward to the next number. Returns 0 on failure.
        /// </summary>
        private static long ExtractLongValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return 0;
            }
            int idx = json.IndexOf(key, System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return 0;
            }
            // Scan from after the key for the first digit (or minus).
            int i = idx + key.Length;
            while (i < json.Length && !char.IsDigit(json[i]) && json[i] != '-')
            {
                i++;
            }
            int start = i;
            if (start < json.Length && json[start] == '-') i++;
            while (i < json.Length && char.IsDigit(json[i]))
            {
                i++;
            }
            if (i == start || (i == start + 1 && json[start] == '-'))
            {
                return 0;
            }
            if (long.TryParse(json.Substring(start, i - start), out var v))
            {
                return v;
            }
            return 0;
        }
    }
}