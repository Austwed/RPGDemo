using System;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyStoryBoardDimensions
    {
        private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        public static void Run()
        {
            foreach(var size in new[]{new[]{10,10},new[]{24,14},new[]{32,24},new[]{64,64},new[]{10,64},new[]{64,10}})
            {
                int width=size[0],height=size[1];var board=BattleBoard.CreateDefault(width,height);
                Need(board.Width==width&&board.Height==height&&board.Cells.Length==width*height,"创建棋盘丢失尺寸");
                var last=new GridPosition(width-1,height-1);board.Cell(last).Elevation=2;
                var clone=board.Clone();Need(clone.Width==width&&clone.Height==height&&clone.Cell(last).Elevation==2,"复制棋盘丢失边界数据");
                RealtimeCampaignBattle.ValidateBoard(clone);
                var arena=new PrototypeArena(new bool[width*height],new bool[width*height],width:width,height:height);
                var point=new PrototypePoint(width-.75,height-.75);var cell=arena.CellAt(point);
                Need(arena.Columns==width&&arena.Rows==height&&arena.FineWalkable(cell)&&arena.Center(cell).X>width-1&&arena.Center(cell).Z>height-1,"即时战场边界不可用");
            }
            bool rejected=false;try{RealtimeCampaignBattle.ValidateBoard(new BattleBoard{Width=65,Height=10,Cells=new BoardCell[650]});}catch(ArgumentException){rejected=true;}
            Need(rejected,"即时战场未拒绝超过64格");
            Debug.Log("VerifyStoryBoardDimensions PASS");
        }
        public static void Batch()
        {
            try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
