using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using BaseConquest.Presentation;

namespace BaseConquest.Base
{
    // The title has its own clock and two authored routes. These actors never enter the campaign roster.
    public sealed class VillageTitleLoop : MonoBehaviour
    {
        [Serializable]
        private sealed class Walker
        {
            public RawImage Image;
            public Texture2D Standing, Walking;
            public Vector2 From, To;
            public float Seconds, Rest, Offset;
            public void Sample(float clock)
            {
                float half = Rest + Seconds;
                float t = Mathf.Repeat(clock + Offset, half * 2);
                bool outbound = t < half;
                float leg = outbound ? t : t - half;
                bool moving = leg >= Rest;
                float part = Mathf.Clamp01((leg - Rest) / Seconds);
                Vector2 start = outbound ? From : To, end = outbound ? To : From;
                Vector2 delta = end - start;
                var facing = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                    ? (delta.x < 0 ? HeroFacing.West : HeroFacing.East)
                    : (delta.y < 0 ? HeroFacing.South : HeroFacing.North);
                int columns = moving ? 8 : 5;
                int frame = moving ? (int)((leg - Rest) * 8) % columns : 0;
                Image.texture = moving ? Walking : Standing;
                Image.uvRect = new Rect(frame / (float)columns, (3 - (int)facing) / 4f, 1f / columns, .25f);
                Image.rectTransform.anchoredPosition = Vector2.Lerp(start, end, part);
            }
        }

        [SerializeField] private Walker[] villagers;
        [SerializeField] private RawImage rabbit;
        [SerializeField] private Vector2 burrow, grass;
        private Action back;
        private float clock;
        internal void BindBack(Action callback) => back = callback;
        private void OnEnable() { clock = 0; Sample(0); }
        private void Update()
        {
            clock = Mathf.Repeat(clock + Time.unscaledDeltaTime, 3600);
            if (rabbit.gameObject.activeInHierarchy) Sample(clock);
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) back?.Invoke();
        }

        public void Sample(float seconds)
        {
            foreach (var villager in villagers) villager.Sample(seconds);
            // Emerge, hop to the grass, graze, then go home and disappear underground for a moment.
            float t = Mathf.Repeat(seconds + 1.1f, 12f);
            const float emerge = .65f, outEnd = 2.5f, returnStart = 7, returnEnd = 8.85f, hidden = 9.5f;
            bool walking = t >= emerge && t < outEnd || t >= returnStart && t < returnEnd;
            float distance = t < outEnd ? Mathf.InverseLerp(emerge, outEnd, t) : 1 - Mathf.InverseLerp(returnStart, returnEnd, t);
            rabbit.rectTransform.anchoredPosition = Vector2.Lerp(burrow, grass, distance);
            rabbit.enabled = t < hidden;
            int action = t < emerge ? 3 : walking ? 1 : t >= returnEnd ? 2 : 0;
            int frame = action == 3 ? (int)(t / emerge * 8) : action == 2 ? Mathf.Min(7,(int)((t - returnEnd) / (hidden - returnEnd) * 8)) : (int)(t * 10) % 8;
            bool left = t < returnStart ? grass.x < burrow.x : burrow.x < grass.x;
            rabbit.uvRect = new Rect((action * 8 + frame) / 32f, left ? .5f : 0, 1 / 32f, .5f);
        }
    }
}
