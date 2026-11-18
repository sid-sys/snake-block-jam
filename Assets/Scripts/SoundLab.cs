using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace SnakeJam
{
    [Serializable] class CatFile { public string root; public CatCat[] categories; }
    [Serializable] class CatCat { public string name, label; public CatSub[] subs; }
    [Serializable] class CatSub { public string name, label; public string[] files; }   // "File.wav|seconds"

    // Admin "Sound test": USED IN GAME lists every event and the sound it plays (SWAP replaces it);
    // LIBRARY browses the whole shared sound pack by category and sub-category (Unity Editor only, played straight from _Shared).
    public class SoundLab : MonoBehaviour
    {
        const int UsedPerPage = 9, SubsPerPage = 11, FilesPerPage = 9;

        Canvas cv; AudioSource src; Action onBack;
        UIKit.PopupHandle popup; RectTransform body;
        int tab, usedPage, cat = -1, sub = -1, page;
        string assignEvent = "", status = "";
        CatFile catalog; string catalogError;
        readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        public static SoundLab Create(Transform parent, Canvas canvas)
        {
            var go = new GameObject("SoundLab");
            go.transform.SetParent(parent);
            var s = go.AddComponent<SoundLab>();
            s.cv = canvas;
            s.src = go.AddComponent<AudioSource>();
            s.src.playOnAwake = false;
            return s;
        }

        public void Open(Action back)
        {
            if (popup != null && popup.dim != null) return;
            onBack = back; tab = 0; usedPage = 0; cat = sub = -1; page = 0; assignEvent = ""; status = "";
            LoadCatalog();
            UIKit.SilentClicks = true;
            popup = UIKit.Popup(cv, "SOUND TEST", new Vector2(1000, 1800));
            popup.onClosed = () => { UIKit.SilentClicks = false; onBack?.Invoke(); };
            Build();
        }

        void LoadCatalog()
        {
#if UNITY_EDITOR
            if (catalog != null) return;
            try
            {
                var p = Path.GetFullPath(Path.Combine(Application.dataPath, "../SfxCatalog/sfx_catalog.json"));
                catalog = JsonUtility.FromJson<CatFile>(File.ReadAllText(p));
                if (catalog == null || catalog.categories == null) catalogError = "The sound catalog is empty.";
            }
            catch (Exception e) { catalogError = "Catalog not found: " + e.Message; }
#else
            catalogError = "The full library works in the Unity Editor only.";
#endif
        }

        // ------------------------------------------------------------ building the screen
        void Build()
        {
            if (popup == null || popup.dim == null) return;
            if (body != null) Destroy(body.gameObject);
            body = UIKit.Stretch(popup.content, "SoundBody");

            var t1 = UIKit.Pill(body, "USED IN GAME", "blue", new Vector2(450, 96), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-240, -150), () => { tab = 0; Build(); }, null, 36);
            var t2 = UIKit.Pill(body, "LIBRARY", "purple", new Vector2(450, 96), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(240, -150), () => { tab = 1; page = 0; Build(); }, null, 36);
            (tab == 0 ? t2 : t1).GetComponent<Image>().color = new Color(1, 1, 1, 0.5f);

            if (!string.IsNullOrEmpty(status))
            {
                var st = UIKit.Label(body, status, 28, new Color(0.15f, 0.55f, 0.25f), TextAnchor.UpperCenter, false);
                st.rectTransform.offsetMax = new Vector2(0, -262);
            }
            if (tab == 0) BuildUsed(); else BuildLibrary();
        }

        void Footer(int pages, Action prev, Action next, string backLabel = null, Action back = null)
        {
            float y = 28;
            UIKit.Pill(body, "< PREV", "blue", new Vector2(250, 90), new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, y), prev, null, 34);
            UIKit.Pill(body, "NEXT >", "blue", new Vector2(250, 90), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, y), next, null, 34);
            var pg = UIKit.Label(body, "page " + (page + 1) + " / " + Mathf.Max(1, pages), 30, UIKit.Ink, TextAnchor.LowerCenter, false);
            pg.rectTransform.offsetMin = new Vector2(0, y + 28);
            if (back != null)
                UIKit.Pill(body, backLabel, "orange", new Vector2(420, 80), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -270 - (string.IsNullOrEmpty(status) ? 0 : 36)), back, null, 32);
        }

        void Row(float y, string line1, string line2, Action play, string extraLabel = null, Action extra = null)
        {
            var row = UIKit.Panel(body, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(920, 124), new Color(0.95f, 0.91f, 0.82f, 1f), "Row");
            float right = extra != null ? 420 : 230;
            var a = UIKit.Label(row.transform, line1, 32, UIKit.Ink, TextAnchor.MiddleLeft, false);
            a.rectTransform.offsetMin = new Vector2(22, 60); a.rectTransform.offsetMax = new Vector2(-right, -4);
            a.overflowMode = TextOverflowModes.Ellipsis;
            var b = UIKit.Label(row.transform, line2, 25, new Color(0.35f, 0.3f, 0.5f), TextAnchor.MiddleLeft, false);
            b.rectTransform.offsetMin = new Vector2(22, 4); b.rectTransform.offsetMax = new Vector2(-right, -62);
            b.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Pill(row.transform, "PLAY", "green", new Vector2(190, 92), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), play, null, 38);
            if (extra != null)
                UIKit.Pill(row.transform, extraLabel, "orange", new Vector2(190, 92), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16 - 202, 0), extra, null, 34);
        }

        // ------------------------------------------------------------ tab 1: sounds the game uses
        void BuildUsed()
        {
            var list = Sfx.MapEntries;
            int pages = Mathf.CeilToInt(list.Count / (float)UsedPerPage);
            usedPage = Mathf.Clamp(usedPage, 0, Mathf.Max(0, pages - 1)); page = usedPage;
            var hint = UIKit.Label(body, "Every game event and its sound. SWAP = pick another sound from the library.", 26, UIKit.Ink, TextAnchor.UpperCenter, false);
            hint.rectTransform.offsetMax = new Vector2(0, -226);
            for (int i = 0; i < UsedPerPage; i++)
            {
                int idx = usedPage * UsedPerPage + i;
                if (idx >= list.Count) break;
                var e = list[idx];
                string ev = e.ev;
                Row(-300 - i * 140f, ev + "   -   " + e.where, e.cat + " / " + Nice(e.sub) + " / " + e.orig + "   (" + e.dur.ToString("0.00") + " s)",
                    () => PlayUsed(ev), "SWAP", () => { assignEvent = ev; tab = 1; cat = sub = -1; page = 0; status = "Pick a sound for: " + ev; Build(); });
            }
            Footer(pages, () => { usedPage = Mathf.Max(0, usedPage - 1); Build(); }, () => { usedPage = Mathf.Min(pages - 1, usedPage + 1); Build(); });
        }

        void PlayUsed(string ev)
        {
            if (Sfx.I != null && Sfx.I.Clips.TryGetValue(ev, out var clip) && clip != null) { src.Stop(); src.PlayOneShot(clip); }
            else { status = "No clip for " + ev; Build(); }
        }

        // ------------------------------------------------------------ tab 2: the whole library
        void BuildLibrary()
        {
            if (catalog == null)
            {
                var m = UIKit.Label(body, catalogError ?? "Library not available.", 36, UIKit.Ink, TextAnchor.MiddleCenter, false);
                m.textWrappingMode = TextWrappingModes.Normal;
                m.rectTransform.offsetMin = new Vector2(60, 200); m.rectTransform.offsetMax = new Vector2(-60, -320);
                return;
            }
            if (cat < 0) { BuildCategories(); return; }
            var c = catalog.categories[cat];
            if (sub < 0) { BuildSubs(c); return; }
            BuildFiles(c, c.subs[sub]);
        }

        void BuildCategories()
        {
            int total = 0; foreach (var c in catalog.categories) foreach (var s in c.subs) total += s.files.Length;
            var hint = UIKit.Label(body, "Library: " + total + " sounds. Choose a category.", 30, UIKit.Ink, TextAnchor.UpperCenter, false);
            hint.rectTransform.offsetMax = new Vector2(0, -226 - (string.IsNullOrEmpty(status) ? 0 : 36));
            string[] cols = { "blue", "green", "purple", "orange" };
            for (int i = 0; i < catalog.categories.Length; i++)
            {
                int idx = i; var c = catalog.categories[i];
                int n = 0; foreach (var s in c.subs) n += s.files.Length;
                UIKit.Pill(body, c.label + "\n" + n + " sounds, " + c.subs.Length + " groups", cols[i % 4], new Vector2(440, 150), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                    new Vector2((i % 2 == 0 ? -235 : 235), -330 - (i / 2) * 175), () => { cat = idx; sub = -1; page = 0; Build(); }, null, 30);
            }
        }

        void BuildSubs(CatCat c)
        {
            int pages = Mathf.CeilToInt(c.subs.Length / (float)SubsPerPage);
            page = Mathf.Clamp(page, 0, Mathf.Max(0, pages - 1));
            string[] cols = { "blue", "green", "purple", "orange" };
            for (int i = 0; i < SubsPerPage; i++)
            {
                int idx = page * SubsPerPage + i;
                if (idx >= c.subs.Length) break;
                var s = c.subs[idx]; float avg = 0; foreach (var f in s.files) avg += Dur(f); avg /= Mathf.Max(1, s.files.Length);
                int sidx = idx;
                UIKit.Pill(body, c.label + "  >  " + s.label + "      " + s.files.Length + " files, avg " + avg.ToString("0.0") + " s", cols[i % 4], new Vector2(900, 92),
                    new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -372 - i * 106), () => { sub = sidx; page = 0; Build(); }, null, 30);
            }
            Footer(pages, () => { page = Mathf.Max(0, page - 1); Build(); }, () => { page = Mathf.Min(pages - 1, page + 1); Build(); }, "< ALL CATEGORIES", () => { cat = -1; page = 0; Build(); });
        }

        void BuildFiles(CatCat c, CatSub s)
        {
            int pages = Mathf.CeilToInt(s.files.Length / (float)FilesPerPage);
            page = Mathf.Clamp(page, 0, Mathf.Max(0, pages - 1));
            for (int i = 0; i < FilesPerPage; i++)
            {
                int idx = page * FilesPerPage + i;
                if (idx >= s.files.Length) break;
                string fn = Name(s.files[idx]); float dur = Dur(s.files[idx]);
                string path = Path.Combine(catalog.root, c.name, fn);
                string catName = c.name, subName = s.name;
                Action use = string.IsNullOrEmpty(assignEvent) ? (Action)null : () =>
                {
#if UNITY_EDITOR
                    bool ok = Sfx.Assign(assignEvent, catName, subName, fn, dur, catalog.root);
                    status = ok ? "Now \"" + assignEvent + "\" plays " + fn : "Could not assign " + fn;
#endif
                    assignEvent = ""; tab = 0; Build();
                };
                Row(-372 - i * 140f, Nice(Path.GetFileNameWithoutExtension(fn)), c.label + " / " + s.label + "   (" + dur.ToString("0.00") + " s)", () => PlayFile(path), use == null ? null : "USE", use);
            }
            Footer(pages, () => { page = Mathf.Max(0, page - 1); Build(); }, () => { page = Mathf.Min(pages - 1, page + 1); Build(); }, "< " + c.label.ToUpper() + " GROUPS", () => { sub = -1; page = 0; Build(); });
        }

        // ------------------------------------------------------------ helpers
        static string Name(string e) { int i = e.IndexOf('|'); return i < 0 ? e : e.Substring(0, i); }
        static float Dur(string e) { int i = e.IndexOf('|'); float v; return i >= 0 && float.TryParse(e.Substring(i + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f; }
        static string Nice(string s) { return s.Replace("_", " "); }

        void PlayFile(string path) { StartCoroutine(LoadAndPlay(path)); }

        IEnumerator LoadAndPlay(string path)
        {
            AudioClip clip;
            if (!cache.TryGetValue(path, out clip))
            {
                using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.WAV))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { status = "Cannot play: " + req.error; Build(); yield break; }
                    clip = DownloadHandlerAudioClip.GetContent(req);
                }
                if (cache.Count > 40) { foreach (var kv in cache) if (kv.Value != null) Destroy(kv.Value); cache.Clear(); }
                cache[path] = clip;
            }
            src.Stop();
            src.PlayOneShot(clip);
        }
    }
}
