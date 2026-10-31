using UnityEngine;

namespace BaseConquest.Base
{
    /// <summary>
    /// 旧格式导入器维护的兼容经营默认值。
    /// 未声明UseOpeningRules的模板从本资产取值；35号配表开局保留自己的规则。
    /// 旧存档继续使用自身BaseEconomyRules，重导配表不迁移保存进度。
    /// </summary>
    [CreateAssetMenu(menuName = "SLG/据点经营设置", fileName = "BaseSettings")]
    public sealed class BaseSettings : ScriptableObject
    {
        public BaseEconomyRules Rules = new BaseEconomyRules();

        [Tooltip("资源路径（不含扩展名）。资产放在 Assets/Resources/BaseSettings.asset 时才会被运行时自动挂载。")]
        public string ResourceName = "BaseSettings";

        private void OnValidate()
        {
            Rules ??= new BaseEconomyRules();
        }

        /// <summary>
        /// 场景加载前把资产里的经营规则注入 BaseScenarioDocument.DefaultRules。
        /// 资产缺失时静默跳过，模板自带 Rules 继续生效。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallDefaultRules()
        {
            BaseSettings settings = Resources.Load<BaseSettings>("BaseSettings");
            if (settings != null && settings.Rules != null) BaseScenarioDocument.DefaultRules = settings.Rules;
        }
    }
}
