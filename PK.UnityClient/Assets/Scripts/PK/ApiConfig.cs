using UnityEngine;

namespace PK
{
    [CreateAssetMenu(menuName = "PK/ApiConfig", fileName = "ApiConfig")]
    public class ApiConfig : ScriptableObject
    {
        public string BaseUrl = "http://localhost:5000";
        public int TimeoutSeconds = 8;
    }
}
