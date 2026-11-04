using System;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private int personnelFilter,personnelPage;
        private void PersonnelRoster()
        {
            var s=owner.State;var kinds=new[]{SoldierKind.Hero,SoldierKind.Melee,SoldierKind.Ranged,SoldierKind.Cavalry};
            if(BaseRpg.Active(s)){RowButton("俘虏名册 · "+s.Aftermath.Prisoners.Count(p=>p.Status=="held"),0,800,OpenHeldPrisoners);row+=60;}
            for(int i=0;i<kinds.Length;i++){int index=i;RowButton((personnelFilter==i ? "● " : "")+BattleSimulation.KindName(kinds[i]),-315+i*210,200,()=>{personnelFilter=index;personnelPage=0;OpenBarracks();});}row+=60;
            var units=BasePersonnel.Units(s).Where(u=>u.Kind==kinds[personnelFilter]).ToArray();int pages=Math.Max(1,(units.Length+7)/8);personnelPage=Math.Max(0,Math.Min(pages-1,personnelPage));
            Line("共 "+units.Length+"人；点击单位查看完整属性、工作与粮草／装备效果。",54,18);
            string stamp=BaseMuster.Snapshot(s);
            foreach(var u in units.Skip(personnelPage*8).Take(8))
            {
                string key=u.Key;Label(content,u.Name+" · 生命 "+u.Health.ToString("0.#")+"/"+u.MaxHealth.ToString("0.#")+" · 攻 "+u.Current.Damage.ToString("0.##")+" 防 "+u.Current.Defense.ToString("0.##")+"\n"+u.Facility+" · "+u.State,new Vector2(-80,-row-36),new Vector2(680,72),18,TextAnchor.MiddleLeft,new Vector2(.5f,1));
                RowButton("详细信息",350,130,()=>OpenPersonnelDetail(key,stamp));row+=80;
            }
            RowButton("上一页",-290,240,()=>{personnelPage--;OpenBarracks();},personnelPage>0);RowButton("第 "+(personnelPage+1)+"／"+pages+" 页",0,240,()=>{},false);RowButton("下一页",290,240,()=>{personnelPage++;OpenBarracks();},personnelPage+1<pages);row+=60;
            Line(UnitDisplayNames.Mark("黄色姓名",true)+"：通过劝降加入。仅标记来源，工作、伤情、编队与战斗规则相同。",70,18);
        }
        private void OpenHeldPrisoners()
        {
            var s=owner.State;BeginPanel("保留俘虏 · 待招降");RowButton("返回名册",0,800,()=>{activePage="roster";OpenBarracks();});row+=60;
            Line("队长名额 "+BaseRpg.Population(s)+" / "+BaseRpg.Capacity(s)+"；随行满时送本领地基地，营帐满时进入当地旅店／扎营点（最多3名）；费用或寄存名额不足才保留俘虏。",70,19);
            foreach(var prisoner in s.Aftermath.Prisoners.Where(p=>p.Status=="held").ToArray())
            {
                string id=prisoner.Id;Line(prisoner.Name+" · 生命 "+prisoner.Health.ToString("0.#")+"/"+prisoner.Stats.Health.ToString("0.#"),48,20);
                if(prisoner.Rpg?.Version==1)Line(BaseRpg.Describe(prisoner.Rpg),120,18);
                RowButton("招降",-210,400,()=>RecruitPrisoner(id,OpenHeldPrisoners),prisoner.Kind!=SoldierKind.Hero||(BaseInn.Enabled(s)?BaseInn.CanRecruit(s):BaseRpg.HasRoom(s)));
                RowButton("释放",210,400,()=>{BeginPanel("确认释放 · "+prisoner.Name);Line("释放后离开名单，无法再招降。",64);RowButton("确认释放",-210,400,()=>{owner.Change(next=>{if(!BaseAftermath.Decide(next,id,"release"))throw new InvalidOperationException("俘虏状态已变化");},"俘虏已释放");OpenHeldPrisoners();});RowButton("取消",210,400,OpenHeldPrisoners);row+=60;EndPanel();});row+=60;
            }
            EndPanel();
        }
        private void OpenPersonnelDetail(string key,string stamp,Action back=null)
        {
            var s=owner.State;var u=BaseMuster.Snapshot(s)==stamp ? BasePersonnel.Units(s).FirstOrDefault(u=>u.Key==key) : null;
            BeginPanel("兵营 · 单位详情","personnel:"+key);
            RowButton(back==null?"返回名册":"返回对话",-210,400,back??(()=>{activePage="roster";OpenBarracks();}));
            if(u==null){row+=60;Line("人员安排已变化，请从名册重新选择。",80);EndPanel();return;}
            if(u.IsLeader){RowButton("管理员安排",0,800,()=>OpenPartner(u.LeaderId),!u.Away&&u.Health>0);row+=60;}
            Line(u.Name+" · "+BattleSimulation.KindName(u.Kind),52,24);Line("生命 "+u.Health.ToString("0.#")+" / "+u.MaxHealth.ToString("0.#"),46);
            if(u.IsLeader){bool following=BaseAdventure.Contains(s,u.LeaderId);RowButton(following?"人物与背包":"到基地仓库管理此人背包",0,800,()=>{gearLeader=u.LeaderId;OpenPersonalEquipment();},following);row+=56;}
            Line("当前攻击 "+u.Current.Damage.ToString("0.##")+" · 防御 "+u.Current.Defense.ToString("0.##")+"；基础攻击 "+u.Basic.Damage.ToString("0.##")+" · 防御 "+u.Basic.Defense.ToString("0.##"),62,19);
            var person=u.IsLeader?s.Leaders.Single(leader=>leader.Id==u.LeaderId):null;
            if(person!=null)Line("获取来源："+(string.IsNullOrEmpty(person.AcquisitionSource)?"未记录":person.AcquisitionSource)+" · 游戏日："+(person.AcquisitionDay>0?"第"+person.AcquisitionDay+"天":"未记录"),64,19);
            var profession=u.IsLeader ? u.Current.HasProfessionKind?u.Current.ProfessionKind:u.Current.HasCombatKind?u.Current.CombatKind:BaseCommanderIdentity.Appearance(person)=="ahe"?SoldierKind.Ranged:BaseCommanderIdentity.Appearance(person)=="azhu"?SoldierKind.Cavalry:SoldierKind.Melee : u.Kind;
            double range=u.IsLeader?BasePersonalEquipment.RealtimeRange(person):BaseRpg.Active(s)?s.Demo.Rpg.Range[BaseRpg.Profession(u.Kind)]:RealtimeCombatRules.Range(u.Kind);
            Line("射程 "+range.ToString("0.##")+"细格 · 移速 "+(BaseRpg.Active(s)?person==null?s.Demo.Rpg.Speed[BaseRpg.Profession(profession)]:BaseRpg.MovementSpeed(person):RealtimeCombatRules.Speed(profession))+"细格／秒"+(BaseRpg.Active(s)?" · 自然10暴击":" · 暴击率 "+(u.Current.CritChance*100).ToString("0.#")+"%")+" · 暴击伤害×"+u.Current.CritMultiplier.ToString("0.##"),60,19);
            Line("所在："+u.Facility+" · "+u.State+"\n生产资格："+(u.Effective ? "今日可生产" : "今日不生产")+"；岗位："+(u.Locked ? "手动锁定" : "自动安排"),76,19);
            Line(u.IsLeader ? "管理员增益：粮田 +"+(u.FarmEfficiency*100).ToString("0.#")+"%／工坊 +"+(u.WorkshopEfficiency*100).ToString("0.#")+"%" : "工作效率：粮田×"+u.FarmEfficiency.ToString("0.##")+"／工坊×"+u.WorkshopEfficiency.ToString("0.##"),60,19);
            Line("状态效果\n"+u.Effects.Replace("／","\n"),210,19);
            if(person?.Rpg?.Version==1){Line(BaseRpg.Describe(person.Rpg),140,18);string problem=BaseRpg.DismissProblem(s,person.Id);if(!person.Rpg.Story){Line(problem==""?"请离将全部装备与背包物品退回基地仓库，普通兵保留。":problem,66,18);RowButton("请离队长",0,800,()=>{BeginPanel("确认请离 · "+person.Name);Line("所有物品退回基地仓库，普通兵保留。",65);RowButton("确认请离",-210,400,()=>{owner.Change(next=>{if(!BaseRpg.Dismiss(next,person.Id))throw new InvalidOperationException(BaseRpg.DismissProblem(next,person.Id));},"队长已请离，物品已返仓");activePage="roster";OpenBarracks();});RowButton("取消",210,400,()=>OpenPersonnelDetail(key,stamp,back));row+=60;EndPanel();},problem=="");row+=60;}}
            if(u.Recruited)Line(UnitDisplayNames.Mark("黄色姓名",true)+"表示通过劝降加入；只标记来源。",60,18);
            EndPanel();
        }
    }
}
