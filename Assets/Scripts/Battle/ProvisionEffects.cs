using System;
using System.Collections.Generic;
namespace BaseConquest.Battle
{
    public static class ProvisionEffects
    {
        public static string Summary(SoldierKind kind,double factor,double weapon,double armor,bool equipmentPrepared)
        {
            var labels=new List<string> {factor<.999999 ? "缺粮：攻击×"+factor.ToString("0.###")+"（降低"+((1-factor)*100).ToString("0.#")+"%）" : "粮草充足：攻击×1"};
            if(kind==SoldierKind.Hero)labels.Add("队长不使用普通兵军需");
            else if(!equipmentPrepared)labels.Add("军需未准备：无装备加成");
            else {labels.Add((kind==SoldierKind.Ranged ? "弓" : "剑")+"：攻击+"+weapon.ToString("0.##"));labels.Add("盔甲：防御+"+armor.ToString("0.##"));}
            return string.Join("／",labels);
        }
    }
}
