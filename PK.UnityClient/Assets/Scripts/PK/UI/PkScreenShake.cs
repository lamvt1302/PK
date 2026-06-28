using System.Collections;
using UnityEngine;

namespace PK.UI
{
    /// <summary>
    /// Screen shake effect: random offset trong duration, decay intensity, lerp ve vi tri
    /// goc khi ket thuc. Dat len RectTransform cua canvas can shake.
    /// TODO: Prefab setup can tao trong Unity Editor â€” gan RectTransform (canvas shake root)
    /// vao Inspector. Khong nen shake toan bo Canvas root de khong anh huong UI input.
    /// </summary>
    public class PkScreenShake : MonoBehaviour
    {
        [Header("Refs")]
        public RectTransform ShakeTarget;

        [Header("Defaults")]
        public float DefaultIntensity = 12f;
        public float DefaultDuration = 0.35f;
        public float Frequency = 0.03f; // khoang thoi gian moi lan random offset

        private Vector2 _origin;
        private Coroutine _shakeRoutine;

        private void Awake()
        {
            if (ShakeTarget == null) ShakeTarget = GetComponent<RectTransform>();
            if (ShakeTarget != null) _origin = ShakeTarget.anchoredPosition;
        }

        /// <summary>Shake voi intensity & duration mac dinh.</summary>
        public void Shake()
        {
            Shake(DefaultIntensity, DefaultDuration);
        }

        /// <summary>Shake voi intensity va duration tuy chon.</summary>
        public void Shake(float intensity, float duration)
        {
            if (ShakeTarget == null) return;
            if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
            _shakeRoutine = StartCoroutine(RunShake(intensity, duration));
        }

        private IEnumerator RunShake(float intensity, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Frequency;
                float decay = 1f - Mathf.Clamp01(t / duration);
                float mag = intensity * decay;
                var offset = new Vector2(
                    Random.Range(-mag, mag),
                    Random.Range(-mag, mag));
                ShakeTarget.anchoredPosition = _origin + offset;
                yield return new WaitForSeconds(Frequency);
            }
            // Lerp ve vi tri goc.
            float k = 0f;
            var from = ShakeTarget.anchoredPosition;
            while (k < 0.1f)
            {
                k += Time.deltaTime;
                ShakeTarget.anchoredPosition = Vector2.Lerp(from, _origin, Mathf.Clamp01(k / 0.1f));
                yield return null;
            }
            ShakeTarget.anchoredPosition = _origin;
            _shakeRoutine = null;
        }
    }
}