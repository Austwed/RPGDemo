using System.Text.RegularExpressions;
namespace BaseConquest.Battle
{
    public static class UnitDisplayNames
    {
        public const string RecruitmentColor="<color=#8B512F>";
        private static readonly Regex SideWords = new Regex("甲方|乙方|我方|敌方");
        public static string Mark(string name,bool recruited)=>recruited ? RecruitmentColor+Plain(name)+"</color>" : name;
        public static string Plain(string name)=>Regex.Replace(name??"","<[^>]*>","").TrimStart('●',' ');
        public static string ForObserver(string text, BattleSide observer = BattleSide.A) => SideWords.Replace(text ?? "", match =>
            ((match.Value == "我方" || match.Value == "甲方") == (observer == BattleSide.A)) ? "我方" : "敌方");
        public static string RecruitedName(string name)
        {var plain=ForObserver(Plain(name));return (plain.StartsWith("我方")||plain.StartsWith("敌方") ? plain.Substring(2) : plain).TrimStart('●',' ');}
    }
}
