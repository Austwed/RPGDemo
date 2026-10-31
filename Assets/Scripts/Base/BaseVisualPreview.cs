using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private Transform runtimeRoot;
    }
    public sealed partial class BaseHud
    {
        public GameObject VisualRoot=>root.gameObject;
    }
}
