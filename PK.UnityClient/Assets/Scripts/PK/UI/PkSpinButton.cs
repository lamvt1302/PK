using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PK.UI
{
    /// <summary>
    /// MonoBehaviour cho nut Spin: xu ly click -> Bootstrap.SpinOnce(), press scale
    /// animation (Coroutine, khong can DOTween), haptic feedback (TODO: implement vibration).
    /// TODO: Prefab setup can tao trong Unity Editor â€” gan Button component + Bootstrap
    /// (PK.PkBootstrap) vao Inspector. Gan script len GameObject chua Button "Spin".
    /// </summary>
    public class PkSpinButton : MonoBehaviour
    {
        [Header("Refs")]
        public Button SpinButton;
        public PK.PkBootstrap Bootstrap;

        [Header("Press Animation")]
        public float PressScale = 0.92f;
        public float PressDuration = 0.08f;
        public float ReleaseDuration = 0.12f;

        private Vector3 _baseScale = Vector3.one;
        private Coroutine _pressRoutine;

        private void Awake()
        {
            if (SpinButton == null) SpinButton = GetComponent<Button>();
            _baseScale = transform.localScale;
            if (SpinButton != null)
            {
                SpinButton.onClick.RemoveAllListeners();
                SpinButton.onClick.AddListener(OnSpinClicked);
            }
        }

        /// <summary>Callback khi nhan nut Spin.</summary>
        public void OnSpinClicked()
        {
            // Haptic feedback (vibration) â€” chua implement, chi log.
            // TODO: goi Handheld.Vibrate() hoac native haptic khi port mobile.
            Debug.Log("[PkSpinButton] Haptic feedback (TODO: implement vibration)");

            if (Bootstrap != null) Bootstrap.SpinOnce();
            else Debug.LogWarning("[PkSpinButton] Bootstrap chua gan â€” khong the spin.");
        }

        /// <summary>Press-down scale animation (goi tu EventTrigger PointerDown).</summary>
        public void OnPressDown()
        {
            if (_pressRoutine != null) StopCoroutine(_pressRoutine);
            _pressRoutine = StartCoroutine(ScaleTo(_baseScale * PressScale, PressDuration));
        }

        /// <summary>Press-release scale animation (goi tu EventTrigger PointerUp).</summary>
        public void OnPressUp()
        {
            if (_pressRoutine != null) StopCoroutine(_pressRoutine);
            _pressRoutine = StartCoroutine(ScaleTo(_baseScale, ReleaseDuration));
        }

        /// <summary>Bat/tat interactable cua Button Spin.</summary>
        public void SetInteractable(bool interactable)
        {
            if (SpinButton != null) SpinButton.interactable = interactable;
        }

        private IEnumerator ScaleTo(Vector3 target, float duration)
        {
            Vector3 start = transform.localScale;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                transform.localScale = Vector3.Lerp(start, target, k);
                yield return null;
            }
            transform.localScale = target;
            _pressRoutine = null;
        }
    }
}