using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace SnakeJam
{
    public static class Art
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static Sprite circle, round;

        public static Sprite Get(string name)
        {
            if (cache.TryGetValue(name, out var s) && s != null) return s;
            s = Resources.Load<Sprite>("Sprites/" + name);
            if (s == null) Debug.LogWarning("Missing sprite: " + name);
            cache[name] = s;
            return s;
        }

        public static Font Font => font != null ? font : (font = Resources.Load<Font>("Fonts/Heavy"));
        static Font font;

        static TMP_FontAsset tmpFont; static Material tmpShadow;
        // TextMeshPro font made from the Lato Heavy file; characters are added on demand
        public static TMP_FontAsset TmpFont
        {
            get
            {
                if (tmpFont == null)
                {
                    tmpFont = TMP_FontAsset.CreateFontAsset(Font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                    tmpFont.name = "Lato Heavy SDF";
                }
                return tmpFont;
            }
        }
        // ---- picture font: every letter and digit is a glossy candy picture (Resources/Fonts/candy_atlas.png + candy_glyphs.json) ----
        [System.Serializable] class GlyphRow { public string n; public int x, y, w, h, adv, by; }
        [System.Serializable] class GlyphTable { public GlyphRow[] g; }
        static TMP_SpriteAsset candySprites;
        static readonly Dictionary<char, string> candyNames = new Dictionary<char, string>();
        public const float CandyPointSize = 205f;   // bigger number = smaller letters
        public static TMP_SpriteAsset CandySprites
        {
            get
            {
                if (candySprites != null) return candySprites;
                var tex = Resources.Load<Texture2D>("Fonts/candy_atlas");
                var json = Resources.Load<TextAsset>("Fonts/candy_glyphs");
                if (tex == null || json == null) return null;
                var table = JsonUtility.FromJson<GlyphTable>("{\"g\":" + json.text + "}");
                var a = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                a.name = "Candy Letters";
                // mark as a current version so Unity does not try to upgrade an old-format asset
                typeof(TMP_SpriteAsset).GetField("m_Version", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(a, "1.1.0");
                a.spriteSheet = tex;
                Shader sh = Shader.Find("TextMeshPro/Sprite");
                a.material = sh != null ? new Material(sh) : new Material(TMP_Settings.defaultSpriteAsset.material);
                a.material.mainTexture = tex;
                a.faceInfo = new UnityEngine.TextCore.FaceInfo { pointSize = CandyPointSize, scale = 1f, ascentLine = CandyPointSize * 0.8f, descentLine = -CandyPointSize * 0.2f, lineHeight = CandyPointSize, baseline = 0f };
                for (int i = 0; i < table.g.Length; i++)
                {
                    var r = table.g[i];
                    var gl = new TMP_SpriteGlyph
                    {
                        index = (uint)i,
                        metrics = new UnityEngine.TextCore.GlyphMetrics(r.w, r.h, 0f, r.by, r.adv),
                        glyphRect = new UnityEngine.TextCore.GlyphRect(r.x, tex.height - r.y - r.h, r.w, r.h),
                        scale = 1f, atlasIndex = 0
                    };
                    a.spriteGlyphTable.Add(gl);
                    var ch = new TMP_SpriteCharacter(0xFFFE, gl) { name = r.n, scale = 1f };
                    a.spriteCharacterTable.Add(ch);
                    if (r.n.Length == 1) candyNames[r.n[0]] = r.n;
                }
                candyNames['+'] = "plus"; candyNames[':'] = "colon"; candyNames['.'] = "dot"; candyNames['!'] = "excl";
                candyNames['?'] = "quest"; candyNames['-'] = "minus"; candyNames['%'] = "percent"; candyNames['('] = "lpar"; candyNames[')'] = "rpar"; candyNames[','] = "comma";
                a.UpdateLookupTables();
                return candySprites = a;
            }
        }
        class CandyPre : ITextPreprocessor
        {
            public string PreprocessText(string text)
            {
                if (string.IsNullOrEmpty(text)) return text;
                var sb = new System.Text.StringBuilder(text.Length * 20);
                foreach (char c0 in text)
                {
                    char c = char.ToUpperInvariant(c0);
                    string nm;
                    if (candyNames.TryGetValue(c, out nm)) sb.Append("<sprite name=\"").Append(nm).Append("\" tint=1>");
                    else sb.Append(c0);
                }
                return sb.ToString();
            }
        }
        public static readonly ITextPreprocessor CandyPreprocessor = new CandyPre();

        static Material tmpOutline;
        // same font with a dark outline and a drop shadow: the one text style of the whole game
        public static Material TmpOutlineMaterial
        {
            get
            {
                if (tmpOutline == null)
                {
                    tmpOutline = new Material(TmpShadowMaterial) { name = "Lato Heavy SDF Outline" };
                    tmpOutline.EnableKeyword(ShaderUtilities.Keyword_Outline);
                    tmpOutline.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.17f, 0.1f, 0.42f, 1f));
                    tmpOutline.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
                    tmpOutline.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.05f);
                    tmpOutline.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.05f, 0.02f, 0.2f, 0.5f));
                }
                return tmpOutline;
            }
        }
        // same font with a soft drop shadow (underlay) shared by all labels
        public static Material TmpShadowMaterial
        {
            get
            {
                if (tmpShadow == null)
                {
                    tmpShadow = new Material(TmpFont.material) { name = "Lato Heavy SDF Shadow" };
                    tmpShadow.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                    tmpShadow.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.05f, 0.02f, 0.2f, 0.38f));
                    tmpShadow.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
                    tmpShadow.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.45f);
                    tmpShadow.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.15f);
                    tmpShadow.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f);
                }
                return tmpShadow;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (circle != null) return circle;
                const int N = 64;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f));
                        px[y * N + x] = new Color(1, 1, 1, Mathf.Clamp01(N / 2f - d));
                    }
                t.SetPixels(px); t.Apply();
                return circle = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 64f);
            }
        }

        static Sprite skullBig, stripes;
        // big red skull with a thick white outline (hard level banner)
        public static Sprite SkullBig
        {
            get
            {
                if (skullBig != null) return skullBig;
                const int N = 160;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var red = new Color(0.93f, 0.12f, 0.14f, 1f);
                float k = N / 64f;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float px = (x + 0.5f) / k, py = (y + 0.5f) / k;
                        float head = 22f - Vector2.Distance(new Vector2(px, py), new Vector2(32, 38));
                        float jaw = Mathf.Min(Mathf.Min(px - 19f, 45f - px), Mathf.Min(py - 9f, 28f - py)) + 0.5f;
                        float s = Mathf.Max(head, jaw);                       // > 0 inside the skull
                        float outline = Mathf.Clamp01((s + 3.2f) * k * 0.5f + 0.5f);   // skull grown by 3 units: the white outline
                        float inside = Mathf.Clamp01(s * k * 0.5f + 0.5f);
                        float e1 = 5.4f - Vector2.Distance(new Vector2(px, py), new Vector2(23.5f, 37f));
                        float e2 = 5.4f - Vector2.Distance(new Vector2(px, py), new Vector2(40.5f, 37f));
                        float nose = Mathf.Min(Mathf.Min(py - 23f, 31f - py), (py - 23f) * 0.5f + 0.5f - Mathf.Abs(px - 32f));
                        float hole = Mathf.Clamp01(Mathf.Max(Mathf.Max(e1, e2), nose) * k * 0.5f + 0.5f);
                        float tooth = (Mathf.Abs(px - 28f) < 0.8f || Mathf.Abs(px - 32f) < 0.8f || Mathf.Abs(px - 36f) < 0.8f) && py < 19f && py > 10f ? 1f : 0f;
                        Color fill = Color.Lerp(red, Color.white, Mathf.Max(hole, 0f));
                        fill = Color.Lerp(fill, red, tooth);
                        Color c = Color.Lerp(Color.white, fill, inside);
                        t.SetPixel(x, y, new Color(c.r, c.g, c.b, outline));
                    }
                t.Apply();
                return skullBig = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        // diagonal red / white hazard stripes (tile it)
        public static Sprite Stripes
        {
            get
            {
                if (stripes != null) return stripes;
                const int N = 48;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        bool a = ((x + y) % N) < N / 2;
                        t.SetPixel(x, y, a ? new Color(1f, 0.22f, 0.22f, 1f) : new Color(1f, 0.9f, 0.9f, 1f));
                    }
                t.Apply();
                return stripes = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
        }

        static Sprite skull;
        // small white skull (hard level mark)
        public static Sprite Skull
        {
            get
            {
                if (skull != null) return skull;
                const int N = 64;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var dark = new Color(0.25f, 0.1f, 0.42f, 1f);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float head = 24f - Vector2.Distance(new Vector2(px, py), new Vector2(32, 38));
                        float jawX = Mathf.Min(px - 20f, 44f - px), jawY = Mathf.Min(py - 8f, 26f - py);
                        float jaw = Mathf.Min(jawX, jawY);
                        float a = Mathf.Clamp01(Mathf.Max(head, jaw) + 0.5f);
                        float e1 = 7f - Vector2.Distance(new Vector2(px, py), new Vector2(23, 36));
                        float e2 = 7f - Vector2.Distance(new Vector2(px, py), new Vector2(41, 36));
                        float nose = Mathf.Min(py - 22f, 30f - py) < 0 ? -1f : 3f - Mathf.Abs(px - 32f) * 1.2f - (py - 22f) * 0.0f;
                        float hole = Mathf.Clamp01(Mathf.Max(Mathf.Max(e1, e2), Mathf.Min(nose, 1f)) + 0.5f);
                        // teeth lines
                        float tooth = (Mathf.Abs(px - 28f) < 0.7f || Mathf.Abs(px - 36f) < 0.7f) && py < 16f ? 1f : 0f;
                        var c = Color.Lerp(Color.white, dark, Mathf.Max(hole, tooth * 0.8f));
                        t.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
                    }
                t.Apply();
                return skull = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 64f);
            }
        }

        static Sprite glow;
        // soft round glow (bright centre, fades to nothing)
        public static Sprite Glow
        {
            get
            {
                if (glow != null) return glow;
                const int N = 128;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                        float a = Mathf.Clamp01(1f - r); a *= a;
                        t.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                t.Apply();
                return glow = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        // white rounded square with 9-slice border, tint it with a colour (no outline)
        public static Sprite Round
        {
            get
            {
                if (round != null) return round;
                const int N = 64; const float R = 22f;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float qx = Mathf.Abs(x + 0.5f - N / 2f) - (N / 2f - R);
                        float qy = Mathf.Abs(y + 0.5f - N / 2f) - (N / 2f - R);
                        float dist = Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - R;
                        px[y * N + x] = new Color(1, 1, 1, Mathf.Clamp01(0.5f - dist));
                    }
                t.SetPixels(px); t.Apply();
                return round = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
            }
        }
    }

    public static class Fx
    {
        public static Transform Root;

        public static void Burst(Vector3 pos, Color col, int count, float radius, float dur, float size = 0.16f, Sprite sprite = null, int order = 30)
        {
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("fx");
                go.transform.SetParent(Root, false);
                go.transform.position = pos;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite != null ? sprite : Art.Circle;
                sr.color = col; sr.sortingOrder = order;
                float sc = size * Random.Range(0.6f, 1.2f);
                go.transform.localScale = Vector3.one * sc;
                float ang = Random.value * Mathf.PI * 2f;
                var target = pos + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0) * radius * Random.Range(0.5f, 1f);
                var seq = DOTween.Sequence();
                seq.Join(go.transform.DOMove(target, dur).SetEase(Ease.OutQuad));
                seq.Join(sr.DOFade(0f, dur).SetEase(Ease.InQuad));
                seq.Join(go.transform.DOScale(sc * 0.3f, dur));
                seq.OnComplete(() => Object.Destroy(go));
            }
        }

        public static void Shake(float strength, float dur)
        {
            // Feel camera shaker (MMCameraShaker sits on the camera)
            MMCameraShakeEvent.Trigger(dur, strength, 45f, 0f, 0f, 0f, false, null, false);
        }
    }
}
