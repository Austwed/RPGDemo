using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BaseConquest.Battle
{
    // Rich-text colors and plain-text hit geometry share the same font and wrapping.
    public sealed class BattleLogLinks : MonoBehaviour, IPointerClickHandler
    {
        public BattleSceneController Owner;
        public struct Link { public int Start, Length, UnitId; }
        private readonly List<Link> links = new List<Link>();
        private string plain = "";
        private readonly TextGenerator geometry = new TextGenerator();
        public void SetEntries(BattleSimulation battle, IEnumerable<BattleEvent> entries)
        {
            links.Clear(); var text = new StringBuilder();
            foreach (var entry in entries)
            {
                if (text.Length > 0) text.Append('\n');
                int start = text.Length; string row = "[" + entry.Round + "] " + BattleMobileHud.RawEvent(battle, entry); text.Append(row);
                var ids = new[] { entry.SourceId, entry.TargetId }.Concat(entry.NamedIds ?? new int[0]).ToArray();
                var names = new[] { entry.SourceName, entry.TargetName }.Concat(entry.NamedNames ?? new string[0]).ToArray();
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] < 0) continue; string name = i < names.Length ? names[i] : null;
                    if (string.IsNullOrEmpty(name)) name = battle.UnitName(battle.Soldier(ids[i]));
                    bool ambiguous = names.Where((other, index) => index != i && index < ids.Length && ids[index] != ids[i]).Any(other => other == name);
                    for (int offset = row.IndexOf(name, System.StringComparison.Ordinal); offset >= 0; offset = row.IndexOf(name, offset + name.Length, System.StringComparison.Ordinal))
                    {
                        if (links.Any(x => x.Start == start + offset)) continue;
                        links.Add(new Link { Start = start + offset, Length = name.Length, UnitId = ids[i] });
                        if (ambiguous) break;
                    }
                }
            }
            plain = text.ToString();
        }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner == null || eventData.dragging || Vector2.Distance(eventData.position, eventData.pressPosition) > 10 * Owner.HudScale) return;
            var label = GetComponent<Text>(); var rect = label.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out var local)) return;
            var settings = label.GetGenerationSettings(rect.rect.size); settings.richText = false;
            geometry.Populate(plain, settings); var characters = geometry.characters; var lines = geometry.lines;
            local *= label.pixelsPerUnit;
            for (int line = 0; line < lines.Count; line++)
            {
                float top = lines[line].topY; if (local.y > top || local.y < top - lines[line].height) continue;
                int begin = lines[line].startCharIdx, end = line + 1 < lines.Count ? lines[line + 1].startCharIdx : characters.Count;
                for (int i = begin; i < end && i < characters.Count; i++)
                {
                    var character = characters[i]; if (local.x < character.cursorPos.x || local.x > character.cursorPos.x + character.charWidth) continue;
                    foreach (var link in links) if (i >= link.Start && i < link.Start + link.Length) { Owner.FocusUnit(link.UnitId); return; }
                }
            }
        }
    }
}
