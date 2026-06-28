using System;
using System.Collections.Generic;

namespace PK
{
    [Serializable]
    public class GuestLoginRequest
    {
        public string device_id;
    }

    [Serializable]
    public class GuestLoginResponse
    {
        public string player_id;
        public string token;
        public Profile profile;
    }

    [Serializable]
    public class Profile
    {
        public int level;
        public long xp;
        public long gold;
        public int spins;
        public int shield_count;
        public int current_island;
    }

    [Serializable]
    public class SpinResponse
    {
        public string spin_id;
        public SpinResult result;
        public Balances balances;
    }

    [Serializable]
    public class SpinResult
    {
        public string type;
        // Server-side SpinService populates `amount` directly (e.g. gold amount,
        // free-spin count). Kept as a top-level field so the Unity client can read
        // result.amount without parsing the payload dictionary (bug from user-agent).
        public long amount;
        public DictionaryStringObject payload;
    }

    [Serializable]
    public class Balances
    {
        public long gold;
        public int spins;
        public int shield_count;
    }

    [Serializable]
    public class IslandStateResponse
    {
        public int current_island;
        public Building[] buildings;
    }

    [Serializable]
    public class Building
    {
        public int slot;
        public int level;
    }

    [Serializable]
    public class IslandUpgradeRequest
    {
        public int slot;
    }

    [Serializable]
    public class IslandUpgradeResponse
    {
        public Building upgraded;
        public Balances balances;
        public DictionaryStringObject island;
    }

    [Serializable]
    public class AttackStartResponse
    {
        public string attack_session_id;
        public AttackTarget target;
    }

    [Serializable]
    public class AttackTarget
    {
        public string player_id;
        // Bug #3 (r11): human-readable target name from the server (bots get
        // themed Vietnamese pirate names). Empty for older server builds.
        public string name;
        public int current_island;
        public int shield_count;
    }

    [Serializable]
    public class AttackResolveRequest
    {
        public string attack_session_id;
        public ClientInput client_input = new();
    }

    [Serializable]
    public class ClientInput
    {
        public string tap_result = "auto";
        // Bug #6 (r16): chest_index (0-2) sent by the client during the raid
        // mini-game so the server can apply the per-chest multiplier. -1 = not
        // set (plain attack path, no chest pick).
        public int chest_index = -1;
    }

    [Serializable]
    public class AttackResolveResponse
    {
        public AttackResolveResult result;
        public Balances balances;
    }

    [Serializable]
    public class AttackResolveResult
    {
        public bool success;
        public long gold_stolen;
        public bool shield_consumed;
        // Bug #6 (r16): multiplier applied by the server for the chosen chest
        // (1, 1.5, or 2). Older server builds omit this field -> default 1.
        public float chest_multiplier = 1f;
    }

    // Unity JsonUtility không hỗ trợ Dictionary<K,V>, nên dùng wrapper đơn giản.
    [Serializable]
    public class DictionaryStringObject
    {
        public List<Entry> entries = new();

        [Serializable]
        public class Entry
        {
            public string key;
            public string value; // lưu string để hiển thị; backend payload hiện đơn giản
        }
    }

    [Serializable]
    public class ApiErrorEnvelope
    {
        public ApiError error;
    }

    [Serializable]
    public class ApiError
    {
        public string code;
        public string message;
        public DictionaryStringObject details;
    }

    // === Sprint-5: Event system (agent-15-unity-lead) ===

    [Serializable]
    public class GameEventInfo
    {
        public int id;
        public string eventCode;
        public string displayName;
        public string eventType;
        public string startAt;
        public string endAt;
        public string config;
    }

    [Serializable]
    public class GameEventListResponse
    {
        public GameEventInfo[] events;
    }

    [Serializable]
    public class EventClaimRequest
    {
        public int eventId;
        public string claimData;
    }

    [Serializable]
    public class EventClaimResponse
    {
        public int claimId;
        public int eventId;
        public string status;
        public string claimedAt;
        // Bug #2 (hardcore-r2): the server now returns the parsed reward_type and
        // numeric reward_amount so the client can show exactly what was credited.
        public string reward_type;
        public long reward_amount;
    }

    [Serializable]
    public class EventClaimHistoryResponse
    {
        public EventClaimInfo[] claims;
    }

    [Serializable]
    public class EventClaimInfo
    {
        public int claimId;
        public int eventId;
        public string eventCode;
        public string status;
        public string claimedAt;
    }

    // === Bug #2 (r4): daily free spins response ===

    [Serializable]
    public class DailyRewardResponse
    {
        public int spins_granted;
        public bool replayed;
        public Balances balances;
    }
}
