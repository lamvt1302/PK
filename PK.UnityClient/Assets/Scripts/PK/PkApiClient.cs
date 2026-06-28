using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PK
{
    public class PkApiClient
    {
        private readonly ApiConfig _config;
        private readonly AuthState _auth;

        public PkApiClient(ApiConfig config, AuthState auth)
        {
            _config = config;
            _auth = auth;
        }

        private string Url(string path) => _config.BaseUrl.TrimEnd('/') + path;

        private static string ErrorText(UnityWebRequest req)
        {
            return string.IsNullOrWhiteSpace(req.downloadHandler?.text)
                ? req.error
                : req.downloadHandler.text;
        }

        public IEnumerator GuestLogin(string deviceId, Action<GuestLoginResponse> onOk, Action<string> onErr)
        {
            var reqObj = new GuestLoginRequest { device_id = deviceId };
            var json = JsonUtility.ToJson(reqObj);

            using var req = new UnityWebRequest(Url("/api/v1/player/guest-login"), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var resp = JsonUtility.FromJson<GuestLoginResponse>(req.downloadHandler.text);
            if (!string.IsNullOrWhiteSpace(resp?.token))
            {
                _auth.Token = resp.token;
            }
            onOk?.Invoke(resp);
        }

        public IEnumerator GetProfile(Action<Profile> onOk, Action<string> onErr)
        {
            using var req = new UnityWebRequest(Url("/api/v1/player/profile"), "GET");
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var profile = JsonUtility.FromJson<Profile>(req.downloadHandler.text);
            onOk?.Invoke(profile);
        }

        public IEnumerator Spin(Guid requestId, Action<SpinResponse> onOk, Action<string> onErr)
        {
            using var req = new UnityWebRequest(Url("/api/v1/spin"), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.SetRequestHeader("X-Request-Id", requestId.ToString());
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var resp = JsonUtility.FromJson<SpinResponse>(req.downloadHandler.text);
            onOk?.Invoke(resp);
        }

        public IEnumerator GetIslandState(Action<IslandStateResponse> onOk, Action<string> onErr)
        {
            using var req = new UnityWebRequest(Url("/api/v1/island/state"), "GET");
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var resp = JsonUtility.FromJson<IslandStateResponse>(req.downloadHandler.text);
            onOk?.Invoke(resp);
        }

        public IEnumerator UpgradeIsland(Guid requestId, int slot, Action<IslandUpgradeResponse> onOk, Action<string> onErr)
        {
            var json = JsonUtility.ToJson(new IslandUpgradeRequest { slot = slot });

            using var req = new UnityWebRequest(Url("/api/v1/island/upgrade"), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.SetRequestHeader("X-Request-Id", requestId.ToString());
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var resp = JsonUtility.FromJson<IslandUpgradeResponse>(req.downloadHandler.text);
            onOk?.Invoke(resp);
        }

        public IEnumerator StartAttack(Guid requestId, Action<AttackStartResponse> onOk, Action<string> onErr)
        {
            using var req = new UnityWebRequest(Url("/api/v1/attack/start"), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.SetRequestHeader("X-Request-Id", requestId.ToString());
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var resp = JsonUtility.FromJson<AttackStartResponse>(req.downloadHandler.text);
            onOk?.Invoke(resp);
        }

        public IEnumerator ResolveAttack(Guid requestId, string attackSessionId, Action<AttackResolveResponse> onOk, Action<string> onErr)
            => ResolveAttack(requestId, attackSessionId, -1, onOk, onErr);

        // Bug #6 (r16): overload accepting a chest_index (0-2) for the raid
        // mini-game. -1 = plain attack (no chest pick). The server applies a
        // per-chest multiplier when chest_index >= 0.
        public IEnumerator ResolveAttack(Guid requestId, string attackSessionId, int chestIndex, Action<AttackResolveResponse> onOk, Action<string> onErr)
        {
            var json = JsonUtility.ToJson(new AttackResolveRequest
            {
                attack_session_id = attackSessionId,
                client_input = new ClientInput { chest_index = chestIndex }
            });

            using var req = new UnityWebRequest(Url("/api/v1/attack/resolve"), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.SetRequestHeader("X-Request-Id", requestId.ToString());
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var resp = JsonUtility.FromJson<AttackResolveResponse>(req.downloadHandler.text);
            onOk?.Invoke(resp);
        }

        // === Sprint-5: Event API (agent-15-unity-lead) ===

        public IEnumerator GetActiveEvents(Action<GameEventInfo[]> onOk, Action<string> onErr)
        {
            using var req = new UnityWebRequest(Url("/api/v1/events/active"), "GET");
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            // API returns array directly — wrap for JsonUtility.
            // Bug #1 (r7): the server may return a single object `{...}` instead of
            // an array `[{...}]` when there is exactly 1 event. JsonUtility cannot
            // parse `{"events":{...}}` into a GameEventInfo[] (it silently yields an
            // empty array, which makes the HUD show "Sự kiện: không có"). Normalize
            // here: if the payload starts with `{` (single object), wrap it as an
            // array `[{...}]` before injecting it into the wrapper.
            var json = req.downloadHandler.text;
            var trimmed = json.TrimStart();
            if (trimmed.Length > 0 && trimmed[0] == '{')
            {
                json = "[" + json + "]";
            }
            var wrapped = "{\"events\":" + json + "}";
            var resp = JsonUtility.FromJson<GameEventListResponse>(wrapped);
            onOk?.Invoke(resp?.events ?? Array.Empty<GameEventInfo>());
        }

        public IEnumerator ClaimEvent(int eventId, Action<EventClaimResponse> onOk, Action<string> onErr)
        {
            var reqObj = new EventClaimRequest { eventId = eventId, claimData = "{}" };
            var json = JsonUtility.ToJson(reqObj);

            using var req = new UnityWebRequest(Url("/api/v1/events/claim"), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var resp = JsonUtility.FromJson<EventClaimResponse>(req.downloadHandler.text);
            onOk?.Invoke(resp);
        }

        public IEnumerator GetEventClaims(Action<EventClaimInfo[]> onOk, Action<string> onErr)
        {
            using var req = new UnityWebRequest(Url("/api/v1/events/claims"), "GET");
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var json = req.downloadHandler.text;
            var wrapped = "{\"claims\":" + json + "}";
            var resp = JsonUtility.FromJson<EventClaimHistoryResponse>(wrapped);
            onOk?.Invoke(resp?.claims ?? Array.Empty<EventClaimInfo>());
        }

        // === Bug #2 (r4): daily free spins endpoint ===

        public IEnumerator ClaimDailyReward(Action<DailyRewardResponse> onOk, Action<string> onErr)
        {
            using var req = new UnityWebRequest(Url("/api/v1/spin/daily-reward"), "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + _auth.Token);
            req.timeout = _config.TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke(ErrorText(req));
                yield break;
            }

            var resp = JsonUtility.FromJson<DailyRewardResponse>(req.downloadHandler.text);
            onOk?.Invoke(resp);
        }
    }
}
