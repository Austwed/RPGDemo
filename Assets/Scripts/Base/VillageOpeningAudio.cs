using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed class VillageOpeningAudio : MonoBehaviour
    {
        internal enum Cue { Grass, Stone, Page, Assignment, Sword, Bow, WoodHit, Hit, Fall, Hoof }

        private const string Root = "Audio/VillageOpening/";
        private const double Stride = 1.05;

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

        internal bool TakingSteps => active && !inPractice;

        private void Awake()
        {
            ambience = MakeSource("Ambience");
            score = MakeSource("Exploration");
            effects = MakeSource();
            sounds = new[]
            {
                new Sound(Cue.Grass, .44f, .12f), new Sound(Cue.Stone, .41f, .12f),
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
            if (active != opening || inPractice != practice) distance = 0;
            if (active && !opening) effects.Stop();
            active = opening;
            inPractice = practice;
        }

        private void LateUpdate()
        {
            Fade(ambience, active ? (inPractice ? .18f : .30f) : 0);
            Fade(score, active && !inPractice ? .28f : 0, pauseWhenSilent: active);
        }

        private static void Fade(AudioSource source, float target, bool pauseWhenSilent = false)
        {
            if (source.clip == null) return;
            if (target > 0 && !source.isPlaying)
            {
                if (source.timeSamples > 0) source.UnPause();
                else source.Play();
            }
            source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * .55f);
            if (target == 0 && source.volume <= 0)
            {
                if (pauseWhenSilent)
                {
                    if (source.isPlaying) source.Pause();
                }
                else if (source.isPlaying || source.timeSamples > 0) source.Stop();
            }
        }

        internal void Footstep(double travelled, GroundSurface surface)
        {
            if (!TakingSteps || travelled <= 0) return;
            distance += travelled;
            if (distance < Stride) return;
            distance %= Stride;
            Play(surface == GroundSurface.Road || surface == GroundSurface.Cobble ? Cue.Stone : Cue.Grass);
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
