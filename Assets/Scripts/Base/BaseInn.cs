using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    // Legacy save fields. Demo has no inn, campsite check-in or recurring room fees.
    [Serializable] public sealed class BaseInnGuest
    {
        public string LeaderId,AreaId;public int PaidDay,Debt;public bool Free,WasCompanion;
        public BaseInnGuest Copy()=>(BaseInnGuest)MemberwiseClone();
    }
    [Serializable] public sealed class BaseInnState
    {
        public List<BaseInnGuest> Guests=new List<BaseInnGuest>();
        public BaseInnState Copy()=>new BaseInnState{Guests=Guests.Select(g=>g.Copy()).ToList()};
    }
    public static class BaseInn
    {
        public static void ValidateRetired(BaseState s)
        {
            s.Inn??=new BaseInnState();
            s.Inn.Guests??=new List<BaseInnGuest>();
            if(s.Inn.Guests.Count!=0)throw new ArgumentException("存档含已退役的住宿记录");
        }
    }
}
