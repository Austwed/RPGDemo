using UnityEngine;
namespace BaseConquest.Presentation
{
    // All upright 2D art is transparent and sorted at its ground-contact anchor.
    // A smaller map Z is nearer the viewer; texture height never changes that anchor.
    public static class MapSpriteOrder
    {
        public static int At(float worldZ)=>Mathf.Clamp(Mathf.RoundToInt(-worldZ*25)*4,-30000,30000);
        public static void Apply(Transform root,float worldZ)
        {int order=At(worldZ);foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))renderer.sortingOrder=order;root.GetComponent<PlayerHeroView>()?.SortRig(order);root.GetComponent<CompanionSpriteView>()?.SortRig(order);}
    }
}
