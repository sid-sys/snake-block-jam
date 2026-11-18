using System.Collections.Generic;
using UnityEngine;

namespace SnakeJam
{
    [System.Serializable] public class SfxMapEntry { public string ev, file, cat, sub, orig, where; public float dur; }
    [System.Serializable] public class SfxMapFile { public SfxMapEntry[] entries; }

    // Sounds: every event has a built-in synthesised fallback. Real recordings from the shared pack
    // (Assets/Resources/Audio/Sfx, listed in Assets/Resources/Audio/sfx_map.json) replace them when present.
    public class Sfx : MonoBehaviour
    {
        public static Sfx I;
        public static List<SfxMapEntry> MapEntries = new List<SfxMapEntry>();
        public static event System.Action MapChanged;
        AudioSource src;
        public Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        const int SR = 44100;

        public static Sfx Create(Transform parent)
        {
            var go = new GameObject("Sfx");
            go.transform.SetParent(parent);
            var s = go.AddComponent<Sfx>();
            s.src = go.AddComponent<AudioSource>();
            s.src.playOnAwake = false;
            I = s;
            s.Build();
            s.LoadMapped();
            return s;
        }

        public static void Play(string name, float vol = 1f, float pitch = 1f)
        {
            FeelFx.Haptic(name);   // phone buzz that matches the sound (Feel / Nice Vibrations)
            if (I == null || Save.S == null || !Save.S.sfx) return;
            if (!I.Clips.TryGetValue(name, out var c)) return;
            I.src.pitch = pitch;
            I.src.PlayOneShot(c, vol);
        }

        static string MapPath { get { return System.IO.Path.Combine(Application.dataPath, "Resources/Audio/sfx_map.json"); } }

        void LoadMapped()
        {
            MapEntries.Clear();
            string json = null;
#if UNITY_EDITOR
            if (System.IO.File.Exists(MapPath)) json = System.IO.File.ReadAllText(MapPath);
#endif
            if (json == null) { var ta = Resources.Load<TextAsset>("Audio/sfx_map"); if (ta != null) json = ta.text; }
            if (string.IsNullOrEmpty(json)) return;
            var f = JsonUtility.FromJson<SfxMapFile>(json);
            if (f == null || f.entries == null) return;
            foreach (var e in f.entries)
            {
                MapEntries.Add(e);
                var clip = Resources.Load<AudioClip>("Audio/Sfx/" + e.file);
                if (clip != null) Clips[e.ev] = clip;
            }
        }

#if UNITY_EDITOR
        // copy ONE sound from the shared pack into the game and make it the sound of an event
        public static bool Assign(string ev, string cat, string sub, string fileName, float dur, string sharedRoot)
        {
            try
            {
                string outName = cat + "__" + System.IO.Path.GetFileNameWithoutExtension(fileName);
                string dstDir = System.IO.Path.Combine(Application.dataPath, "Resources/Audio/Sfx");
                System.IO.Directory.CreateDirectory(dstDir);
                System.IO.File.Copy(System.IO.Path.Combine(sharedRoot, cat, fileName), System.IO.Path.Combine(dstDir, outName + ".wav"), true);
                UnityEditor.AssetDatabase.ImportAsset("Assets/Resources/Audio/Sfx/" + outName + ".wav", UnityEditor.ImportAssetOptions.ForceSynchronousImport);
                foreach (var e in MapEntries) if (e.ev == ev) { e.file = outName; e.cat = cat; e.sub = sub; e.orig = fileName; e.dur = dur; }
                System.IO.File.WriteAllText(MapPath, JsonUtility.ToJson(new SfxMapFile { entries = MapEntries.ToArray() }, true));
                var clip = Resources.Load<AudioClip>("Audio/Sfx/" + outName);
                if (clip != null && I != null) I.Clips[ev] = clip;
                MapChanged?.Invoke();
                return true;
            }
            catch (System.Exception ex) { Debug.LogWarning("Assign failed: " + ex.Message); return false; }
        }
#endif

        void Build()
        {
            Clips["ui_click"] = Make(0.05f, (t, n) => Sine(t, 1200) * Env(t, 0.05f, 0.002f) * 0.4f);
            Clips["tap"] = Make(0.07f, (t, n) => Sine(t, 900) * Env(t, 0.07f, 0.002f) * 0.6f + Sine(t, 1800) * Env(t, 0.07f, 0.002f) * 0.2f);
            Clips["slide"] = Make(0.35f, (t, n) => Noise(n, t) * Env(t, 0.35f, 0.05f) * 0.18f * Mathf.Sin(t * 70f));
            Clips["bump"] = Make(0.28f, (t, n) => Sine(t, Mathf.Lerp(420, 140, t / 0.28f)) * Env(t, 0.28f, 0.004f) * 0.8f);
            Clips["thud"] = Make(0.16f, (t, n) => (Sine(t, 90) + Noise(n, t) * 0.25f) * Env(t, 0.16f, 0.002f) * 0.9f);
            Clips["locked"] = Make(0.2f, (t, n) => Mathf.Sign(Sine(t, 180)) * Env(t, 0.2f, 0.003f) * 0.25f);
            Clips["escape"] = Arp(new[] { 523f, 659f, 784f, 1047f }, 0.07f);
            Clips["coin"] = Arp(new[] { 988f, 1319f }, 0.06f);
            Clips["win"] = Arp(new[] { 523f, 659f, 784f, 1047f, 784f, 1047f, 1319f }, 0.1f);
            Clips["pop"] = Make(0.1f, (t, n) => Sine(t, Mathf.Lerp(500, 1200, t / 0.1f)) * Env(t, 0.1f, 0.002f) * 0.6f);
            Clips["buy"] = Arp(new[] { 784f, 1047f, 1568f }, 0.06f);
            Clips["hint"] = Arp(new[] { 880f, 1175f }, 0.09f);
            Clips["undo"] = Make(0.18f, (t, n) => Sine(t, Mathf.Lerp(900, 450, t / 0.18f)) * Env(t, 0.18f, 0.003f) * 0.6f);
            Clips["vacuum"] = Make(0.4f, (t, n) => (Sine(t, Mathf.Lerp(200, 1400, t / 0.4f)) * 0.5f + Noise(n, t) * 0.15f) * Env(t, 0.4f, 0.01f));
            Clips["drill"] = Make(0.9f, (t, n) => (Sine(t, 110f + 45f * Mathf.Sin(t * 70f)) * 0.5f + Noise(n, t) * 0.35f) * Env(t, 0.9f, 0.02f) * 0.8f);
            Clips["thaw"] = Arp(new[] { 1319f, 1760f, 2093f }, 0.05f);
        }

        delegate float Gen(float t, int n);

        static float Sine(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);
        static float Noise(int n, float t) { unchecked { int x = n * 1103515245 + 12345; x ^= x >> 13; x *= 1274126177; return ((x & 0xFFFF) / 32768f) - 1f; } }
        static float Env(float t, float len, float attack)
        {
            float a = Mathf.Clamp01(t / attack);
            float r = Mathf.Clamp01((len - t) / (len * 0.6f));
            return a * r;
        }

        static AudioClip Make(float len, Gen g)
        {
            int count = Mathf.CeilToInt(len * SR);
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(g(i / (float)SR, i), -1f, 1f);
            var c = AudioClip.Create("sfx", count, 1, SR, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip Arp(float[] notes, float step)
        {
            float len = step * notes.Length + 0.25f;
            return Make(len, (t, n) =>
            {
                float v = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float st = k * step; if (t < st) continue;
                    float lt = t - st;
                    v += (Sine(lt, notes[k]) + 0.3f * Sine(lt, notes[k] * 2f)) * Mathf.Exp(-lt * 9f) * 0.35f;
                }
                return v;
            });
        }
    }
}
