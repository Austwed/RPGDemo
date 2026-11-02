using BaseConquest.Base;
using UnityEngine;

namespace BaseConquest.Presentation
{
    public static class DemoPortraits
    {
        public enum Source { Portrait, CharacterSheet, LegacyCompanion, LegacyHero, Missing }
        public readonly struct Result
        {
            public readonly Texture2D Texture;public readonly Rect Uv;public readonly Source Origin;public readonly string Id;
            public Result(Texture2D texture,Rect uv,Source origin,string id){Texture=texture;Uv=uv;Origin=origin;Id=id;}
        }
        public static Result For(string id,bool playerHero=false)
        {
            id=playerHero&&string.IsNullOrEmpty(id)?"player":id??"";
            id=BaseDemoNpcs.ResolveNpcArt(id,true);
            var portrait=PixelDemoArt.Load("Portraits","portrait-"+id);
            if(portrait!=null)return new Result(portrait,new Rect(0,0,1,1),Source.Portrait,id);
            if(!playerHero)
            {
                var character=PixelDemoArt.Load("Characters",id);
                if(character!=null)return new Result(character,PixelDemoArt.Frame(HeroFacing.South,HeroPose.Idle,0),Source.CharacterSheet,id);
                var art=CommanderArt.For(id);var legacy=art==null?null:Resources.Load<Texture2D>("Characters/"+art.TextureName);
                if(legacy!=null)return new Result(legacy,CommanderArt.UV(art.Clips[0],legacy),Source.LegacyCompanion,id);
            }
            else
            {
                var hero=Resources.Load<Texture2D>("Characters/PlayerHero-v1");if(hero!=null)return new Result(hero,new Rect(0,7/8f,1/8f,1/8f),Source.LegacyHero,id);
            }
            return new Result(null,default,Source.Missing,id);
        }
    }
}
