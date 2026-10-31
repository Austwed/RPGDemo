using System;
using UnityEngine;
namespace BaseConquest.Battle
{
    public static class RuntimeDeveloperOptions
    {
        private const string Key="BaseConquest.DeveloperMode";
        public static event Action<bool> Changed;
        public static bool Enabled
        {
            get=>PlayerPrefs.GetInt(Key,0)!=0;
            set {PlayerPrefs.SetInt(Key,value ? 1 : 0);PlayerPrefs.Save();Changed?.Invoke(value);}
        }
    }
}
