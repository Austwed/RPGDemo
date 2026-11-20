using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    public static partial class UiSkinScreenshot
    {
        private const int RecordingRate=30;
        private const BindingFlags InstanceMethods=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly List<float> recordedSteps=new List<float>();
        private static readonly List<string> recordedCues=new List<string>();
        private static readonly MethodInfo stepMotion=typeof(BaseSceneController).GetMethod("MoveExploration",InstanceMethods);
        private static Func<double,double,GroundSurface> footSurface;
        private static Array cueSounds;
        private static FieldInfo nextClip;
        private static int[] previousClips;
        private static readonly FieldInfo effectsGain=typeof(GameAudioVolume).GetField("effects",BindingFlags.Static|BindingFlags.NonPublic);
        private static object priorGain;
        private static Vector2[] stepPath;
        private static int waypoint,stepDirection;
        private static int captureFrames,lastCaptureFrame,channels,originalRate;
        private static bool recording,mixedSteps;

        public static void RunFootstepRecordings()
        {
            stage="footstep-boot";
            Run();
        }
        private static void TickFootstepRecording()
        {
            controller=C;
            if(controller==null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>()!=null)return;
            if(stage=="footstep-boot")
            {
                controller.LoadDemoStage("camp","male");
                stage="footstep-ready";
                return;
            }
            if(stage=="footstep-ready")
            {
                if(controller.State?.Campaign.Location!="base" || BaseBattleGateway.MainMenu)return;
                H.ClosePanel();
                var audio=Object.FindAnyObjectByType<VillageOpeningAudio>();
                if(audio==null || Field(audio,"place").ToString()!="Field")return;
                var effects=(AudioSource)Field(audio,"effects");
                // Keep the actual effects source and listener. Silence music and ambience only in this isolated run.
                foreach(var source in Object.FindObjectsByType<AudioSource>())source.mute=source!=effects;
                AudioListener.pause=false;
                priorGain=effectsGain.GetValue(null);
                effectsGain.SetValue(null,1f);
                cueSounds=(Array)Field(audio,"sounds");
                nextClip=cueSounds.GetValue(0).GetType().GetField("Next",InstanceMethods);
                previousClips=new int[3];
                var view=Field(controller,"regionView");
                footSurface=(Func<double,double,GroundSurface>)view.GetType().GetMethod("FootstepSurfaceAt",InstanceMethods)
                    .CreateDelegate(typeof(Func<double,double,GroundSurface>),view);
                originalRate=Time.captureFramerate;
                Time.captureFramerate=RecordingRate;
                channels=AudioSettings.GetConfiguration().speakerMode switch
                {
                    AudioSpeakerMode.Mono=>1,AudioSpeakerMode.Stereo=>2,AudioSpeakerMode.Quad=>4,
                    AudioSpeakerMode.Surround=>5,AudioSpeakerMode.Mode5point1=>6,AudioSpeakerMode.Mode7point1=>8,
                    _=>throw new InvalidOperationException("无法确定录音输出声道")
                };
                if(!AudioRenderer.Start())throw new InvalidOperationException("Unity 无法开始录制混音输出");
                recording=true;
                BeginFootstepTake(false);
                stage="footstep-record";
                return;
            }
            if(lastCaptureFrame==Time.frameCount)return;
            lastCaptureFrame=Time.frameCount;
            const int movingFrames=RecordingRate*5;
            if(captureFrames<movingFrames)
            {
                Vector2 position=new Vector2((float)controller.State.PlayerX,(float)controller.State.PlayerY);
                var target=stepPath[waypoint];
                if(Vector2.Distance(position,target)<.16f)
                {
                    int firstTurnBack=mixedSteps?stepPath.Length-2:0;
                    if(waypoint==stepPath.Length-1 || waypoint==firstTurnBack && stepDirection<0)
                        stepDirection=-stepDirection;
                    waypoint+=stepDirection;
                    target=stepPath[waypoint];
                }
                stepMotion.Invoke(controller,new object[]{(target-position).normalized,1d/RecordingRate});
                TrackFootstepCue();
            }
            int samples=AudioRenderer.GetSampleCountForCaptureFrame();
            if(samples<=0)return;
            using(var frame=new NativeArray<float>(samples*channels,Allocator.Temp))
            {
                if(!AudioRenderer.Render(frame))throw new InvalidOperationException("Unity 音频渲染失败");
                recordedSteps.AddRange(frame.ToArray());
            }
            if(++captureFrames<movingFrames+RecordingRate/2)return;
            WriteFootstepTake();
            if(!mixedSteps){BeginFootstepTake(true);return;}
            Finish(0,"完成石板连续行走和石板走向草地的 Unity 混音实录");
        }
        private static void StopFootstepCapture()
        {
            if(!recording)return;
            recording=false;
            AudioRenderer.Stop();
            effectsGain.SetValue(null,priorGain);
            Time.captureFramerate=originalRate;
        }
        private static void BeginFootstepTake(bool mixed)
        {
            mixedSteps=mixed;captureFrames=0;recordedSteps.Clear();recordedCues.Clear();
            stepPath=FindFootstepPath(mixed);
            waypoint=1;stepDirection=1;
            if(!controller.Change(s=>{s.PlayerX=stepPath[0].x;s.PlayerY=stepPath[0].y;},""))throw new InvalidOperationException(controller.Message);
            for(int i=0;i<3;i++)previousClips[i]=(int)nextClip.GetValue(cueSounds.GetValue(i));
            Log.Add("灰岩营地 "+string.Join(" → ",stepPath.Select(p=>p.ToString()))+" "+(mixed?"石板到草地":"连续石板"));
        }
        private static Vector2[] FindFootstepPath(bool mixed)
        {
            var state=controller.State;
            var board=BaseBattlefield.Board(state);
            var geometry=BaseSpatial.Objects(state);
            var directions=new[]{Vector2.right,Vector2.up,Vector2.left,Vector2.down};
            bool Walkable(Vector2 p)=>p.x>=1&&p.y>=1&&p.x<board.Width-1&&p.y<board.Height-1&&
                BaseSpatial.Geometry(geometry,p.x,p.y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,p.x,p.y,geometry);
            foreach(var cell in board.Positions())
            {
                var start=new Vector2(cell.X+.5f,cell.Z+.5f);
                if(footSurface(start.x,start.y)!=GroundSurface.Cobble||!Walkable(start))continue;
                foreach(var direction in directions)
                {
                    for(int d=1;d<=40;d++)
                    {
                        var bend=start+direction*(d*.125f);
                        if(!Walkable(bend)||footSurface(bend.x,bend.y)!=GroundSurface.Cobble)break;
                        if(d<20)continue;
                        if(!mixed)return new[]{start,bend};
                        foreach(var crossing in directions)
                        {
                            if(crossing==-direction)continue;
                            int grass=0;
                            for(int e=1;e<=36;e++)
                            {
                                var end=bend+crossing*(e*.125f);
                                if(!Walkable(end))break;
                                var surface=footSurface(end.x,end.y);
                                if(surface==GroundSurface.Grass)grass++;
                                else if(surface!=GroundSurface.Cobble||grass>0)break;
                                if(grass>=20)return new[]{start,bend,end-crossing*2,end};
                            }
                        }
                    }
                }
            }
            throw new InvalidOperationException("营地没有找到连续可走的"+(mixed?"石板到草地":"石板")+"路段");
        }
        private static void TrackFootstepCue()
        {
            for(int i=0;i<3;i++)
            {
                int next=(int)nextClip.GetValue(cueSounds.GetValue(i));
                if(previousClips[i]==next)continue;
                previousClips[i]=next;
                var s=controller.State;
                var surface=footSurface(s.PlayerX,s.PlayerY);
                if(i==0 && surface!=GroundSurface.Grass || i==1 && surface!=GroundSurface.Cobble || i==2)
                    throw new InvalidOperationException("脚底 "+surface+" 与实际触发的脚步 "+i+" 不同");
                recordedCues.Add(i==0?"Grass":"Stone");
            }
        }
        private static void WriteFootstepTake()
        {
            int firstGrass=recordedCues.IndexOf("Grass");
            bool crossedOnce=firstGrass>=2 && recordedCues.Count-firstGrass>=2 &&
                recordedCues.Skip(firstGrass).All(cue=>cue=="Grass");
            if(recordedCues.Count<4 || recordedCues[0]!="Stone" || (mixedSteps?!crossedOnce:firstGrass>=0))
                throw new InvalidOperationException("脚步序列不符："+string.Join(",",recordedCues));
            float peak=recordedSteps.Max(v=>Math.Abs(v));
            double rms=Math.Sqrt(recordedSteps.Average(v=>(double)v*v));
            if(peak<.002 || rms<.0001 || peak>1.01)throw new InvalidOperationException($"录音电平异常：peak={peak} rms={rms}");
            string path=Path.Combine("Logs",mixedSteps?"footsteps-stone-to-grass.wav":"footsteps-continuous-stone.wav");
            using(var wav=new BinaryWriter(File.Create(path)))
            {
                int rate=AudioSettings.outputSampleRate;
                int size=recordedSteps.Count*2;
                void Four(string id)=>wav.Write(Encoding.ASCII.GetBytes(id));
                Four("RIFF");wav.Write(36+size);Four("WAVE");Four("fmt ");wav.Write(16);wav.Write((short)1);
                wav.Write((short)channels);wav.Write(rate);wav.Write(rate*channels*2);wav.Write((short)(channels*2));
                wav.Write((short)16);Four("data");wav.Write(size);
                foreach(float value in recordedSteps)wav.Write((short)Mathf.RoundToInt(Mathf.Clamp(value,-1,1)*32767));
            }
            Log.Add($"{path} · {AudioSettings.outputSampleRate}Hz × {channels} · peak {peak:F4} · RMS {rms:F5} · "+string.Join(" ",recordedCues));
        }
    }
}
