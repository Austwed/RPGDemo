using UnityEngine;

namespace BaseConquest.Presentation
{
    // Stable binding names survive a designer renaming the object in Hierarchy.
    [DisallowMultipleComponent]
    public sealed class UiVisualNode : MonoBehaviour
    {
        [HideInInspector] public string BindingName;
        [Tooltip("固定界面可调整位置、尺寸和锚点。数据列表由程序排列，默认只读取外观。")]
        public bool UseAuthoredLayout = true;
        [Tooltip("仅用于固定标题等文字；库存、生命、姓名等实时数据请保持关闭。")]
        public bool OverrideStaticText;
        [Tooltip("装饰物不会参与按钮绑定，可以添加图片、边框等子物体。")]
        public bool Decoration;
    }
}
