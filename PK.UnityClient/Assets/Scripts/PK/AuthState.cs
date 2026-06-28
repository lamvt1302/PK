using UnityEngine;

namespace PK
{
    public class AuthState
    {
        private const string TokenKey = "pk_token";

        public string Token
        {
            get => PlayerPrefs.GetString(TokenKey, "");
            set
            {
                PlayerPrefs.SetString(TokenKey, value ?? "");
                PlayerPrefs.Save();
            }
        }

        public bool HasToken => !string.IsNullOrWhiteSpace(Token);
    }
}

