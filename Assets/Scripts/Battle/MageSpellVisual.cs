using System;
using System.Collections.Generic;
using UnityEngine;

namespace BaseConquest.Battle
{
    [Serializable]
    public struct MageSpellVisual
    {
        public int id;
        public float x, z, radius, opacity;
        public bool impact;

        // The authoritative attack packet supplies the position, time and hit result for both clients.
        public static void Collect(RealtimeCampaignBattle run, BattleSide observer, List<MageSpellVisual> into)
        {
            into.Clear();
            if (run.Session.Opening.Rpg == null) return;
            var board = run.Battle.Board;
            double now = run.Model.Elapsed;
            double speed = run.Session.Opening.Rpg.ArrowSpeed / RealtimePrototype.PrototypeArena.Subdivision;
            double oldest = now - Math.Sqrt(board.Width * board.Width + board.Height * board.Height) / speed - .24;
            var attacks = run.RpgAttackHistory;
            for (int i = attacks.Count - 1; i >= 0; i--)
            {
                var packet = attacks[i];
                if (packet.LaunchedAt < oldest) break;
                if (packet.DamageType != "magic" || packet.TargetId < 0 || now < packet.LaunchedAt) continue;
                double sinceImpact = now - packet.ArriveAt;
                bool landed = sinceImpact >= 0;
                if (landed && (!packet.ImpactApplied || sinceImpact >= .24)) continue;
                double t = Math.Min(1, (now - packet.LaunchedAt) / Math.Max(.0001, packet.ArriveAt - packet.LaunchedAt));
                var pos = landed && packet.HasImpactPosition ? packet.ImpactPosition :
                    packet.Origin + (packet.Destination - packet.Origin) * t;
                if (!run.Battle.Vision.Visible(observer, new GridPosition((int)pos.X, (int)pos.Z))) continue;
                into.Add(new MageSpellVisual
                {
                    id = packet.Id, x = (float)pos.X, z = (float)pos.Z, impact = landed,
                    radius = landed ? .16f + (float)sinceImpact * .9f : .15f,
                    opacity = landed ? 1 - (float)sinceImpact / .24f : 1
                });
            }
        }

        private static Sprite orb, ring;
        public static Sprite Art(bool impact)
        {
            var cached = impact ? ring : orb;
            if (cached != null) return cached;
            const int size = 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = impact ? "MageImpact" : "MageOrb", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * (size / 2f)) / (size / 2f);
                if (d > 1 || impact && d < .55f) continue;
                pixels[y * size + x] = d < .50f || impact && d < .73f ? new Color32(255, 248, 181, 255)
                    : d < .79f ? new Color32(255, 162, 49, 255) : new Color32(199, 57, 44, 213);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            cached = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f, size);
            if (impact) ring = cached; else orb = cached;
            return cached;
        }
    }
}
