using System;
namespace BaseConquest.Battle
{
    public readonly struct RpgAttackRoll
    {
        public readonly int First,Second,Selected,NetAdvantage;
        public bool Hit {get;}
        public bool Critical=>Selected==10;
        public RpgAttackRoll(int hit,int evasion,int advantage,int disadvantage,int first,int second=0)
        {
            if(hit<0||hit>10||evasion<0||evasion>10||advantage<0||disadvantage<0||first<1||first>10)throw new ArgumentException("D10点数非法");
            NetAdvantage=Math.Sign(advantage-disadvantage);
            if(NetAdvantage!=0&&(second<1||second>10)||NetAdvantage==0&&second!=0)throw new ArgumentException("D10掷骰次数非法");
            First=first;Second=second;Selected=NetAdvantage>0?Math.Max(first,second):NetAdvantage<0?Math.Min(first,second):first;
            Hit=Selected==10||Selected!=1&&Selected+hit>=11+evasion;
        }
    }
    public static class RpgCombatRules
    {
        public static double Interval(double basic,double speed,double minimum)
        {
            double value=basic/(1+speed);
            if(!Finite(basic)||!Finite(speed)||!Finite(minimum)||basic<=0||speed<=-1||minimum<=0||value<minimum)throw new ArgumentException("攻速使前摇低于允许间隔");
            return value;
        }
        public static double Damage(double attack,double defense,double k,double cap,double critical,double received=1)
        {
            if(!Finite(attack)||!Finite(defense)||!Finite(k)||!Finite(cap)||!Finite(critical)||!Finite(received)||attack<0||defense<0||k<=0||cap<0||cap>=1||critical<1||received<0)throw new ArgumentException("RPG伤害参数非法");
            return attack*critical*(1-cap*defense/(defense+k))*received;
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}
