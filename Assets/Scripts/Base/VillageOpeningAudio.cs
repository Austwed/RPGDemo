using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed class VillageOpeningAudio : MonoBehaviour
    {
        internal enum Cue { Grass, Stone, Dirt, Page, Assignment, Sword, Bow, WoodHit, Hit, Fall, Hoof }

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

        private AudioSource ambience, score, effects;
        private Sound[] sounds;
        private bool active, inPractice;
        private double distance;
        private float ambienceGain, scoreGain;

        private void Awake()
        {
            ambience = MakeSource("Ambience");
            score = MakeSource("Exploration");
            effects = MakeSource();
            sounds = new[]
            {
                new Sound(Cue.Grass, .30f, .14f), new Sound(Cue.Stone, .39f, .14f), new Sound(Cue.Dirt, .31f, .14f),
                new Sound(Cue.Page, .37f, .09f), new Sound(Cue.Assignment, .53f, .16f),
                new Sound(Cue.Sword, .38f, .12f), new Sound(Cue.Bow, .40f, .12f),
                new Sound(Cue.WoodHit, .49f, .13f), new Sound(Cue.Hit, .47f, .13f),
                new Sound(Cue.Fall, .46f, .23f), new Sound(Cue.Hoof, .30f, .16f)
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

        internal void SetContext(bool opening, bool practice)
        {
            if (active != opening || inPractice != practice) distance = Stride * .6;
            if (active && !opening) effects.Stop();
            active = opening;
            inPractice = practice;
        }

        private void LateUpdate()
        {
            float sfx = GameAudioVolume.Effects;
            effects.volume = sfx;
            Fade(ambience, ref ambienceGain, active ? (inPractice ? .18f : .30f) : 0, sfx);
            Fade(score, ref scoreGain, active && !inPractice ? .28f : 0, GameAudioVolume.Music, pauseWhenSilent: active);
        }

        private static void Fade(AudioSource source, ref float gain, float target, float channelVolume, bool pauseWhenSilent = false)
        {
            if (source.clip == null) return;
            if (target > 0 && !source.isPlaying)
            {
                if (source.timeSamples > 0) source.UnPause();
                else source.Play();
            }
            gain = Mathf.MoveTowards(gain, target, Time.unscaledDeltaTime * .55f);
            source.volume = gain * channelVolume;
            if (target == 0 && gain <= 0)
            {
                if (pauseWhenSilent)
                {
                    if (source.isPlaying) source.Pause();
                }
                else if (source.isPlaying || source.timeSamples > 0) source.Stop();
            }
        }

        internal void Footstep(double travelled, BattleBoardView board, double x, double z)
        {
            if (!active || inPractice || travelled <= 0) return;
            distance += travelled;
            if (distance < Stride) return;
            distance %= Stride;
            Play(board.FootstepSurfaceAt(x, z) switch
            {
                GroundSurface.Cobble => Cue.Stone,
                GroundSurface.DirtPath or GroundSurface.Sand => Cue.Dirt,
                _ => Cue.Grass
            });
        }

        internal void Play(Cue cue, float proximity = 1)
        {
            if (!active || proximity <= 0) return;
            var sound = sounds[(int)cue];
            if (sound.Clips.Length == 0 || Time.unscaledTime < sound.ReadyAt) return;
            sound.ReadyAt = Time.unscaledTime + sound.Interval;
            effects.PlayOneShot(sound.Clips[sound.Next], sound.Volume * proximity);
            sound.Next = (sound.Next + 1) % sound.Clips.Length;
        }

        internal void BindPractice(RealtimeCampaignBattle practice)
        {
            float Near(PrototypeUnit actor) => Mathf.Clamp01(1 - (float)(actor.Position - practice.Model.Player.Position).Length * .085f);
            void Strike(PrototypeUnit source) => Play(source.Ranged ? Cue.Bow : Cue.Sword, Near(source));

            var attack = practice.Model.AttackTriggered;
            if (attack != null)
            {
                practice.Model.AttackTriggered = source =>
                {
                    Strike(source);
                    attack(source);
                };
            }
            practice.Tactics.ObjectHit += (source, _, damage) =>
            {
                if (damage <= 0) return;
                Strike(source);
                Play(Cue.WoodHit, Near(source));
            };
            practice.Model.DamageApplied += (source, target, damage) =>
            {
                if (damage <= 0) return;
                if (attack == null) Strike(source);
                var proximity = Near(target);
                Play(Cue.Hit, proximity);
                if (!target.Alive) Play(Cue.Fall, proximity);
            };
            practice.Model.CellEntered += (actor, _) =>
            {
                if (actor.FootprintLength == 2) Play(Cue.Hoof, Near(actor));
            };
        }
    }
}
