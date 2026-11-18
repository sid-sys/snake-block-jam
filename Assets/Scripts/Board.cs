using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SnakeJam
{
    public class Board : MonoBehaviour
    {
        public enum Mode { Normal, Vacuum }

        public LevelData data;
        public Mode mode = Mode.Normal;
        public bool inputLocked;
        public int bumps, taps;
        public int movesLeft;
        public int Remaining => active.Count;
        public int Total { get; private set; }
        public bool Won => won;
        public float Zoom => zoom;
        public bool NeedsZoom { get; private set; }   // board too big to show with big cells: starts zoomed in, zoom buttons are shown

        public event Action OnWin;                 // board is empty
        public event Action OnChanged;
        public event Action OnOutOfMoves;
        public event Action<string> OnPraise;
        public event Action OnFirstEscape;
        public event Action OnHit;
        public event Action<int> OnFrozenTap;
        public event Action<bool> OnVacuumMode;

        readonly List<SnakeView> active = new List<SnakeView>();
        readonly Dictionary<long, SnakeView> occ = new Dictionary<long, SnakeView>();
        readonly Stack<SnakeView> history = new Stack<SnakeView>();
        readonly HashSet<Vector2Int> liveRocks = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, GameObject> rockObjs = new Dictionary<Vector2Int, GameObject>();
        Transform root;
        Camera cam;
        int escaped, combo;
        bool won, outFired;

        // camera / zoom
        float baseOrtho = 6f, zoom = 1f, minZoom = 1f, aspect = 0.5625f;
        readonly List<SpriteRenderer> dotSr = new List<SpriteRenderer>();
        readonly Dictionary<Vector2Int, SpriteRenderer> lightObjs = new Dictionary<Vector2Int, SpriteRenderer>();
        Vector3 basePos;
        Vector2 pan;
        Transform rig;
        // pointer state
        bool pressing, dragged, overUI;
        Vector2 downPos, lastPos;
        float pinchPrev, noTapUntil;

        public bool CanUndo => history.Count > 0 && !won;

        public void Init(Camera c)
        {
            cam = c;
            rig = cam.transform.parent != null ? cam.transform.parent : cam.transform;
            root = new GameObject("BoardRoot").transform;
            root.SetParent(transform, false);
            Fx.Root = root;
        }

        public void SetVisible(bool on) { if (root != null) root.gameObject.SetActive(on); }

        public Vector3 CellToWorld(Vector2Int c)
        {
            return new Vector3(c.x - (data.w - 1) / 2f, c.y - (data.h - 1) / 2f, 0f);
        }

        public Vector2Int WorldToCell(Vector3 p)
        {
            return new Vector2Int(Mathf.RoundToInt(p.x + (data.w - 1) / 2f), Mathf.RoundToInt(p.y + (data.h - 1) / 2f));
        }

        // ------------------------------------------------------------------ load
        static long KeyOf(Vector2Int c, int axis) => Rules.Key(c, axis);

        void AddOcc(SnakeView sv)
        {
            for (int i = 0; i < sv.cells.Count; i++) occ[KeyOf(sv.cells[i], data.R.CellAxis(sv.cells, i))] = sv;
        }
        void RemoveOcc(SnakeView sv)
        {
            for (int i = 0; i < sv.cells.Count; i++) occ.Remove(KeyOf(sv.cells[i], data.R.CellAxis(sv.cells, i)));
        }

        SpriteRenderer MakeProp(string sprite, Vector2Int cell, float width, int order, Color? col = null)
        {
            var go = new GameObject(sprite);
            go.transform.SetParent(root, false);
            go.transform.localPosition = CellToWorld(cell);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Art.Get(sprite);
            sr.sortingOrder = order;
            if (col.HasValue) sr.color = col.Value;
            float s = width / sr.sprite.bounds.size.x;
            go.transform.localScale = Vector3.one * s;
            return sr;
        }

        public void Load(LevelData d)
        {
            DOTween.KillAll();
            foreach (Transform t in root) Destroy(t.gameObject);
            active.Clear(); occ.Clear(); history.Clear(); liveRocks.Clear(); rockObjs.Clear(); dotSr.Clear(); lightObjs.Clear();
            d.R.redLights = new HashSet<Vector2Int>(d.lightsRed);
            data = d; escaped = 0; combo = 0; bumps = 0; taps = 0; won = false; outFired = false; inputLocked = false;
            movesLeft = Save.S.adminUnlimited ? 9999 : d.moves;
            zoom = 1f; pan = Vector2.zero; pressing = false; pinchPrev = 0f;
            SetMode(Mode.Normal);

            // dot grid: shows only where no snake covers the cell
            var dotCol = DotColor();
            for (int x = 0; x < d.w; x++)
                for (int y = 0; y < d.h; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!d.mask[x, y] || d.walls.Contains(cell) || d.rocks.Contains(cell) || d.bridges.Contains(cell) || d.portals.ContainsKey(cell) || d.lights.Contains(cell)) continue;
                    var go = new GameObject("Dot");
                    go.transform.SetParent(root, false);
                    go.transform.localPosition = CellToWorld(cell);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = Art.Circle; sr.color = dotCol; sr.sortingOrder = 0; dotSr.Add(sr);
                    go.transform.localScale = Vector3.zero;
                    go.transform.DOScale(0.15f, 0.3f).SetEase(Ease.OutBack).SetDelay((x + y) * 0.01f);
                }

            foreach (var c in d.walls) MakeProp("wall_block", c, 0.94f, 4);
            foreach (var c in d.rocks) { rockObjs[c] = MakeProp("rock_block", c, 0.94f, 4).gameObject; liveRocks.Add(c); }
            foreach (var c in d.lights) lightObjs[c] = MakeProp(d.lightsRed.Contains(c) ? "light_red" : "light_green", c, 1.0f, 3);
            // 3D ground roads (snakes ride over them) - tile shape depends on which neighbours are road
            foreach (var c in d.roads)
            {
                if (d.pieceCells.Contains(c)) continue;
                int mk = (d.roads.Contains(c + Vector2Int.up) ? 1 : 0) | (d.roads.Contains(c + Vector2Int.right) ? 2 : 0)
                       | (d.roads.Contains(c + Vector2Int.down) ? 4 : 0) | (d.roads.Contains(c + Vector2Int.left) ? 8 : 0);
                MakeProp("road_" + mk, c, 1.0f, 2);
            }
            // road pieces: one picture per piece, fitted to its cells; snakes (order 10+) are drawn above it, snakes under an underpass below it
            foreach (var piece in d.pieces)
            {
                var go = new GameObject(piece.sprite);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(piece.center.x - (d.w - 1) / 2f, piece.center.y - (d.h - 1) / 2f, 0f);
                go.transform.localRotation = Quaternion.Euler(0, 0, piece.rot * 90f);
                var ch = new GameObject("Picture");
                ch.transform.SetParent(go.transform, false);
                var psr = ch.AddComponent<SpriteRenderer>();
                psr.sprite = Art.Get(piece.sprite); psr.sortingOrder = 500;
                var sz = psr.sprite.bounds.size;
                ch.transform.localScale = new Vector3(piece.w / sz.x, (piece.h / (1f - piece.wf)) / sz.y, 1f);
                ch.transform.localPosition = new Vector3(0f, -piece.h * piece.wf / (1f - piece.wf) / 2f, 0f);
            }
            // bridges: lavender barrel - snakes along it go over, snakes across it go under
            foreach (var c in d.bridges)
            {
                if (d.roads.Contains(c)) continue;
                int ax = d.bridgeAxis.TryGetValue(c, out var a0) ? a0 : 1;
                var rs = MakeProp("bridge_deck", c, 1.06f, 1000);
                if (ax == 2) rs.transform.rotation = Quaternion.Euler(0, 0, 90f);
            }
            Color[] pc = { new Color(0.45f, 0.7f, 1f), new Color(1f, 0.62f, 0.3f), new Color(0.6f, 1f, 0.6f) };
            for (int i = 0; i < d.portalFirst.Count; i++)
            {
                var a = d.portalFirst[i]; var b = d.portals[a];
                foreach (var c in new[] { a, b })
                {
                    var sr = MakeProp("portal", c, 1.0f, 3, pc[i % pc.Length]);
                    sr.transform.DOLocalRotate(new Vector3(0, 0, -360f), 4f, RotateMode.FastBeyond360).SetEase(Ease.Linear).SetLoops(-1);
                }
            }

            var pal = Palette.Skin(Save.S.skin);
            for (int i = 0; i < d.snakes.Count; i++)
            {
                var sd = d.snakes[i];
                var go = new GameObject("Snake");
                go.transform.SetParent(root, false);
                var sv = go.AddComponent<SnakeView>();
                sv.Init(this, sd, pal[sd.colorIdx % pal.Length], i);
                active.Add(sv);
                AddOcc(sv);
            }
            FitCamera();
            StartIntro();
            Total = active.Count;
            RefreshFrozen(false);
            OnChanged?.Invoke();
        }

        // snakes crawl in from outside the screen along their own track and settle into the grid
        void StartIntro()
        {
            inputLocked = true;
            float longest = 0f;
            for (int i = 0; i < active.Count; i++)
            {
                var sv = active[i];
                float dist = 10f + sv.Length;
                float delay = 0.05f + (i % 6) * 0.04f + UnityEngine.Random.value * 0.1f;
                float dur = Mathf.Min(0.9f, 0.45f + dist * 0.02f);
                sv.busy = true;
                var local = sv;
                local.Place(dist);
                if (i % 3 == 0) DOVirtual.DelayedCall(delay, () => Sfx.Play("slide", 0.12f, UnityEngine.Random.Range(0.9f, 1.2f)));
                var tw = local.Slide(dist, 0f, dur, Ease.OutCubic).SetDelay(delay);
                tw.OnComplete(() => local.busy = false);
                longest = Mathf.Max(longest, delay + dur);
            }
            DOVirtual.DelayedCall(longest + 0.05f, () => { inputLocked = false; });
        }

        // ------------------------------------------------------------------ camera / zoom
        void FitCamera()
        {
            aspect = (float)UnityEngine.Screen.width / Mathf.Max(1, UnityEngine.Screen.height);
            // the whole board always fits between the top bar and the bottom bar, with a margin on every side
            // canvas is 1080 units wide (match width), so its height is 1080 / aspect. The board gets the free rectangle
            // between the top bar (resTop) and the bottom bar (resBot), centred in it.
            float Hc = 1080f / aspect;
            float minW = 6f / (2f * aspect);
            float byW = (data.w + 0.8f) / (2f * aspect * (1020f / 1080f));
            float bot = resBot;
            float full = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                float avail = Mathf.Max(300f, Hc - resTop - bot);
                float byH = (data.h + 0.8f) * Hc / (2f * avail);
                full = Mathf.Max(byH, byW, minW);
                // a cell must stay big enough to tap: below MinCell canvas units the map starts zoomed in and the player pans / zooms
                float startOrtho = Hc / (2f * MinCell);
                NeedsZoom = full > startOrtho + 0.001f;
                if (pass == 0 && NeedsZoom) bot = resBot + resZoomRow;   // the zoom buttons take a row above the bottom bar
                else break;
            }
            if (NeedsZoom)
            {
                baseOrtho = Hc / (2f * MinCell);
                minZoom = baseOrtho / full;
            }
            else { baseOrtho = full; minZoom = 1f; }
            shiftCanvas = (bot - resTop) * 0.5f;   // how far (canvas units) the board centre sits above the screen centre
            ApplyCamera();
        }

        const float MinCell = 70f;   // smallest cell (canvas units, 1080 wide) at the start of a level: about 47 px on a 720 px wide phone

        // set by the HUD: canvas units kept free at the top, at the bottom, and for the zoom row on giant maps
        public float resTop = 200f, resBot = 240f, resZoomRow = 130f;
        float shiftCanvas;

        void ApplyCamera()
        {
            zoom = Mathf.Clamp(zoom, minZoom, 4f);
            float ortho = baseOrtho / zoom;
            cam.orthographic = true;
            cam.orthographicSize = ortho;
            basePos = new Vector3(0, -shiftCanvas * 2f * ortho / (1080f / aspect), rig == cam.transform ? -10f : 0f);
            float limX = Mathf.Max(0f, data.w * 0.5f + 0.8f - ortho * aspect);
            float limY = Mathf.Max(0f, data.h * 0.5f + 0.8f - ortho);
            pan.x = Mathf.Clamp(pan.x, -limX, limX);
            pan.y = Mathf.Clamp(pan.y, -limY, limY);
            if (zoom <= minZoom + 0.001f) pan = Vector2.zero;
            rig.position = basePos + new Vector3(pan.x, pan.y, 0);
        }

        public void ZoomBy(float factor) { zoom *= factor; ApplyCamera(); }
        public void ResetZoom() { zoom = minZoom; pan = Vector2.zero; ApplyCamera(); }

        void PanScreen(Vector2 deltaPx)
        {
            float worldPerPx = (2f * cam.orthographicSize) / Mathf.Max(1, UnityEngine.Screen.height);
            pan -= deltaPx * worldPerPx;
            ApplyCamera();
        }

        // ------------------------------------------------------------------ input
        void Update()
        {
            if (won || data == null || !root.gameObject.activeInHierarchy) return;

            // zoom: mouse wheel
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float sc = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
                {
                    zoom *= 1f + Mathf.Clamp(sc / 120f, -1f, 1f) * 0.12f;
                    ApplyCamera();
                }
            }
            // zoom: two finger pinch
            var ts = Touchscreen.current;
            if (ts != null)
            {
                int n = 0; Vector2 p0 = Vector2.zero, p1 = Vector2.zero;
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    if (n == 0) p0 = t.position.ReadValue(); else if (n == 1) p1 = t.position.ReadValue();
                    n++;
                }
                if (n >= 2)
                {
                    float dist = Vector2.Distance(p0, p1);
                    if (pinchPrev > 1f && dist > 1f) { zoom *= dist / pinchPrev; ApplyCamera(); }
                    pinchPrev = dist;
                    pressing = false; noTapUntil = Time.time + 0.3f;
                    return;
                }
                pinchPrev = 0f;
            }

            var ptr = Pointer.current;
            if (ptr == null) return;
            if (ptr.press.wasPressedThisFrame)
            {
                pressing = true; dragged = false;
                downPos = lastPos = ptr.position.ReadValue();
                overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            }
            if (pressing && ptr.press.isPressed)
            {
                var cur = ptr.position.ReadValue();
                if (!overUI && zoom > minZoom + 0.01f && (dragged || (cur - downPos).magnitude > 18f))
                {
                    dragged = true;
                    PanScreen(cur - lastPos);
                }
                lastPos = cur;
            }
            if (pressing && ptr.press.wasReleasedThisFrame)
            {
                pressing = false;
                if (!overUI && !dragged && !inputLocked && Time.time > noTapUntil) TapAt(downPos);
            }
        }

        void TapAt(Vector2 screenPos)
        {
            var wp = cam.ScreenToWorldPoint(screenPos);
            wp.z = 0;
            var c = WorldToCell(wp);
            // a snake body on a bridge may be on either axis: prefer the one nearest the finger
            SnakeView sv;
            if (data.bridges.Contains(c))
            {
                var cw = CellToWorld(c);
                bool wantH = Mathf.Abs(wp.x - cw.x) > Mathf.Abs(wp.y - cw.y);
                if (!occ.TryGetValue(KeyOf(c, wantH ? 1 : 2), out sv) && !occ.TryGetValue(KeyOf(c, wantH ? 2 : 1), out sv)) return;
            }
            else if (!occ.TryGetValue(KeyOf(c, 0), out sv)) return;
            Tap(sv);
        }

        public void Tap(SnakeView sv)
        {
            if (sv.busy || movesLeft <= 0) return;
            if (mode == Mode.Vacuum)
            {
                if (Save.S.vacuums <= 0) { SetMode(Mode.Normal); return; }
                Save.S.vacuums--; Save.Commit();
                SetMode(Mode.Normal);
                Remove(sv, true, null);
                return;
            }
            if (sv.frozenLeft > 0)
            {
                Sfx.Play("locked");
                sv.ShakeLocked();
                OnFrozenTap?.Invoke(sv.frozenLeft);
                return;
            }
            Sfx.Play("tap");
            var ray = CastFor(sv);
            taps++;
            movesLeft--;
            if (ray.exits) Remove(sv, false, ray);
            else Bump(sv, ray);
            ToggleLights();
            CheckOutOfMoves();
        }

        Ray CastFor(SnakeView sv)
        {
            return data.R.Cast(sv.def, k => occ.TryGetValue(k, out var v) ? v.id : -1, sv.id, liveRocks);
        }

        static Color DotColor() => Save.S.theme == 3 ? new Color(0.75f, 0.8f, 1f, 0.5f) : Palette.DotLight;

        // every tap flips all traffic lights
        void ToggleLights()
        {
            if (data.lights.Count == 0) return;
            var red = data.R.redLights;
            Sfx.Play("light");
            foreach (var c in data.lights)
            {
                bool nowRed = !red.Contains(c);
                if (nowRed) red.Add(c); else red.Remove(c);
                if (lightObjs.TryGetValue(c, out var sr) && sr != null)
                {
                    sr.sprite = Art.Get(nowRed ? "light_red" : "light_green");
                    sr.transform.DOComplete();
                    sr.transform.DOPunchScale(Vector3.one * 0.12f, 0.3f, 6, 0.6f);
                }
            }
        }

        // instant preview: re-colour dots and every snake with the current theme / skin
        public void ApplyLook()
        {
            var dc = DotColor();
            foreach (var d in dotSr) if (d != null) d.color = dc;
            var pal = Palette.Skin(Save.S.skin);
            foreach (var sv in active) sv.Recolor(pal[sv.def.colorIdx % pal.Length]);
            foreach (var sv in history) sv.Recolor(pal[sv.def.colorIdx % pal.Length]);
        }

        void CheckOutOfMoves()
        {
            if (movesLeft > 0 || outFired) return;
            outFired = true;
            DOVirtual.DelayedCall(1.3f, () =>
            {
                if (!won && occ.Count > 0 && movesLeft <= 0) OnOutOfMoves?.Invoke();
                else outFired = false;
            });
        }

        public void AddMoves(int n)
        {
            movesLeft += n; outFired = false;
            OnChanged?.Invoke();
        }

        void Bump(SnakeView sv, Ray ray)
        {
            bumps++; combo = 0;
            sv.busy = true;
            var dir = sv.def.Dir;
            int free = ray.free;
            SnakeView blocker = null; int bi = 0;
            if (ray.blockType == 0 && occ.TryGetValue(ray.blockKey, out blocker)) bi = blocker.cells.IndexOf(ray.fwd[free]);
            var seq = DOTween.Sequence();
            if (free > 0)
            {
                seq.AppendCallback(() => Sfx.Play("slide", 0.5f));
                seq.Append(sv.Slide(0f, free, 0.03f * Mathf.Min(free, 8) + 0.04f, Ease.OutQuad));
            }
            seq.AppendCallback(() =>
            {
                Sfx.Play("bump", 0.9f, UnityEngine.Random.Range(0.9f, 1.1f));
                Sfx.Play("thud", 0.5f);
                FeelFx.Hit();   // Feel: freeze for a blink + colour-split pulse (the phone buzz comes with the "bump" sound)
                sv.SetDead(true);
                sv.FlashRed();
                OnHit?.Invoke();
                sv.PunchHead(dir);
                if (blocker != null) blocker.PunchPiece(bi);
                var hit = sv.HeadPos + new Vector3(dir.x, dir.y, 0) * 0.55f;
                Fx.Burst(hit, new Color(1f, 1f, 1f, 0.9f), 7, 0.55f, 0.4f, 0.12f);
                Fx.Shake(0.25f, 0.35f);
            });
            seq.AppendInterval(0.45f);
            seq.Append(sv.Slide(free, 0f, 0.06f * Mathf.Min(free, 8) + 0.15f, Ease.OutBack));
            seq.OnComplete(() => { sv.busy = false; });
            OnChanged?.Invoke();
        }

        void Remove(SnakeView sv, bool vacuum, Ray ray)
        {
            sv.busy = true;
            RemoveOcc(sv);
            active.Remove(sv);
            history.Push(sv);
            escaped++;
            bool first = escaped == 1;
            if (!vacuum) combo++;
            RefreshFrozen(true);
            OnChanged?.Invoke();
            if (first) OnFirstEscape?.Invoke();

            if (vacuum)
            {
                Sfx.Play("vacuum");
                Fx.Burst(sv.HeadPos, new Color(0.7f, 0.5f, 1f), 10, 0.9f, 0.45f, 0.14f);
                sv.SuckOut().OnComplete(() => { sv.gameObject.SetActive(false); AfterRemove(); });
                return;
            }

            Sfx.Play("slide");
            Sfx.Play("escape", 0.7f);
            // keep going until the whole snake is off the screen
            int margin = Mathf.CeilToInt(cam.orthographicSize * Mathf.Max(1f, cam.aspect)) + 3;
            float steps = ray.fwd.Count + margin + sv.Length;
            float lastSpark = 0f;
            var col = sv.color;
            float boardLimit = ray.fwd.Count + sv.Length;
            TweenCallback<float> tick = v =>
            {
                sv.Place(v);
                if (v < boardLimit && Time.time - lastSpark > 0.05f)
                {
                    lastSpark = Time.time;
                    Fx.Burst(sv.HeadPos, Color.Lerp(col, Color.white, 0.5f), 1, 0.3f, 0.35f, 0.1f);
                }
            };
            var seq = DOTween.Sequence();
            if (ray.hasRock)
            {
                var rockCell = ray.rock;
                float approach = ray.fwd.IndexOf(rockCell);          // head stops right in front of the rock
                if (approach > 0.01f) seq.Append(DOVirtual.Float(0f, approach, approach / 16f + 0.15f, tick).SetEase(Ease.OutQuad));
                seq.AppendCallback(() => BeginDrillFx(sv, rockCell));
                seq.AppendInterval(0.9f);
                seq.AppendCallback(() => { sv.EndDrill(); BreakRock(rockCell); });
                seq.Append(DOVirtual.Float(approach, steps, Mathf.Clamp((steps - approach) / ExitSpeed, 0.45f, 2.6f), tick).SetEase(Ease.InSine));
            }
            else seq.Append(DOVirtual.Float(0f, steps, Mathf.Clamp(steps / ExitSpeed, 0.45f, 2.6f), tick).SetEase(Ease.InSine));
            seq.OnComplete(() =>
            {
                sv.gameObject.SetActive(false);
                AfterRemove();
            });

            if (combo >= 2 && combo % 2 == 0)
            {
                string[] w = combo >= 6 ? new[] { "Awesome!", "Perfect!" } : (combo >= 4 ? new[] { "Amazing!" } : new[] { "Great!" });
                OnPraise?.Invoke(w[UnityEngine.Random.Range(0, w.Length)]);
            }
        }

        const float ExitSpeed = 24f;   // cells per second a free snake crawls out (default speed)

        static readonly Color DustCol =new Color(0.74f, 0.62f, 0.48f, 1f);

        void BeginDrillFx(SnakeView sv, Vector2Int c)
        {
            Sfx.Play("drill");
            sv.BeginDrill(0.9f);
            Fx.Shake(0.1f, 0.9f);
            if (rockObjs.TryGetValue(c, out var go) && go != null)
            {
                go.transform.DOShakePosition(0.9f, new Vector3(0.05f, 0.05f, 0f), 45, 90, false, false);
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr != null) sr.DOColor(new Color(0.62f, 0.5f, 0.45f), 0.9f);
            }
            var pos = CellToWorld(c);
            var seq = DOTween.Sequence();
            for (int i = 0; i < 7; i++)
                seq.AppendCallback(() => Fx.Burst(pos + new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(-0.3f, 0.3f), 0), DustCol, 3, 0.7f, 0.5f, 0.28f, Art.Glow, 8)).AppendInterval(0.12f);
        }

        // the rock breaks into pieces, dust flies, a little rubble and dust stay for the whole level
        void BreakRock(Vector2Int c)
        {
            liveRocks.Remove(c);
            Sfx.Play("rock");
            FeelFx.RockBreak();
            Sfx.Play("thud", 0.9f, 1.3f);
            Sfx.Play("bump", 0.6f, 0.7f);
            var pos = CellToWorld(c);
            if (rockObjs.TryGetValue(c, out var go) && go != null) { go.transform.DOKill(); Destroy(go); }
            rockObjs.Remove(c);
            Fx.Shake(0.3f, 0.3f);

            var chunkSprite = Art.Get("rock_chunk");
            for (int i = 0; i < 11; i++)
            {
                var ch = new GameObject("RockPiece");
                ch.transform.SetParent(root, false);
                ch.transform.position = pos;
                var sr = ch.AddComponent<SpriteRenderer>();
                sr.sprite = chunkSprite; sr.sortingOrder = 9;
                sr.color = Color.Lerp(Color.white, new Color(0.85f, 0.7f, 0.55f), UnityEngine.Random.value);
                bool stays = i < 4;                                   // these pieces stay on the floor as rubble
                float w = stays ? UnityEngine.Random.Range(0.12f, 0.2f) : UnityEngine.Random.Range(0.18f, 0.34f);
                ch.transform.localScale = Vector3.one * (w / chunkSprite.bounds.size.x);
                ch.transform.rotation = Quaternion.Euler(0, 0, UnityEngine.Random.Range(0f, 360f));
                var target = pos + (Vector3)(UnityEngine.Random.insideUnitCircle * (stays ? 0.45f : 1.2f));
                float dur = UnityEngine.Random.Range(0.45f, 0.75f);
                ch.transform.DOJump(target, UnityEngine.Random.Range(0.4f, 0.9f), 1, dur).SetEase(Ease.OutQuad);
                ch.transform.DORotate(new Vector3(0, 0, UnityEngine.Random.Range(-540f, 540f)), dur, RotateMode.FastBeyond360);
                if (!stays)
                {
                    sr.DOFade(0f, 0.4f).SetDelay(dur + 0.3f);
                    Destroy(ch, dur + 1.2f);
                }
            }
            // quick burst of dust
            Fx.Burst(pos, DustCol, 18, 1.4f, 0.8f, 0.4f, Art.Glow, 12);
            // dust that stays and slowly drifts for the whole level
            for (int i = 0; i < 6; i++)
            {
                var d = new GameObject("LingeringDust");
                d.transform.SetParent(root, false);
                var start = pos + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.25f);
                d.transform.position = start;
                var sr = d.AddComponent<SpriteRenderer>();
                sr.sprite = Art.Glow; sr.sortingOrder = 6;
                sr.color = new Color(DustCol.r, DustCol.g, DustCol.b, 0f);
                float size = UnityEngine.Random.Range(0.8f, 1.35f);
                d.transform.localScale = Vector3.one * 0.15f;
                d.transform.DOScale(size, 0.9f).SetEase(Ease.OutCubic);
                sr.DOFade(UnityEngine.Random.Range(0.45f, 0.65f), 0.9f);
                d.transform.DOMove(start + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.18f), UnityEngine.Random.Range(2.5f, 4.5f))
                    .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetDelay(0.9f);
            }
        }

        void AfterRemove()
        {
            if (occ.Count == 0 && !won)
            {
                won = true;
                DOVirtual.DelayedCall(0.25f, () => { Sfx.Play("win"); FeelFx.Win(); OnWin?.Invoke(); });
            }
        }

        void RefreshFrozen(bool animate)
        {
            foreach (var s in active)
            {
                int left = Mathf.Max(0, s.def.thaw - escaped);
                if (left != s.frozenLeft || (left > 0 && !animate)) s.SetFrozen(left, animate);
            }
        }

        // ------------------------------------------------------------------ power ups
        public SnakeView FindHintSnake()
        {
            var cands = new List<SnakeView>();
            foreach (var s in active)
            {
                if (s.frozenLeft > 0 || s.busy) continue;
                if (CastFor(s).exits) cands.Add(s);
            }
            if (cands.Count == 0) return null;
            SnakeView best = cands[0]; int bestScore = -1;
            foreach (var s in cands)
            {
                // prefer the snake that unblocks the most others
                int score = 0;
                foreach (var o in active)
                {
                    if (o == s) continue;
                    var r = CastFor(o);
                    foreach (var c in r.fwd)
                    {
                        if (occ.TryGetValue(KeyOf(c, 0), out var oo) && oo == s) { score++; break; }
                    }
                }
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        public bool UseHint()
        {
            if (won || inputLocked) return false;
            var best = FindHintSnake();
            if (best == null) return false;
            Sfx.Play("hint");
            best.FlashWhite();
            ShowHintArrow(best);
            return true;
        }

        void ShowHintArrow(SnakeView sv)
        {
            var go = new GameObject("HintArrow");
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Art.Get("icon_play"); sr.sortingOrder = 4000;
            var d = sv.def.Dir;
            go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            go.transform.localScale = Vector3.one * 0.5f;
            var start = sv.HeadPos + sv.DirVec * 0.9f;
            go.transform.position = start;
            var seq = DOTween.Sequence();
            for (int i = 0; i < 3; i++)
            {
                seq.AppendCallback(() => { go.transform.position = start; sr.color = Color.white; });
                seq.Append(go.transform.DOMove(start + sv.DirVec * 0.8f, 0.45f).SetEase(Ease.OutQuad));
                seq.Join(sr.DOFade(0.15f, 0.45f));
            }
            seq.OnComplete(() => Destroy(go));
            Destroy(go, 3f);
        }

        public bool UseUndo()
        {
            if (!CanUndo) return false;
            var sv = history.Pop();
            escaped--;
            sv.gameObject.SetActive(true);
            sv.busy = false;
            sv.ResetVisual();
            AddOcc(sv);
            active.Add(sv);
            RefreshFrozen(false);
            Sfx.Play("undo");
            sv.PopIn();
            movesLeft++; outFired = false;
            OnChanged?.Invoke();
            return true;
        }

        public void SetMode(Mode m)
        {
            mode = m;
            OnVacuumMode?.Invoke(m == Mode.Vacuum);
        }
    }
}
