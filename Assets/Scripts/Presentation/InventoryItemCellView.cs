using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Presentation
{
    [DisallowMultipleComponent]
    public sealed class InventoryItemCellView : MonoBehaviour
    {
        [SerializeField] private Image frame;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Text slotLabel;
        [SerializeField] private Text quantity;
        [SerializeField] private Text marker;
        [SerializeField] private Outline selection;
        [SerializeField] private Sprite emptyFrame;
        [SerializeField] private Color emptyTint = Color.white;
        [SerializeField] private Sprite[] qualityFrames;
        [SerializeField] private Color[] qualityTints;

        internal void Bind(Sprite item, bool occupied, int quality, int count, string slotName, string state, bool selected)
        {
            int tier = Mathf.Clamp(quality, 0, qualityFrames.Length - 1);
            frame.sprite = occupied ? qualityFrames[tier] : emptyFrame;
            frame.color = occupied ? qualityTints[tier] : emptyTint;
            itemIcon.sprite = item;
            itemIcon.enabled = item != null;
            slotLabel.text = slotName;
            slotLabel.enabled = !string.IsNullOrEmpty(slotName);
            quantity.text = count > 1 ? "×" + count : "";
            quantity.enabled = count > 1;
            marker.text = state;
            marker.enabled = !string.IsNullOrEmpty(state);
            SetSelected(selected);
        }

        internal void SetSelected(bool selected) => selection.enabled = selected;
    }
}
