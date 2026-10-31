using System.Text.RegularExpressions;
namespace BaseConquest.Battle
{
    public static class UnitDisplayNames
    {
        public const string RecruitmentColor="<color=#FFD84D>";
        public static string Mark(string name,bool recruited)=>recruited ? RecruitmentColor+Plain(name)+"</color>" : name;
        public static string Plain(string name)=>Regex.Replace(name??"","<[^>]*>","").TrimStart('●',' ');
        public static string RecruitedName(string name)
        {var plain=Plain(name);return (plain.StartsWith("甲方")||plain.StartsWith("乙方") ? plain.Substring(2) : plain).TrimStart('●',' ');}
    }
}
