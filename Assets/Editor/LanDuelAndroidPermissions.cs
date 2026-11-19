#if UNITY_ANDROID
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;

namespace BaseConquest.Editor
{
    // Needed to obtain the active Wi-Fi gateway when the Android route table is app-specific.
    public sealed class LanDuelAndroidPermissions : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var doc = XDocument.Load(manifest);
            var android = XNamespace.Get("http://schemas.android.com/apk/res/android");
            const string permission = "android.permission.ACCESS_NETWORK_STATE";
            if (doc.Root.Elements("uses-permission").Any(e => (string)e.Attribute(android + "name") == permission)) return;
            doc.Root.AddFirst(new XElement("uses-permission", new XAttribute(android + "name", permission)));
            doc.Save(manifest);
        }
    }
}
#endif
