using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace SnakeJam
{
    // A snake is drawn as a thin continuous ribbon: pieces sit a third of a cell apart on the snake's own track.
    public class SnakeView : MonoBehaviour
    {
        public SnakeDef def;
        public List<Vector2Int> cells;          // logical cells, 0 = head
        public int frozenLeft;
        public bool busy;
        public int id;
        public Color color;
        Color baseTint;

        Board board;
        readonly List<Transform> pieces = new List<Transform>();   // 0 = head, then body toward tail
        readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
        Transform eyes;
        readonly SpriteRenderer[] eyeSr = new SpriteRenderer[2];
        readonly Transform[] pupils = new Transform[2];
        readonly SpriteRenderer[] pupilSr = new SpriteRenderer[2];
        readonly SpriteRenderer[] shineSr = new SpriteRenderer[2];
        float eyeDia, pupilDia, eyeOff, eyeScale;
        Tween spin;
        SpriteRenderer snow;
        Transform drillIcon; SpriteRenderer drillSr;
        Vector3 endDir = Vector3.right, curDir = Vector3.right;
        Vector2Int lastQ = new Vector2Int(9, 9);
        List<Vector3> path;                      // tail cell ... head cell, then the cells in front of the head
        bool[] tele;                             // tele[i] = the step i -> i+1 is a portal jump
        bool hasTele;
        float[] baseScale, widthOf;
        bool[] underState, fadeState;
        int[] lastOrder;
        bool iced;
        Tween drillSpin;
        int[] baseOrder; bool[] over; int headOrderBase;
        const float BodyW = 0.7f;               // ribbon width in cells: leaves a clear gap between neighbour snakes
        const float HeadW = 0.82f;
        const int Sub = 3;                       // pieces per cell
        const float SpriteUnits = 1.1f;          // body sprite is ~1.1 world units
        const int OverBonus = 2000;
        public int Length => cells.Count;

        public void Init(Board b, SnakeDef d, Color col, int snakeId)
        {
            board = b; def = d; color = col; id = snakeId;
            cells = new List<Vector2Int>(d.cells);
            int n = cells.Count;
            int m = Sub * (n - 1) + 1;
            baseScale = new float[m]; widthOf = new float[m]; baseOrder = new int[m]; over = new bool[m]; underState = new bool[m]; fadeState = new bool[m]; lastOrder = new int[m];
            for (int j = 0; j < m; j++)
            {
                var go = new GameObject(j == 0 ? "Head" : "P" + j);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Art.Get(j == 0 ? "snake_head" : "snake_body");
                sr.color = col;
                baseOrder[j] = 10 + (m - j);
                sr.sortingOrder = baseOrder[j];
                float w = j == 0 ? HeadW : BodyW;
                int fromEnd = m - 1 - j;
                if (j != 0 && fromEnd < Sub * 2) w *= Mathf.Lerp(0.72f, 1f, fromEnd / (Sub * 2f));
                widthOf[j] = w;
                baseScale[j] = w / sr.sprite.bounds.size.x;
                go.transform.localScale = Vector3.one * baseScale[j];
                pieces.Add(go.transform); renderers.Add(sr);
            }
            headOrderBase = 10 + m;

            var eg = new GameObject("Eyes");
            eg.transform.SetParent(transform, false);
            eyes = eg.transform;
            eyeScale = 1f;
            eyeDia = 0.34f; eyeOff = 0.24f; pupilDia = eyeDia * 0.52f;
            var eyeSprite = Art.Get("eye_single");
            for (int k = 0; k < 2; k++)
            {
                var ebg = new GameObject("Eyeball" + k);
                ebg.transform.SetParent(eg.transform, false);
                ebg.transform.localPosition = new Vector3(k == 0 ? -eyeOff : eyeOff, 0, 0);
                ebg.transform.localScale = Vector3.one * (eyeDia / eyeSprite.bounds.size.x);
                eyeSr[k] = ebg.AddComponent<SpriteRenderer>();
                eyeSr[k].sprite = eyeSprite;
                eyeSr[k].sortingOrder = headOrderBase + 4;
            }
            for (int k = 0; k < 2; k++)
            {
                var pg = new GameObject("Pupil" + k);
                pg.transform.SetParent(eg.transform, false);
                var psr = pg.AddComponent<SpriteRenderer>();
                psr.sprite = Art.Circle; psr.color = new Color(0.07f, 0.05f, 0.12f); psr.sortingOrder = headOrderBase + 5;
                pg.transform.localScale = Vector3.one * pupilDia;
                var hg = new GameObject("Shine");
                hg.transform.SetParent(pg.transform, false);
                hg.transform.localPosition = new Vector3(0.2f, 0.22f, 0);
                hg.transform.localScale = Vector3.one * 0.3f;
                var hsr = hg.AddComponent<SpriteRenderer>();
                hsr.sprite = Art.Circle; hsr.color = Color.white; hsr.sortingOrder = headOrderBase + 6;
                pupils[k] = pg.transform; pupilSr[k] = psr; shineSr[k] = hsr;
            }

            var sg = new GameObject("Snow");
            sg.transform.SetParent(transform, false);
            snow = sg.AddComponent<SpriteRenderer>();
            snow.sprite = Art.Get("icon_snow");
            snow.sortingOrder = headOrderBase + 8;
            sg.transform.localScale = Vector3.one * 0.45f;
            snow.enabled = false;

            if (d.drill)
            {
                var dg = new GameObject("Drill");
                dg.transform.SetParent(transform, false);
                drillSr = dg.AddComponent<SpriteRenderer>();
                drillSr.sprite = Art.Get("icon_drill");
                drillSr.sortingOrder = headOrderBase + 3;
                dg.transform.localScale = Vector3.one * 0.3f;
                drillIcon = dg.transform;
            }

            BuildPath();
            Place(0f);
            LookForward();
            SetFrozen(d.thaw, false);
        }

        void BuildPath()
        {
            path = new List<Vector3>();
            for (int i = cells.Count - 1; i >= 0; i--) path.Add(board.CellToWorld(cells[i]));
            foreach (var c in board.data.R.Forward(cells[0], def.Dir)) path.Add(board.CellToWorld(c));
            tele = new bool[path.Count];
            for (int i = 0; i < path.Count - 1; i++)
                if (Vector3.Distance(path[i], path[i + 1]) > 1.6f) { tele[i] = true; hasTele = true; }
            endDir = DirVec;
            if (path.Count >= 2) { var ev = path[path.Count - 1] - path[path.Count - 2]; if (ev.magnitude > 0.1f && ev.magnitude < 1.6f) endDir = ev.normalized; }
        }

        // u is a position along the track in cell units; beyond the last cell it continues straight.
        Vector3 PathAt(float u, out float vis)
        {
            vis = 1f;
            int last = path.Count - 1;
            if (u >= last) return path[last] + endDir * (u - last);
            u = Mathf.Max(0f, u);
            int a = Mathf.FloorToInt(u); int b = Mathf.Min(a + 1, last);
            float f = u - a;
            if (hasTele && tele[a])
            {
                if (f < 0.5f) { vis = 1f - 2f * f; return path[a]; }
                vis = 2f * f - 1f; return path[b];
            }
            return Vector3.LerpUnclamped(path[a], path[b], f);
        }

        // s = how many cells the whole snake has travelled along its own track
        public void Place(float s)
        {
            int n = cells.Count;
            bool bridges = board.data.HasBridge || board.data.HasRoad;
            for (int j = 0; j < pieces.Count; j++)
            {
                float u = (n - 1) - j / (float)Sub + s;
                var pos = PathAt(u, out float vis);
                if (bridges) UpdateBridge(j, u, ref pos);
                pieces[j].position = pos;
                if (hasTele || bridges) pieces[j].localScale = Vector3.one * (baseScale[j] * vis * bulgeNow);
            }
            float uh = (n - 1) + s; float vv;
            var tv = PathAt(uh + 0.15f, out vv) - PathAt(uh - 0.15f, out vv);
            Vector3 hd = (tv.sqrMagnitude > 1e-4f && tv.magnitude < 0.9f) ? tv.normalized : (uh >= path.Count - 1 ? endDir : DirVec);
            eyes.position = pieces[0].position + hd * 0.06f;
            eyes.rotation = Quaternion.Euler(0, 0, Mathf.Abs(hd.x) > Mathf.Abs(hd.y) ? 90f : 0f);
            var q = new Vector2Int(Mathf.RoundToInt(hd.x), Mathf.RoundToInt(hd.y));
            if (q != lastQ) { lastQ = q; curDir = hd; if (spin == null) LookForward(); }
            snow.transform.position = pieces[0].position;
            if (drillIcon != null)
            {
                drillIcon.position = pieces[0].position + hd * 0.5f;
                drillIcon.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(hd.y, hd.x) * Mathf.Rad2Deg);
            }
        }

        float bulgeNow = 1f;

        // Roads are ground terrain: any snake piece on a road is lifted and bulged (smoothly ramping in and out).
        // Bridges have two levels: pieces moving ALONG the deck go over it (bulged, drawn above), pieces crossing go UNDER it.
        void UpdateBridge(int j, float u, ref Vector3 pos)
        {
            bulgeNow = 1f;
            var data = board.data;
            var c0 = board.WorldToCell(pos);
            float hb = 0f, hr = 0f; bool under = false;
            if (data.HasBridge)
            {
                float v;
                var t = PathAt(u + 0.1f, out v) - PathAt(u - 0.1f, out v);
                int travel = Mathf.Abs(t.x) > Mathf.Abs(t.y) ? 1 : 2;
                var along = travel == 1 ? Vector2Int.right : Vector2Int.up;
                for (int k = -1; k <= 1; k++)
                {
                    var bc = c0 + along * k;
                    if (!data.bridges.Contains(bc)) continue;
                    var cw = board.CellToWorld(bc);
                    float dAlong = travel == 1 ? Mathf.Abs(pos.x - cw.x) : Mathf.Abs(pos.y - cw.y);
                    float dPerp = travel == 1 ? Mathf.Abs(pos.y - cw.y) : Mathf.Abs(pos.x - cw.x);
                    if (dPerp > 0.45f) continue;
                    int deck; if (!data.bridgeAxis.TryGetValue(bc, out deck)) deck = 1;
                    if (deck == travel) hb = Mathf.Max(hb, Mathf.Clamp01(1f - (dAlong - 0.42f) / 0.38f));
                    else if (dAlong < 0.5f) under = true;
                }
            }
            bool onRoad = false;
            if (data.HasRoad)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var rc = c0 + new Vector2Int(dx, dy);
                        if (!data.roads.Contains(rc)) continue;
                        var cw = board.CellToWorld(rc);
                        float dd = Mathf.Max(Mathf.Abs(pos.x - cw.x), Mathf.Abs(pos.y - cw.y));
                        hr = Mathf.Max(hr, Mathf.Clamp01((0.95f - dd) / 0.4f));
                        if (dd <= 0.55f) onRoad = true;
                    }
            float h = Mathf.Max(hb, hr);
            if (h > 0.001f)
            {
                bulgeNow = 1f + 0.2f * h;
                pos += new Vector3(0f, 0.07f * h, 0f);
            }
            // riding ALONG a road is drawn above the road picture (order 500); crossing UNDER it is drawn below, so the road hides that part
            bool isOver = !under && (hb > 0.45f || onRoad);
            int bonus = isOver ? OverBonus : 0;
            int ord = under ? 1 : baseOrder[j] + bonus;
            if (lastOrder[j] != ord)
            {
                lastOrder[j] = ord; over[j] = isOver;
                renderers[j].sortingOrder = ord;
                if (j == 0)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        eyeSr[k].sortingOrder = under ? 2 : headOrderBase + 4 + bonus;
                        pupilSr[k].sortingOrder = under ? 3 : headOrderBase + 5 + bonus;
                        shineSr[k].sortingOrder = under ? 4 : headOrderBase + 6 + bonus;
                    }
                    snow.sortingOrder = under ? 4 : headOrderBase + 8 + bonus;
                    if (drillSr != null) drillSr.sortingOrder = under ? 3 : headOrderBase + 3 + bonus;
                }
            }
            if (underState[j] != under) { underState[j] = under; ApplyPieceColor(j); }
        }

        void SetPupils(float angleDeg, float frac)
        {
            float R = (eyeDia * 0.5f - pupilDia * 0.5f) * frac;
            float a = angleDeg * Mathf.Deg2Rad;
            for (int k = 0; k < 2; k++)
            {
                float cx = k == 0 ? -eyeOff : eyeOff;
                pupils[k].localPosition = new Vector3(cx + Mathf.Cos(a) * R, Mathf.Sin(a) * R, 0);
            }
        }

        void LookForward()
        {
            var local = Quaternion.Inverse(eyes.rotation) * curDir;
            float R = (eyeDia * 0.5f - pupilDia * 0.5f) * 0.45f;
            for (int k = 0; k < 2; k++)
            {
                float cx = k == 0 ? -eyeOff : eyeOff;
                pupils[k].localPosition = new Vector3(cx + local.x * R, local.y * R, 0);
            }
        }

        // dead = pupils sit in a corner and circle clockwise (dizzy eyes)
        public void SetDead(bool dead)
        {
            if (spin != null) { spin.Kill(); spin = null; }
            if (dead)
            {
                spin = DOVirtual.Float(45f, 45f - 360f, 0.5f, a => SetPupils(a, 1f))
                    .SetEase(Ease.Linear).SetLoops(3, LoopType.Restart).SetTarget(transform)
                    .OnComplete(() => { spin = null; LookForward(); });
            }
            else LookForward();
        }

        void RecalcScale(int j)
        {
            baseScale[j] = widthOf[j] * (iced ? 1.12f : 1f) / renderers[j].sprite.bounds.size.x;
            pieces[j].localScale = Vector3.one * baseScale[j];
        }

        // frozen snakes are drawn with ice blocks instead of a snowflake icon
        public void SetFrozen(int left, bool animate)
        {
            bool was = frozenLeft > 0;
            frozenLeft = left;
            bool now = left > 0;
            snow.enabled = false;
            if (now != was)
            {
                iced = now;
                for (int j = 0; j < pieces.Count; j++)
                {
                    renderers[j].sprite = Art.Get(now ? (j == 0 ? "ice_head" : "ice_body") : (j == 0 ? "snake_head" : "snake_body"));
                    RecalcScale(j);
                }
            }
            RecolorPieces();
            if (was && !now && animate)
            {
                PlayMelt();
                for (int i = 0; i < pieces.Count; i++)
                {
                    pieces[i].DOKill();
                    pieces[i].localScale = Vector3.one * baseScale[i];
                    pieces[i].DOPunchScale(Vector3.one * 0.12f, 0.35f, 6, 0.6f).SetDelay(i * 0.01f);
                }
            }
        }

        // ice melts: the ice blocks shrink and turn to water, drops fall, steam rises, the real snake fades in
        void PlayMelt()
        {
            Sfx.Play("thaw");
            var iceH = Art.Get("ice_head"); var iceB = Art.Get("ice_body");
            for (int j = 0; j < pieces.Count; j++)
            {
                float delay = j * 0.012f;
                var pos = pieces[j].position;
                // ice overlay that melts away
                var og = new GameObject("MeltIce");
                og.transform.position = pos;
                var osr = og.AddComponent<SpriteRenderer>();
                osr.sprite = j == 0 ? iceH : iceB;
                osr.sortingOrder = renderers[j].sortingOrder + 1;
                float full = widthOf[j] * 1.12f / osr.sprite.bounds.size.x;
                og.transform.localScale = Vector3.one * full;
                osr.color = Color.Lerp(Color.white, color, 0.3f);
                og.transform.DOScale(full * 0.45f, 0.9f).SetDelay(delay).SetEase(Ease.InQuad);
                osr.DOColor(new Color(0.65f, 0.86f, 1f, 0f), 0.9f).SetDelay(delay).SetEase(Ease.InQuad);
                Object.Destroy(og, 1.6f + delay);
                // real snake colour fades in under it
                var bcj = BaseC(j); renderers[j].color = new Color(bcj.r, bcj.g, bcj.b, 0f);
                renderers[j].DOFade(1f, 0.6f).SetDelay(0.25f + delay);
                // water drops + puddle + steam
                if (j % 3 == 0)
                {
                    var dg = new GameObject("MeltDrop");
                    dg.transform.position = pos + new Vector3(Random.Range(-0.1f, 0.1f), 0, 0);
                    var dsr = dg.AddComponent<SpriteRenderer>();
                    dsr.sprite = Art.Circle; dsr.color = new Color(0.55f, 0.82f, 1f, 0.9f); dsr.sortingOrder = renderers[j].sortingOrder + 2;
                    dg.transform.localScale = Vector3.one * Random.Range(0.07f, 0.12f);
                    dg.transform.DOMoveY(pos.y - Random.Range(0.4f, 0.7f), 0.55f).SetDelay(delay + 0.1f).SetEase(Ease.InQuad);
                    dsr.DOFade(0f, 0.25f).SetDelay(delay + 0.45f);
                    Object.Destroy(dg, 1.5f + delay);
                }
                if (j % 4 == 0)
                {
                    var pg = new GameObject("MeltPuddle");
                    pg.transform.position = pos + new Vector3(0, -0.05f, 0);
                    var psr = pg.AddComponent<SpriteRenderer>();
                    psr.sprite = Art.Glow; psr.sortingOrder = 5; psr.color = new Color(0.55f, 0.8f, 1f, 0f);
                    pg.transform.localScale = Vector3.one * 0.2f;
                    pg.transform.DOScale(Random.Range(0.55f, 0.85f), 0.8f).SetDelay(delay + 0.2f).SetEase(Ease.OutCubic);
                    psr.DOFade(0.5f, 0.4f).SetDelay(delay + 0.2f);
                    psr.DOFade(0f, 0.9f).SetDelay(delay + 1.1f);
                    Object.Destroy(pg, 2.6f + delay);
                }
                if (j % 5 == 0)
                {
                    var sg = new GameObject("MeltSteam");
                    sg.transform.position = pos;
                    var ssr = sg.AddComponent<SpriteRenderer>();
                    ssr.sprite = Art.Glow; ssr.sortingOrder = renderers[j].sortingOrder + 3; ssr.color = new Color(1f, 1f, 1f, 0.45f);
                    sg.transform.localScale = Vector3.one * 0.25f;
                    sg.transform.DOMoveY(pos.y + 0.6f, 1.0f).SetDelay(delay + 0.15f).SetEase(Ease.OutQuad);
                    sg.transform.DOScale(0.6f, 1.0f).SetDelay(delay + 0.15f);
                    ssr.DOFade(0f, 0.9f).SetDelay(delay + 0.3f);
                    Object.Destroy(sg, 1.8f + delay);
                }
            }
        }

        // ---- drilling: the drill bit spins, the whole snake trembles
        public void BeginDrill(float dur)
        {
            if (drillIcon == null) return;
            drillIcon.DOKill();
            float s = drillIcon.localScale.x;
            drillSpin = drillIcon.DOScaleY(s * 0.22f, 0.04f).SetLoops(-1, LoopType.Yoyo);
            transform.DOShakePosition(dur, new Vector3(0.05f, 0.05f, 0f), 50, 90, false, false);
            pieces[0].DOKill();
            pieces[0].localScale = Vector3.one * baseScale[0];
            pieces[0].DOPunchScale(new Vector3(0.1f, 0.1f, 0f), 0.14f, 1, 0f).SetLoops(Mathf.CeilToInt(dur / 0.14f));
        }

        public void EndDrill()
        {
            if (drillSpin != null) { drillSpin.Kill(); drillSpin = null; }
            if (drillIcon != null) { drillIcon.DOKill(); drillIcon.localScale = Vector3.one * 0.3f; }
            pieces[0].DOKill();
            pieces[0].localScale = Vector3.one * baseScale[0];
        }

        // colour of piece j for the current skin: plain colour, rainbow (violet head -> red tail) or zebra stripes
        bool dimmed;
        // tutorial: grey out snakes that are not the one being taught
        public void SetDim(bool d) { if (dimmed == d) return; dimmed = d; RecolorPieces(); }

        Color BaseC(int j)
        {
            int kind = Palette.SkinKind(Save.S.skin);
            int m = Mathf.Max(2, pieces.Count);
            Color c;
            if (kind == 1) c = Color.HSVToRGB(Mathf.Lerp(0.78f, 0f, j / (float)(m - 1)), 0.88f, 1f);
            else if (kind == 2) c = (j < 2 || (j / 2) % 2 == 0) ? new Color(0.08f, 0.08f, 0.1f) : new Color(0.96f, 0.96f, 0.96f);
            else c = color;
            if (frozenLeft > 0) c = Color.Lerp(Color.white, c, 0.3f);
            if (dimmed) { c = Color.Lerp(c, new Color(0.72f, 0.72f, 0.74f, c.a), 0.8f); }
            return c;
        }

        void ApplyPieceColor(int j)
        {
            var t = BaseC(j);
            if (underState != null && underState[j]) t = new Color(t.r * 0.7f, t.g * 0.7f, t.b * 0.75f, t.a);
            if (fadeState != null && fadeState[j]) t.a = 0.62f;          // see-through while riding a bridge so the bridge stays visible
            renderers[j].DOKill();
            renderers[j].color = t;
        }

        void RecolorPieces() { for (int j = 0; j < renderers.Count; j++) ApplyPieceColor(j); }

        public void Recolor(Color c) { color = c; RecolorPieces(); }

        public void ShakeLocked()
        {
            transform.DOKill(true);
            transform.DOShakePosition(0.25f, new Vector3(0.12f, 0f, 0f), 18, 0f, false, true);
        }

        public Tween Slide(float s0, float s1, float dur, Ease ease)
        {
            return DOVirtual.Float(s0, s1, dur, v => Place(v)).SetEase(ease);
        }

        public void PunchHead(Vector2Int dir)
        {
            var h = pieces[0];
            h.DOKill();
            h.localScale = Vector3.one * baseScale[0];
            var sq = dir.x != 0 ? new Vector3(-0.22f, 0.18f, 0) : new Vector3(0.18f, -0.22f, 0);
            h.DOPunchScale(sq, 0.3f, 8, 0.6f);
        }

        // cellIndex = index into cells (0 = head)
        public void PunchPiece(int cellIndex)
        {
            int j = cellIndex * Sub;
            if (j < 0 || j >= pieces.Count) return;
            pieces[j].DOKill();
            pieces[j].localScale = Vector3.one * baseScale[j];
            pieces[j].DOPunchScale(Vector3.one * 0.18f, 0.3f, 7, 0.6f);
        }

        // red -> original colour (DOTween)
        public void FlashRed()
        {
            for (int j = 0; j < renderers.Count; j++)
            {
                var r = renderers[j];
                r.DOKill();
                r.color = new Color(1f, 0.12f, 0.12f);
                r.DOColor(BaseC(j), 0.7f).SetEase(Ease.OutQuad);
            }
        }

        public void FlashWhite()
        {
            foreach (var r in renderers)
            {
                r.DOKill();
                var c = r.color;
                r.color = Color.white;
                r.DOColor(c, 0.4f).SetEase(Ease.OutQuad);
            }
            pieces[0].DOKill();
            pieces[0].localScale = Vector3.one * baseScale[0];
            pieces[0].DOPunchScale(Vector3.one * 0.2f, 0.4f, 6, 0.6f);
        }

        public Vector3 HeadPos => pieces[0].position;

        public Sequence PopIn()
        {
            var seq = DOTween.Sequence();
            for (int i = 0; i < pieces.Count; i++)
            {
                pieces[i].localScale = Vector3.zero;
                seq.Insert(i * 0.006f, pieces[i].DOScale(baseScale[i], 0.25f).SetEase(Ease.OutBack));
            }
            eyes.localScale = Vector3.zero;
            seq.Insert(0.1f, eyes.DOScale(eyeScale, 0.25f).SetEase(Ease.OutBack));
            return seq;
        }

        public Sequence SuckOut()
        {
            var seq = DOTween.Sequence();
            var target = HeadPos;
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                int k = pieces.Count - 1 - i;
                seq.Insert(k * 0.008f, pieces[i].DOMove(target, 0.28f).SetEase(Ease.InBack));
                seq.Insert(k * 0.008f, pieces[i].DOScale(0f, 0.28f).SetEase(Ease.InBack));
            }
            seq.Insert(0.1f, eyes.DOScale(0f, 0.2f));
            return seq;
        }

        public void ResetVisual()
        {
            DOTween.Kill(transform);
            for (int i = 0; i < pieces.Count; i++) { pieces[i].DOKill(); pieces[i].localScale = Vector3.one * baseScale[i]; }
            eyes.localScale = Vector3.one * eyeScale;
            Place(0f);
            SetDead(false);
        }

        public Vector3 DirVec => new Vector3(def.Dir.x, def.Dir.y, 0);
    }
}
