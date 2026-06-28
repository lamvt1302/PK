using UnityEngine;

namespace PK
{
    /// <summary>
    /// Đại diện cho 1 ô xây dựng (building slot) trên đảo làng Pirate Kings.
    /// Mỗi slot có chỉ số (SlotIndex), cấp độ (Level) và trạng thái đã xây (IsBuilt).
    /// Method Upgrade() tăng Level + 1 và bật IsBuilt.
    /// </summary>
    public class PkBuildingSlot : MonoBehaviour
    {
        [Header("Slot Identity")]
        [Tooltip("Chỉ số slot (0..4) khớp với vị trí trên đảo.")]
        public int SlotIndex;

        [Header("Progression")]
        [Tooltip("Cấp độ hiện tại của building (0 = chưa xây).")]
        public int Level;

        [Tooltip("Đã xây xong building ở slot này chưa.")]
        public bool IsBuilt;

        /// <summary>
        /// Nâng cấp building lên 1 cấp. Lần đầu tiên nâng cấp sẽ bật IsBuilt = true.
        /// </summary>
        public void Upgrade()
        {
            Level += 1;
            IsBuilt = true;
            Debug.Log($"[PkBuildingSlot] Slot {SlotIndex} upgraded to Level {Level}.");
        }

        private void OnMouseDown()
        {
            // Click vào slot trong Play mode -> nâng cấp (demo logic, có thể thay bằng event click UI).
            Upgrade();
        }
    }
}