using System;
using System.Collections.Generic;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyVillagerWalk
    {
        public static void Run()
        {
            var source = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                foreach (string appearance in new[] { "villager", "villager-girl", "chunshen", "laozhou" })
                {
                    VerifyWalk(source, appearance, Vector3.right, HeroFacing.East);
                    VerifyWalk(source, appearance, Vector3.left, HeroFacing.West);
                    VerifyWalk(source, appearance, Vector3.forward, HeroFacing.North);
                    VerifyWalk(source, appearance, Vector3.back, HeroFacing.South);
                }
                Debug.Log("[VerifyVillagerWalk] PASS four civilian atlases, four directions, alternate foot contact during a 0.75-cell walk; idle on stopping");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        private static void VerifyWalk(Material source, string appearance, Vector3 direction, HeroFacing facing)
        {
            var actor = new GameObject(appearance);
            try
            {
                var sprite = actor.AddComponent<CompanionSpriteView>();
                if (!sprite.Initialize(appearance, source)) throw new InvalidOperationException("Missing civilian art: " + appearance);
                var mesh = actor.GetComponentInChildren<MeshFilter>().sharedMesh;
                var renderer = actor.GetComponentInChildren<Renderer>();
                var ink = new MaterialPropertyBlock();
                var columns = new HashSet<int>();
                var walk = PixelDemoArt.Motion(appearance, "walk");
                if (walk == null) throw new InvalidOperationException("Missing civilian walk atlas: " + appearance);
                int count = walk.width / (walk.height / 4);
                var origin = Vector3.zero;
                sprite.Exploration(origin);
                // The shortest ambient path covers 0.75 cells at 1.5 cells/sec.
                const int frames = 30;
                var end = direction * (.75f * BattleBoardView.CellSize);
                for (int frame = 1; frame <= frames; frame++)
                {
                    sprite.Exploration(Vector3.Lerp(origin, end, frame / (float)frames));
                    renderer.GetPropertyBlock(ink);
                    if (ink.GetTexture("_BaseMap") != walk || sprite.Facing != facing)
                        throw new InvalidOperationException("Wrong walk atlas or direction: " + appearance + " / " + facing);
                    columns.Add(Mathf.RoundToInt(mesh.uv[0].x * count));
                }
                // The generic villager, Chunshen and Laozhou hold each drawn pose for two columns.
                // A single short walk has to progress from contact through passing to alternate contact.
                if (!columns.Contains(0) || !columns.Contains(2) || !columns.Contains(4))
                    throw new InvalidOperationException("Short walk misses step phases: " + appearance + " / " + facing);
                sprite.Exploration(end);
                renderer.GetPropertyBlock(ink);
                var idle = PixelDemoArt.Motion(appearance, "idle") ?? PixelDemoArt.Load("Characters", appearance);
                if (ink.GetTexture("_BaseMap") != idle)
                    throw new InvalidOperationException("Stopped civilian did not return to standing: " + appearance);
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }
    }
}
