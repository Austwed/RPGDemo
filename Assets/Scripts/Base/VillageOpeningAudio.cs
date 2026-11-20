using System;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed class VillageOpeningAudio : MonoBehaviour
    {
        internal enum Cue { Grass, Stone, Dirt, Page, Assignment, Sword, Bow, WoodHit, Hit, Grunt, Hoof, Trade, Recruit, Reward, Forge }
        internal enum Place { Silent, Title, Village, Field, Dungeon, Practice, Battle }

        private const string Root = "Audio/VillageOpening/";
        private const double Stride = 1.65;

        private sealed class Sound
        {
            public readonly AudioClip[] Clips;
            public readonly float Volume, Interval;
            public int Next;
            public float ReadyAt;

            public Sound(Cue cue, float volume, float interval)
            {
                Clips = Resources.LoadAll<AudioClip>(Root + cue);
                Volume = volume;
                Interval = interval;
            }
        }

        private AudioSource ambience, score, ruinsScore, battleScore, effects, resultMusic;
        private AudioClip victory, defeat;
        private Sound[] sounds;
        private readonly System.Random voiceRandom = new System.Random();
        private static VillageOpeningAudio instance;
        private BaseSceneController owner;
        private RealtimeCampaignBattle encounter;
        private Place place;
        private bool resultHeard, suspended, hasAttackRelease;
        private double distance;
        private float ambienceGain, scoreGain, ruinsGain, battleGain, duckUntil;

        internal static VillageOpeningAudio Shared()
        {
            if (instance != null) return instance;
            // A script reload can reset static fields while the persistent host is still playing.
            instance = FindAnyObjectByType<VillageOpeningAudio>();
            if (instance != null) return instance;
            var host = new GameObject("ExplorationAudio");
            DontDestroyOnLoad(host);
            return instance = host.AddComponent<VillageOpeningAudio>();
        }

        public static void PlayTitle() => Shared().EnterTitle();

        private void EnterTitle()
        {
            if (place == Place.Title) return;
            owner = null;
            UnbindBattle();
            // The title shares the surface exploration loop. Leave its cursor alone.
            // Stop the previous scene's voices before the title mix can play.
            ruinsScore.Stop();
            battleScore.Stop();
            resultMusic.Stop();
            ruinsScore.volume = ruinsGain = 0;
            battleScore.volume = battleGain = 0;
            duckUntil = 0;
            SetPlace(Place.Title);
        }

        private void Awake()
        {
            ambience = MakeSource("Ambience");
            score = MakeSource("Exploration");
            ruinsScore = MakeSource("RuinsScore");
            battleScore = MakeSource("BattleScore");
            effects = MakeSource();
            resultMusic = MakeSource();
            victory = Resources.Load<AudioClip>(Root + "Victory");
            defeat = Resources.Load<AudioClip>(Root + "Defeat");
            sounds = new[]
            {
                new Sound(Cue.Grass, .30f, .14f), new Sound(Cue.Stone, .39f, .14f), new Sound(Cue.Dirt, .31f, .14f),
                new Sound(Cue.Page, .37f, .09f), new Sound(Cue.Assignment, .53f, .16f),
                new Sound(Cue.Sword, .38f, .12f), new Sound(Cue.Bow, .40f, .12f),
                new Sound(Cue.WoodHit, .49f, .13f), new Sound(Cue.Hit, .47f, .13f),
                new Sound(Cue.Grunt, .62f, .12f), new Sound(Cue.Hoof, .30f, .16f),
                new Sound(Cue.Trade, .53f, 0), new Sound(Cue.Recruit, .54f, 0), new Sound(Cue.Reward, .54f, 0),
                new Sound(Cue.Forge, .50f, 0)
            };
        }

        private AudioSource MakeSource(string loop = null)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            if (loop != null)
            {
                source.clip = Resources.Load<AudioClip>(Root + loop);
                source.loop = true;
                source.volume = 0;
            }
            return source;
        }

        private void SetPlace(Place next)
        {
            if (place == next) return;
            distance = Stride * .6;
            if (next == Place.Silent || next == Place.Title) effects.Stop();
            place = next;
        }

        internal void SetExploration(BaseSceneController scene, Place next)
        {
            owner = scene;
            UnbindBattle();
            SetPlace(next);
        }

        internal void Release(BaseSceneController scene)
        {
            if (owner == scene) SetExploration(null, Place.Silent);
        }

        internal void ReleaseBattle(RealtimeCampaignBattle run)
        {
            if (run != null && encounter == run) SetExploration(null, Place.Silent);
        }

        private void OnApplicationPause(bool paused) => suspended = paused;

        private void LateUpdate()
        {
            if (suspended) return;
            float sfx = GameAudioVolume.Effects;
            effects.volume = sfx;
            float music = GameAudioVolume.Music;
            resultMusic.volume = music;
            float duck = Time.unscaledTime < duckUntil ? .23f : 1;
            Fade(ambience, ref ambienceGain, place is Place.Village or Place.Title ? .30f : place == Place.Practice ? .18f : 0, sfx);
            Fade(score, ref scoreGain, place is Place.Village or Place.Title or Place.Field ? .28f * duck : 0, music, true);
            Fade(ruinsScore, ref ruinsGain, place == Place.Dungeon ? .28f * duck : 0, music, true);
            Fade(battleScore, ref battleGain, place == Place.Battle ? .30f * duck : 0, music);
        }

        private static void Fade(AudioSource source, ref float gain, float target, float channelVolume, bool pauseWhenSilent = false)
        {
            if (source.clip == null) return;
            if (target > 0 && !source.isPlaying)
            {
                // The audio device can drop a voice while the editor or phone is in the background.
                source.UnPause();
                if (!source.isPlaying) source.Play();
            }
            gain = Mathf.MoveTowards(gain, target, Time.unscaledDeltaTime * .55f);
            source.volume = gain * channelVolume;
            if (target == 0 && gain <= 0)
            {
                if (pauseWhenSilent)
                {
                    if (source.isPlaying) source.Pause();
                }
                else if (source.isPlaying) source.Stop();
            }
        }

        internal void Footstep(double travelled, BattleBoardView board, double x, double z)
        {
            if (place is not (Place.Village or Place.Field or Place.Dungeon) || encounter != null || travelled <= 0) return;
            distance += travelled;
            if (distance < Stride) return;
            distance %= Stride;
            Play(place == Place.Dungeon ? Cue.Stone : board.FootstepSurfaceAt(x, z) switch
            {
                GroundSurface.Cobble => Cue.Stone,
                GroundSurface.DirtPath or GroundSurface.Sand => Cue.Dirt,
                _ => Cue.Grass
            });
        }

        internal void Play(Cue cue, float proximity = 1)
        {
            if (place == Place.Silent || proximity <= 0 || GameAudioVolume.Effects <= 0) return;
            var sound = sounds[(int)cue];
            if (sound.Clips.Length == 0 || Time.unscaledTime < sound.ReadyAt) return;
            sound.ReadyAt = Time.unscaledTime + sound.Interval;
            effects.volume = GameAudioVolume.Effects;
            effects.PlayOneShot(sound.Clips[sound.Next], sound.Volume * proximity);
            int stride = cue == Cue.Grunt && sound.Clips.Length > 1 ? voiceRandom.Next(1, sound.Clips.Length) : 1;
            sound.Next = (sound.Next + stride) % sound.Clips.Length;
        }

        internal void BindBattle(RealtimeCampaignBattle run)
        {
            if (encounter == run) return;
            UnbindBattle();
            owner = null;
            encounter = run;
            resultHeard = run.Battle.Report != null;
            SetPlace(run.Session.Opening.Id == BaseDemoIntro.BattleId ? Place.Practice :
                run.Session.Opening.EventId == BaseDemoRuins.EventId ? Place.Dungeon : Place.Battle);
            // A null release callback selects the older combat model's immediate-hit path.
            hasAttackRelease = run.Model.AttackTriggered != null;
            if (hasAttackRelease) run.Model.AttackTriggered = (Action<PrototypeUnit>)Strike + run.Model.AttackTriggered;
            run.Tactics.ObjectHit += ObjectHit;
            run.Model.DamageApplied += DamageApplied;
            run.Model.CellEntered += CellEntered;
        }

        private void UnbindBattle()
        {
            if (encounter == null) return;
            if (hasAttackRelease) encounter.Model.AttackTriggered -= Strike;
            encounter.Tactics.ObjectHit -= ObjectHit;
            encounter.Model.DamageApplied -= DamageApplied;
            encounter.Model.CellEntered -= CellEntered;
            encounter = null;
        }

        private void OnDestroy() => UnbindBattle();

        private float Near(PrototypeUnit actor)
        {
            if (encounter?.Model.Player == null) return 0;
            if (actor.Side == PrototypeSide.Enemy && !encounter.Battle.CanInspectUnit(BattleSide.A, encounter.Battle.Soldier(actor.Id-1))) return 0;
            return Mathf.Clamp01(1 - (float)(actor.Position - encounter.Model.Player.Position).Length * .064f);
        }

        private void Strike(PrototypeUnit source) => Play(source.Ranged ? Cue.Bow : Cue.Sword, Near(source));

        private void ObjectHit(PrototypeUnit source, BattleTacticalObject target, double damage)
        {
            if (damage <= 0) return;
            Strike(source);
            Play(Cue.WoodHit, Near(source));
        }

        private void DamageApplied(PrototypeUnit source, PrototypeUnit target, double damage)
        {
            if (damage <= 0) return;
            if (!hasAttackRelease) Strike(source);
            var proximity = Near(target);
            Play(Cue.Hit, proximity);
            if (!target.Alive) Play(Cue.Grunt, proximity);
        }

        private void CellEntered(PrototypeUnit actor, int cell)
        {
            if (actor.Mounted) Play(Cue.Hoof, Near(actor));
        }

        internal void PlayResult(RealtimeCampaignBattle run)
        {
            if (run != encounter || resultHeard || run.Battle.Report == null) return;
            resultHeard = true;
            // These three encounters cut directly to a conversation; leave space for its opening line.
            string id = run.Session.Opening.Id;
            if (id == BaseDemoIntro.BattleId || id == BaseDemoPrologue.BattleId || id == BaseLocalDuel.Id) return;
            var clip = run.Battle.Report.Outcome == BattleOutcome.SideAWin ? victory : defeat;
            if (clip == null || GameAudioVolume.Music <= 0) return;
            resultMusic.volume = GameAudioVolume.Music;
            resultMusic.PlayOneShot(clip, .45f);
            duckUntil = Time.unscaledTime + clip.length;
        }
    }
}
