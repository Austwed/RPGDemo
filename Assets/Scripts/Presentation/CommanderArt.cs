using UnityEngine;
namespace BaseConquest.Presentation
{
    public sealed class CommanderArt
    {
        public readonly string TextureName;
        // Top-origin source rectangles: standing, upper body, anatomical left/right legs.
        public readonly RectInt[] Clips;
        public readonly Vector2[] Waist;
        public readonly int[] TorsoBottom;
        public readonly RectInt[] WeaponEnds;
        public readonly float PixelScale;
        private CommanderArt(string texture,float scale,RectInt[] clips,Vector2[] waist,int[] bottom,RectInt[] ends=null)
        {TextureName=texture;PixelScale=scale;Clips=clips;Waist=waist;TorsoBottom=bottom;WeaponEnds=ends;}
        public static CommanderArt For(string id)=>id=="ahe" ? Ahe : id=="azhu" ? Azhu : id=="chenan" ? Chenan : id=="wilderness" ? Enemy : null;
        public static Rect UV(RectInt clip,Texture texture)=>new Rect(clip.x/(float)texture.width,1-clip.yMax/(float)texture.height,clip.width/(float)texture.width,clip.height/(float)texture.height);
        private static readonly CommanderArt Ahe=new CommanderArt("PartnerAhe-v3",.0031f,new[]{
            new RectInt(64,20,266,469),new RectInt(366,23,216,466),new RectInt(706,25,210,464),new RectInt(992,21,239,467),
            new RectInt(63,499,267,314),new RectInt(370,501,212,314),new RectInt(707,501,207,315),new RectInt(991,500,245,315),
            new RectInt(151,837,84,173),new RectInt(438,837,90,172),new RectInt(780,837,117,172),new RectInt(1066,837,85,173),
            new RectInt(140,1038,87,178),new RectInt(409,1039,133,175),new RectInt(775,1038,99,177),new RectInt(1078,1038,81,177)},
            new[]{new Vector2(191,795),new Vector2(474,794),new Vector2(814,794),new Vector2(1112,800)},new[]{813,815,816,815});
        private static readonly CommanderArt Azhu=new CommanderArt("PartnerAzhu-v1",.0037f,new[]{
            new RectInt(61,5,219,513),new RectInt(393,7,183,517),new RectInt(675,7,185,518),new RectInt(952,6,212,514),
            new RectInt(60,521,224,325),new RectInt(394,523,188,322),new RectInt(670,523,187,322),new RectInt(951,524,216,325),
            new RectInt(142,856,75,171),new RectInt(429,855,102,171),new RectInt(725,855,96,171),new RectInt(1014,856,77,171),
            // The source's right-leg side views are in reverse column order; keep both feet facing the torso.
            new RectInt(135,1051,85,189),new RectInt(730,1051,91,182),new RectInt(424,1052,97,180),new RectInt(1009,1052,81,181)},
            new[]{new Vector2(181,810),new Vector2(490,810),new Vector2(765,810),new Vector2(1058,810)},new[]{846,845,845,849});
        private static readonly CommanderArt Chenan=new CommanderArt("PartnerChenan-v1",.0033f,new[]{
            new RectInt(32,16,241,473),new RectInt(365,19,194,474),new RectInt(680,18,174,478),new RectInt(970,19,228,471),
            new RectInt(22,504,268,394),new RectInt(387,514,200,333),new RectInt(671,517,155,293),new RectInt(956,514,249,373),
            new RectInt(140,818,71,197),new RectInt(435,825,89,186),new RectInt(713,821,87,193),new RectInt(1032,828,77,187),
            new RectInt(114,1034,73,209),new RectInt(428,1042,98,195),new RectInt(721,1037,93,199),new RectInt(1029,1039,75,204)},
            new[]{new Vector2(165,780),new Vector2(480,780),new Vector2(759,780),new Vector2(1080,785)},new[]{818,825,810,828},
            new[]{new RectInt(22,818,63,80),new RectInt(387,825,47,22),new RectInt(),new RectInt(1164,828,41,59)});
        private static readonly CommanderArt Enemy=new CommanderArt("EnemyCaptain-v1",.0034f,new[]{
            new RectInt(11,2,296,420),new RectInt(375,7,235,415),new RectInt(765,7,242,415),new RectInt(1086,3,285,418),
            new RectInt(5,435,318,356),new RectInt(469,441,160,285),new RectInt(739,441,172,281),new RectInt(1066,435,312,356),
            new RectInt(168,738,79,166),new RectInt(468,738,90,165),new RectInt(821,738,90,163),new RectInt(1134,738,78,165),
            new RectInt(135,921,91,193),new RectInt(465,921,90,191),new RectInt(820,921,93,192),new RectInt(1154,921,86,193)},
            new[]{new Vector2(199,700),new Vector2(531,700),new Vector2(835,700),new Vector2(1194,700)},new[]{738,726,722,738},
            new[]{new RectInt(5,738,113,53),new RectInt(),new RectInt(),new RectInt(1260,738,118,53)});
        public static readonly RectInt[] HorseClips={
            new RectInt(58,329,144,208),new RectInt(266,323,398,232),new RectInt(701,326,399,228),new RectInt(1127,329,142,212),
            new RectInt(98,552,39,142),new RectInt(414,560,100,132),new RectInt(865,555,79,136),new RectInt(1180,559,42,136),
            new RectInt(125,707,38,134),new RectInt(444,713,91,125),new RectInt(834,713,81,126),new RectInt(1174,707,41,135),
            new RectInt(98,854,43,146),new RectInt(443,849,70,151),new RectInt(860,852,62,148),new RectInt(1183,853,46,150),
            new RectInt(111,1012,46,158),new RectInt(438,1012,70,154),new RectInt(858,1012,69,155),new RectInt(1173,1013,49,157)};
        public static readonly Vector2[] HorseLegPivots={
            new Vector2(112,559),new Vector2(488,567),new Vector2(888,562),new Vector2(1202,566),
            new Vector2(147,714),new Vector2(468,720),new Vector2(892,720),new Vector2(1192,714),
            new Vector2(115,861),new Vector2(473,856),new Vector2(892,859),new Vector2(1210,860),
            new Vector2(139,1019),new Vector2(477,1019),new Vector2(888,1019),new Vector2(1197,1020)};
    }
}
