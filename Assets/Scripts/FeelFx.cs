using Lofelt.NiceVibrations;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using MoreMountains.Tools;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SnakeJam
{
    // Game feel from the Feel package (More Mountains):
    //  1. phone haptics (Nice Vibrations) - a different buzz for tap, escape, hit, win, fail, ...
    //  2. hit-stop (MMTimeManager freeze frame) - the game freezes for a blink when a snake hits something
    //  3. screen post effects (URP volume driven by Feel shakers) - colour-split pulse on hit, soft glow on win
    //  4. camera shake - lives in Art.cs (Fx.Shake) and also uses Feel
    public class FeelFx : MonoBehaviour
    {
        public static FeelFx I;
        public static bool Enabled = true;     // admin switch: turns every Feel effect off (haptics + hit-stop + post effects)

        static float lastHaptic;
        static readonly AnimationCurve Pulse = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f));
        static readonly AnimationCurve Flat = AnimationCurve.Constant(0f, 1f, 1f);

        public static FeelFx Create(Transform parent, Camera cam)
        {
            var go = new GameObject("FeelFx");
            go.transform.SetParent(parent);
            var f = go.AddComponent<FeelFx>();
            I = f;
            f.Setup(cam);
            return f;
        }

        void Setup(Camera cam)
        {
            try { HapticController.Init(); } catch { }

            // hit-stop needs a time manager in the scene
            gameObject.AddComponent<MMTimeManager>();

            // post processing: a global volume with chromatic aberration and bloom, both at zero until a pulse is fired
            var ad = cam.GetUniversalAdditionalCameraData();
            if (ad != null) ad.renderPostProcessing = true;
            var vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 10;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var ca = prof.Add<ChromaticAberration>(true);
            ca.intensity.Override(0f);
            var bl = prof.Add<Bloom>(true);
            bl.intensity.Override(0f); bl.threshold.Override(0.9f); bl.scatter.Override(0.65f);
            vol.sharedProfile = prof;
            gameObject.AddComponent<MMChromaticAberrationShaker_URP>();
            gameObject.AddComponent<MMBloomShaker_URP>();
        }

        // ------------------------------------------------------------------ haptics
        // called by Sfx.Play for every sound event name, so each sound has a matching buzz
        public static void Haptic(string ev)
        {
            if (!Enabled || Save.S == null || !Save.S.vibration) return;
            float now = Time.unscaledTime;
            HapticPatterns.PresetType? p = null;
            switch (ev)
            {
                case "ui_click": p = HapticPatterns.PresetType.Selection; break;
                case "tap": p = HapticPatterns.PresetType.LightImpact; break;
                case "escape": p = HapticPatterns.PresetType.SoftImpact; break;
                case "bump": p = HapticPatterns.PresetType.HeavyImpact; break;
                case "locked": p = HapticPatterns.PresetType.Warning; break;
                case "light": p = HapticPatterns.PresetType.MediumImpact; break;
                case "rock": p = HapticPatterns.PresetType.RigidImpact; break;
                case "thaw": p = HapticPatterns.PresetType.SoftImpact; break;
                case "win": p = HapticPatterns.PresetType.Success; break;
                case "fail": p = HapticPatterns.PresetType.Failure; break;
                case "buy": p = HapticPatterns.PresetType.Success; break;
                case "pop": p = HapticPatterns.PresetType.MediumImpact; break;
                case "coin": if (now - lastHaptic > 0.12f) p = HapticPatterns.PresetType.LightImpact; break;
                case "undo": p = HapticPatterns.PresetType.Selection; break;
                case "hint": p = HapticPatterns.PresetType.LightImpact; break;
                case "vacuum": p = HapticPatterns.PresetType.MediumImpact; break;
            }
            try
            {
                if (ev == "drill") { HapticPatterns.PlayConstant(0.45f, 0.3f, 0.9f); lastHaptic = now; return; }
                if (p == null || now - lastHaptic < 0.04f) return;
                lastHaptic = now;
                HapticPatterns.PlayPreset(p.Value);
            }
            catch { }
        }

        // ------------------------------------------------------------------ hit-stop
        public static void HitStop(float seconds)
        {
            if (!Enabled || I == null) return;
            MMFreezeFrameEvent.Trigger(seconds, 0.1f);
        }

        // ------------------------------------------------------------------ post effects
        public static void ColorSplit(float amount, float seconds)
        {
            if (!Enabled || I == null) return;
            MMChromaticAberrationShakeEvent_URP.Trigger(Pulse, seconds, 0f, amount, false, 1f, null, true, true, true, TimescaleModes.Unscaled);
        }

        public static void Glow(float amount, float seconds)
        {
            if (!Enabled || I == null) return;
            MMBloomShakeEvent_URP.Trigger(Pulse, seconds, 0f, amount, Flat, 0.9f, 0.9f, false, 1f, null, true, true, true, TimescaleModes.Unscaled, false, false, true, false);
        }

        // ------------------------------------------------------------------ named moments
        public static void Hit() { HitStop(0.07f); ColorSplit(0.32f, 0.4f); }
        public static void RockBreak() { HitStop(0.04f); ColorSplit(0.2f, 0.3f); }
        public static void Win() { Glow(1.6f, 1.1f); ColorSplit(0.15f, 0.6f); }
        public static void Praise() { Glow(0.9f, 0.6f); }
    }
}
