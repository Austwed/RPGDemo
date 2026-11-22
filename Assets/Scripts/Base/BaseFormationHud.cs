using System;
using System.Linq;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private int compositionSlot;
        private static string TroopsText(BaseCommanderTroops p)=>"近战 "+p.Melee+"／弓兵 "+p.Ranged+"／骑兵 "+p.Cavalry+"／法师 "+p.Mage;
        private void CommanderPresets()
        {
            var s = owner.State;
            Line("位次范围：驻守 ≤ 威胁 ≤ 进攻，填入 1～32 的整数",48,20);
            Line("              驻守                     威胁                     进攻",40,18);
            var fields=new UnityEngine.UI.InputField[9];
            for(int slot=0;slot<3;slot++)
            {
                Label(content,((char)('A'+slot))+" 位",new Vector2(-350,-row-26),new Vector2(100,48),20,TextAnchor.MiddleLeft,new Vector2(.5f,1));
                for(int kind=0;kind<3;kind++)
                {
                    int index=slot*3+kind;var box=Box("RangeInput",content,new Vector2(.5f,1),new Vector2(-160+kind*250,-row-26),new Vector2(220,46),buttonColor);
                    var label=Label(box,s.Muster.Ranges[index].ToString(),Vector2.zero,new Vector2(200,40),20,TextAnchor.MiddleCenter);label.raycastTarget=true;
                    var input=box.gameObject.AddComponent<UnityEngine.UI.InputField>();input.targetGraphic=box.GetComponent<UnityEngine.UI.Image>();input.textComponent=label;input.contentType=UnityEngine.UI.InputField.ContentType.IntegerNumber;input.text=s.Muster.Ranges[index].ToString();fields[index]=input;
                }
                row+=56;
            }
            foreach (var field in fields)
                field.onEndEdit.AddListener(_ =>
                {
                    var ranges = fields.Select(input => int.TryParse(input.text, out int value) ? value : 0).ToArray();
                    if (ranges.SequenceEqual(owner.State.Muster.Ranges)) return;
                    if (!owner.Change(next => BaseMuster.SetRanges(next, ranges), ""))
                        for (int i = 0; i < fields.Length; i++)
                            fields[i].SetTextWithoutNotify(owner.State.Muster.Ranges[i].ToString());
                });
            for (int slot = 0; slot < 3; slot++)
            {
                int seat = slot;
                RowButton($"{(slot == compositionSlot ? "● " : "")}{(char)('A' + slot)} 位 · {TroopsText(s.Muster.SlotTroops[slot])}", 0, 800, () =>
                {
                    compositionSlot = seat;
                    OpenBarracks();
                });
                row += 54;
            }

            foreach (BaseTroopKind kind in BaseTown.TroopKinds(s))
            {
                ArmyCounter(BaseEconomy.TroopName(kind), s.Muster.SlotTroops[compositionSlot].Count(kind), 0, 30, value =>
                {
                    owner.Change(next =>
                    {
                        var troops = next.Muster.SlotTroops.Select(group => group.Copy()).ToArray();
                        troops[compositionSlot].Set(kind, value);
                        BaseMuster.SetSlotTroops(next, troops);
                    }, "");
                    OpenBarracks();
                });
            }
        }
        public void OpenDeparture(string location, string eventId = null, string mode = "full", bool adviceHandled = false)
        {
            interactionObject = "";
            var state = owner.State;
            bool demoDefense = BaseDemoDefense.Enabled(state) && location == "perimeter" &&
                (eventId == null || eventId == BaseDemoDefense.EventId || eventId == BaseDemoDefense.SecondEventId);
            if (demoDefense)
            {
                if (BaseDemoDefense.AnyDue(state) && BaseInteraction.At(state, "perimeter", "demo_defense_start"))
                    OpenBattlePreparation(BaseDemoDefense.SecondDue(state) ? BasePrepRoute.Second : BasePrepRoute.Defense);
                else owner.ReportMessage(BaseDemoDefense.Objective(state));
                return;
            }
            if (!adviceHandled && CompanionAdvice(location, eventId, mode)) return;
            OpenDepartureReview(location, eventId, mode);
        }
    }
}
