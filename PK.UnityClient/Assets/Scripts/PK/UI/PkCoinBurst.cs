using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PK.UI
{
    /// <summary>
    /// Coin burst effect: spawn nhieu Image "coin" tu origin, bay len random, roi xuong,
    /// fade out. Dung Object Pool pattern (List GameObject pool).
    /// TODO: Prefab setup can tao trong Unity Editor â€” gan CoinSprite vao Inspector.
    /// Dat script len GameObject chua RectTransform (canvas overlay). Pool duoc tao runtime.
    /// </summary>
    public class PkCoinBurst : MonoBehaviour
    {
        [Header("Refs")]
        public Sprite CoinSprite;
        public RectTransform Container;

        [Header("Burst Settings")]
        public int PoolSize = 24;
        public float UpForce = 260f;
        public float Gravity = 600f;
        public float Lifetime = 1.2f;
        public float SpinSpeed = 720f;
        public float CoinSize = 48f;

        private readonly List<GameObject> _pool = new();

        private void Awake()
        {
            if (Container == null) Container = GetComponent<RectTransform>();
            for (int i = 0; i < PoolSize; i++) _pool.Add(CreateCoin());
        }

        /// <summary>Spawn count coin tu origin (local position trong Container).</summary>
        public void Spawn(int count, Vector2 origin)
        {
            for (int i = 0; i < count; i++)
            {
                var coin = Acquire();
                if (coin == null) continue;
                StartCoroutine(AnimateCoin(coin, origin));
            }
        }

        private GameObject CreateCoin()
        {
            var go = new GameObject("Coin");
            go.transform.SetParent(Container, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CoinSize, CoinSize);
            var img = go.AddComponent<Image>();
            if (CoinSprite != null) img.sprite = CoinSprite;
            img.raycastTarget = false;
            go.SetActive(false);
            return go;
        }

        private GameObject Acquire()
        {
            foreach (var c in _pool) if (!c.activeSelf) { c.SetActive(true); return c; }
            // Pool het -> tao them 1.
            var extra = CreateCoin();
            _pool.Add(extra);
            extra.SetActive(true);
            return extra;
        }

        private IEnumerator AnimateCoin(GameObject coin, Vector2 origin)
        {
            var rt = (RectTransform)coin.transform;
            rt.anchoredPosition = origin;
            rt.localRotation = Quaternion.identity;

            // Toc do ban dau: huong len + spread ngau nhien.
            float angle = Random.Range(-60f, 60f) * Mathf.Deg2Rad;
            var vel = new Vector2(Mathf.Sin(angle) * UpForce * 0.6f, Mathf.Cos(angle) * UpForce);
            float t = 0f;
            var rot = coin.transform.eulerAngles;

            while (t < Lifetime)
            {
                t += Time.deltaTime;
                vel.y -= Gravity * Time.deltaTime;
                rt.anchoredPosition += vel * Time.deltaTime;
                rot.z += SpinSpeed * Time.deltaTime;
                rt.localRotation = Quaternion.Euler(rot);

                // Fade out nua sau.
                float alpha = t > Lifetime * 0.6f ? Mathf.Lerp(1f, 0f, (t - Lifetime * 0.6f) / (Lifetime * 0.4f)) : 1f;
                var img = coin.GetComponent<Image>();
                if (img != null)
                {
                    var c = img.color; c.a = alpha; img.color = c;
                }
                yield return null;
            }
            coin.SetActive(false);
        }
    }
}