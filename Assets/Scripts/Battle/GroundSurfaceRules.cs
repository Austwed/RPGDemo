namespace BaseConquest.Battle
{
    // Appearance only; these surfaces never change movement cost or combat values.
    public static class GroundSurfaceRules
    {
        public static GroundSurface Resolve(GroundSurface surface,string region,int x,int z)
        {
            if(surface!=GroundSurface.Default)return surface;
            if(region=="world")return GroundSurface.Default;
            if(region=="base"&&(x==12||z==6)||region=="town"&&z==6)return GroundSurface.Road;
            return region=="battle" ? GroundSurface.Sand : GroundSurface.Grass;
        }
    }
}
