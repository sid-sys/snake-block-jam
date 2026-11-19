using System;
using System.Collections.Generic;
using UnityEngine;

namespace SnakeJam
{
    public class SnakeDef
    {
        public List<Vector2Int> cells = new List<Vector2Int>(); // index 0 = head
        public int colorIdx;
        public int thaw = 0;      // 0 = normal, N = frozen until N snakes escaped
        public bool drill;        // may break one rock in front of it
        public Vector2Int Dir => cells[0] - cells[1];
    }

    // one road piece picture placed on the board (bbox size w x h cells before rotation)
    public class PlacedPiece
    {
        public string sprite; public Vector2 center; public int w, h, rot; public float wf;
        public List<Vector2Int> cells = new List<Vector2Int>();
    }

    public class LevelData
    {
        public List<PlacedPiece> pieces = new List<PlacedPiece>();
        public HashSet<Vector2Int> pieceCells = new HashSet<Vector2Int>();
        public int level, w, h;
        public bool[,] mask;
        public string shapeName, title = "";
        public int moves, minMoves;
        public bool hard;
        public List<SnakeDef> snakes = new List<SnakeDef>();
        public HashSet<Vector2Int> bridges = new HashSet<Vector2Int>();
        public HashSet<Vector2Int> roads = new HashSet<Vector2Int>();   // ground roads: snakes ride ON them (one layer)
        public bool HasRoad => roads.Count > 0;
        public HashSet<Vector2Int> lights = new HashSet<Vector2Int>();      // traffic lights: red blocks snakes, every tap flips them
        public HashSet<Vector2Int> lightsRed = new HashSet<Vector2Int>();   // lights that start red
        public bool HasLight => lights.Count > 0;
        public bool giant;                                                  // too big to fit the screen: the player must zoom
        public Dictionary<Vector2Int, int> bridgeAxis = new Dictionary<Vector2Int, int>();   // 1 = road runs left-right, 2 = up-down
        public HashSet<Vector2Int> walls = new HashSet<Vector2Int>();
        public HashSet<Vector2Int> rocks = new HashSet<Vector2Int>();
        public Dictionary<Vector2Int, Vector2Int> portals = new Dictionary<Vector2Int, Vector2Int>(); // both directions
        public List<Vector2Int> portalFirst = new List<Vector2Int>();                                  // one cell per pair

        Rules rules;
        public Rules R { get { if (rules == null) rules = new Rules(this); return rules; } }

        public bool HasBridge => bridges.Count > 0;
        public bool HasPortal => portals.Count > 0;
        public bool HasDrill { get { foreach (var s in snakes) if (s.drill) return true; return false; } }
        public bool HasFrozen { get { foreach (var s in snakes) if (s.thaw > 0) return true; return false; } }

        public int MaskCount()
        {
            int c = 0;
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (mask[x, y]) c++;
            return c;
        }
    }

    public class Ray
    {
        public List<Vector2Int> fwd;
        public int free;            // steps the head can take before it is blocked
        public bool exits;
        public bool hasRock; public Vector2Int rock;
        public long blockKey = -1;  // set when blocked by a snake
        public int blockType;       // 0 snake, 1 wall, 2 rock
    }

    // Movement rules shared by the level generator, the solver and the live board.
    public class Rules
    {
        readonly LevelData d;
        public Rules(LevelData data) { d = data; }

        public HashSet<Vector2Int> redLights;   // live state, set by the board (null = every light counts as green)

        public bool InB(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < d.w && c.y < d.h;
        public static long Key(Vector2Int c, int axis) => ((long)(c.x + 1000) * 10000L + (c.y + 1000)) * 4L + axis;
        public static int Axis(Vector2Int dir) => dir.x != 0 ? 1 : 2;   // 1 horizontal, 2 vertical

        // axis a snake body segment uses on a bridge cell (0 = normal cell, -1 = illegal)
        public int CellAxis(IList<Vector2Int> cells, int i)
        {
            var c = cells[i];
            if (!d.bridges.Contains(c)) return 0;
            if (i == 0 || i == cells.Count - 1) return -1;
            var a = cells[i - 1]; var b = cells[i + 1];
            if (a.y == c.y && b.y == c.y) return 1;
            if (a.x == c.x && b.x == c.x) return 2;
            return -1;
        }

        // cells in front of the head, following portals, until the board edge
        public List<Vector2Int> Forward(Vector2Int head, Vector2Int dir, int cap = 600)
        {
            var list = new List<Vector2Int>();
            var cur = head; int guard = 0;
            while (guard++ < cap)
            {
                var n = cur + dir;
                if (!InB(n)) break;
                list.Add(n);
                if (d.portals.TryGetValue(n, out var other)) { list.Add(other); cur = other; }
                else if (d.roads.Contains(n) && !d.roads.Contains(cur) && UnderCross(cur, n)) cur = n;   // tunnel under a road piece
                else if (d.roads.Contains(n) && !d.roads.Contains(cur) && !RoadBlocked(cur, n))
                {
                    // entered a road through its open end: follow the road (bends included) to the far end, then leave straight
                    var prev = cur; var at = n;
                    while (list.Count < cap)
                    {
                        bool found = false; var nxt = at;
                        foreach (var fd in Four) { var q = at + fd; if (q != prev && d.roads.Contains(q)) { nxt = q; found = true; break; } }
                        if (!found) break;
                        list.Add(nxt); prev = at; at = nxt;
                    }
                    if (at != n) dir = at - prev;
                    cur = at;
                }
                else cur = n;
            }
            return list;
        }

        static readonly Vector2Int[] Four = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        // Roads are walled corridors: a snake may step on or off a road only through its open end
        // (the side opposite the single road neighbour of a dead-end cell). Any other road edge is a wall.
        // a move across the road wall at an underpass cell (perpendicular to the road) goes straight through the tunnel
        bool UnderCross(Vector2Int a, Vector2Int b)
        {
            var cell = d.bridges.Contains(b) ? b : a;
            if (!d.bridges.Contains(cell)) return false;
            int ax;
            if (!d.bridgeAxis.TryGetValue(cell, out ax)) return false;
            return (ax == 1) != (a.x != b.x);
        }

        bool RoadBlocked(Vector2Int a, Vector2Int b)
        {
            bool ra = d.roads.Contains(a), rb = d.roads.Contains(b);
            if (ra == rb) return false;
            var r = ra ? a : b; var other = ra ? b : a;
            if (d.bridges.Contains(r) && UnderCross(a, b)) return false;
            int n = 0; var nb = r;
            foreach (var dir in Four) if (d.roads.Contains(r + dir)) { n++; nb = r + dir; }
            if (n != 1) return true;
            return other != r + (r - nb);
        }

        public Ray Cast(SnakeDef s, Func<long, int> occAt, int selfId, HashSet<Vector2Int> liveRocks)
        {
            var r = new Ray();
            var dir = s.Dir;
            r.fwd = Forward(s.cells[0], dir);
            int axis = Axis(dir);
            bool used = false;
            for (int i = 0; i < r.fwd.Count; i++)
            {
                var c = r.fwd[i];
                var pc = i == 0 ? s.cells[0] : r.fwd[i - 1];
                if (d.roads.Count > 0 && Mathf.Abs(pc.x - c.x) + Mathf.Abs(pc.y - c.y) == 1 && RoadBlocked(pc, c)) { r.free = i; r.blockType = 1; return r; }
                if (d.walls.Contains(c)) { r.free = i; r.blockType = 1; return r; }
                if (redLights != null && redLights.Contains(c)) { r.free = i; r.blockType = 1; return r; }
                if (liveRocks.Contains(c))
                {
                    if (s.drill && !used) { used = true; r.hasRock = true; r.rock = c; continue; }
                    r.free = i; r.blockType = 2; return r;
                }
                int sa = Mathf.Abs(pc.x - c.x) + Mathf.Abs(pc.y - c.y) == 1 ? (c.x != pc.x ? 1 : 2) : axis;
                long k = Key(c, d.bridges.Contains(c) ? sa : 0);
                int o = occAt(k);
                if (o >= 0 && o != selfId) { r.free = i; r.blockKey = k; r.blockType = 0; return r; }
            }
            r.exits = true; r.free = r.fwd.Count;
            return r;
        }
    }

    public static class LevelGen
    {
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        // ---------------------------------------------------------------- shapes
        static readonly string[] ElephantSmallArt =
        {
            "...XXXXXXX...",
            "..XXXXXXXXX..",
            ".XXXXXXXXXXX.",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            "XXXXXXXXXXXXX",
            ".XXXXXXXXXXXX",
            ".XXX.XXX.XXX.",
            ".XXX.XXX.XXX.",
            ".XXX.XXX.XXXX",
        };

        static readonly string[] ElephantArt =
        {
            "....XXXXXXXXX......",
            "..XXXXXXXXXXXXXX...",
            ".XXXXXXXXXXXXXXXXX.",
            "XXXXXXXXXXXXXXXXXXX",
            "XXXXXXXXXXXXXXXXXXX",
            "XXXXXXXXXXXXXXXXXXX",
            "XXXXXXXXXXXXXXXXXXX",
            "XXXXXXXXXXXXXXXXXXX",
            ".XXXXXXXXXXXXXX.XXX",
            ".XXXX..XXX..XXX..XX",
            ".XXXX..XXX..XXX..XX",
            ".XXXX..XXX..XXX..XX",
            ".XXXX..XXX..XXX.XXX",
        };

        static bool[,] CastleMask(out int w, out int h)
        {
            w = 27; h = 30;
            var m = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    bool on = false;
                    if (x >= 5 && x <= 21 && y <= 13) on = true;                        // curtain wall
                    if ((x <= 6 || x >= 20) && y <= 19) on = true;                      // side towers
                    if ((x <= 6 || x >= 20) && (y == 18 || y == 19) && x % 2 == 1) on = false;   // crenellations
                    if (x >= 9 && x <= 17 && y <= 21) on = true;                        // keep
                    if (y >= 22) { float hw = 5f - (y - 22) * 0.65f; if (Mathf.Abs(x - 13) <= hw) on = true; }  // roof
                    if (y >= 20 && y <= 24) { float hw = 3f - (y - 19) * 0.7f; if (Mathf.Abs(x - 3) <= hw || Mathf.Abs(x - 23) <= hw) on = true; }
                    m[x, y] = on;
                }
            return m;
        }

        // big elephant (faces right, trunk raised) - used by the Elephant level
        static bool[,] ElephantMask(out int w, out int h)
        {
            const int W = 46, H = 37; w = W; h = H;
            var m = new bool[W, H];
            void Ell(float cx, float cy, float rx, float ry)
            {
                for (int x = Mathf.Max(0, Mathf.FloorToInt(cx - rx)); x <= Mathf.Min(W - 1, Mathf.CeilToInt(cx + rx)); x++)
                    for (int y = Mathf.Max(0, Mathf.FloorToInt(cy - ry)); y <= Mathf.Min(H - 1, Mathf.CeilToInt(cy + ry)); y++)
                    {
                        float dx = (x - cx) / rx, dy = (y - cy) / ry;
                        if (dx * dx + dy * dy <= 1f) m[x, y] = true;
                    }
            }
            void Line(Vector2 a, Vector2 b, float r0, float r1)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 3f);
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps; var p = Vector2.Lerp(a, b, t); float r = Mathf.Lerp(r0, r1, t);
                    Ell(p.x, p.y, r, r);
                }
            }
            Ell(19f, 17f, 15f, 10f);                       // body
            Ell(36f, 20f, 7f, 7.8f);                       // head
            Ell(31.5f, 19.5f, 4.2f, 7f);                   // ear
            Line(new Vector2(40f, 23f), new Vector2(42.5f, 28f), 2.6f, 2.0f);   // trunk goes up ...
            Line(new Vector2(42.5f, 28f), new Vector2(43f, 32f), 2.0f, 1.7f);
            Line(new Vector2(43f, 32f), new Vector2(40.5f, 35f), 1.7f, 1.3f);   // ... and curls
            for (int x = 6; x <= 12; x++) for (int y = 0; y <= 10; y++) m[x, y] = true;     // back legs
            for (int x = 14; x <= 20; x++) for (int y = 0; y <= 10; y++) m[x, y] = true;
            for (int x = 25; x <= 30; x++) for (int y = 0; y <= 10; y++) m[x, y] = true;    // front legs
            for (int x = 32; x <= 38; x++) for (int y = 0; y <= 11; y++) m[x, y] = true;
            Line(new Vector2(5f, 22f), new Vector2(1.5f, 16f), 1.1f, 0.8f);                  // tail
            return m;
        }

        static bool[,] MakeMask(string shape, int n, out int w, out int h)
        {
            if (shape == "ElephantBig") return ElephantMask(out w, out h);
            if (shape == "Elephant")
            {
                // small elephant, turned to stand tall (10 wide x 13 high) so it fills a portrait phone screen
                w = ElephantSmallArt.Length; int ch = ElephantSmallArt[0].Length;
                h = Mathf.RoundToInt(ch * 1.4f);            // stretched a little taller to fill the screen
                var m = new bool[w, h];
                for (int r = 0; r < w; r++)
                    for (int c = 0; c < h; c++)
                        m[w - 1 - r, c] = ElephantSmallArt[r][Mathf.Min(ch - 1, Mathf.FloorToInt(c / 1.4f))] == 'X';
                return m;
            }
            if (shape == "Castle") return CastleMask(out w, out h);
            w = n; h = n;
            // phone screens are tall: normal level shapes are drawn 3:4 portrait so the board fills the screen (about the same cell count)
            // (width : height about 0.55, the shape of the free screen area, with about the same number of cells)
            if (shape != "Road" && shape != "Flower") { w = Mathf.Max(4, Mathf.RoundToInt(n * 0.72f)); h = Mathf.Max(7, Mathf.RoundToInt(n * 1.45f)); }
            if (shape == "Road") { w = n; h = n - 4; }
            var mask = new bool[w, h];
            float cx = (w - 1) / 2f, cy = (h - 1) / 2f;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    float dx = (x - cx) / (w / 2f), dy = (y - cy) / (h / 2f);
                    bool on;
                    switch (shape)
                    {
                        case "Diamond": on = Mathf.Abs(dx) + Mathf.Abs(dy) <= 1.02f; break;
                        case "Circle": on = dx * dx + dy * dy <= 1.0f; break;
                        case "Ring": on = dx * dx + dy * dy <= 1.0f && dx * dx + dy * dy >= 0.14f; break;
                        case "Cross": on = Mathf.Abs(dx) <= 0.36f || Mathf.Abs(dy) <= 0.36f; break;
                        case "Triangle": on = Mathf.Abs(dx) <= (1f - (y + 0.5f) / h) + 0.12f; break;
                        case "Heart":
                            {
                                float hx = dx * 1.25f, hy = dy * 1.25f + 0.15f;
                                float a = hx * hx + hy * hy - 1f;
                                on = a * a * a - hx * hx * hy * hy * hy <= 0f;
                                break;
                            }
                        case "Star": on = InStar(dx, dy); break;
                        case "Flower":
                            {
                                float r = Mathf.Sqrt(dx * dx + dy * dy), ang = Mathf.Atan2(dy, dx);
                                on = r <= 0.62f + 0.36f * Mathf.Abs(Mathf.Cos(4f * ang)) && r <= 1f;
                                break;
                            }
                        default: on = true; break;
                    }
                    mask[x, y] = on;
                }
            return mask;
        }

        static bool InStar(float x, float y)
        {
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float ang = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0) ? 1.02f : 0.46f;
                pts[i] = new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
            }
            bool inside = false;
            for (int i = 0, j = 9; i < 10; j = i++)
            {
                if ((pts[i].y > y) != (pts[j].y > y) &&
                    x < (pts[j].x - pts[i].x) * (y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x)
                    inside = !inside;
            }
            return inside;
        }

        // ---------------------------------------------------------------- generator
        class BridgeRun { public Vector2Int start; public int axis, len; public BridgeRun(int x, int y, int a, int l) { start = new Vector2Int(x, y); axis = a; len = l; } }

        class GenParams
        {
            public int minLen = 3, maxLen = 6, cap = 999, bridges, portalPairs, drills, frozen;
            public float turn = 0.3f, fill = 0.85f;
            public List<List<Vector2Int>> roadPaths;
            public int roads;
            public bool require;             // retry until every mechanic asked for is really on the board
            public int lights;
            public string[] pieceKeys;       // forced piece list, "name:rotation"
            public string letters;           // fixed letter roads laid out in a grid
            public float roadAvoid = 1f;   // snakes never start on a road (they only ride over roads when tapped)   // chance to reject a snake that would ride on a road
            public List<BridgeRun> runs;           // fixed roads (optional)
        }

        class Gen
        {
            readonly LevelData d; readonly Rules R; readonly System.Random rng; readonly GenParams p;
            public readonly Dictionary<long, int> occ = new Dictionary<long, int>();
            public readonly HashSet<Vector2Int> noRock = new HashSet<Vector2Int>();
            static readonly HashSet<Vector2Int> NoRocks = new HashSet<Vector2Int>();

            public Gen(LevelData data, System.Random r, GenParams gp) { d = data; R = data.R; rng = r; p = gp; }

            bool Free(Vector2Int c) => d.mask[c.x, c.y] && !d.walls.Contains(c) && !d.portals.ContainsKey(c) && !d.lights.Contains(c);

            // every cell within one step (also diagonally) of a road: only the snake that crosses a tunnel may stand here
            readonly HashSet<Vector2Int> nearRoad = new HashSet<Vector2Int>();

            bool TryPlace(List<Vector2Int> cells, bool tunnel = false)
            {
                if (cells.Count < 2) return false;
                var keys = new List<long>();
                var own = new HashSet<Vector2Int>();
                for (int i = 0; i < cells.Count; i++)
                {
                    var c = cells[i];
                    if (!R.InB(c) || !Free(c) || !own.Add(c)) return false;
                    int ax = R.CellAxis(cells, i);
                    if (ax < 0) return false;
                    long k = Rules.Key(c, ax);
                    if (occ.ContainsKey(k)) return false;
                    if (ax != 0 && occ.ContainsKey(Rules.Key(c, 0))) return false;
                    keys.Add(k);
                }
                if (d.HasRoad && rng.NextDouble() < p.roadAvoid) { foreach (var rc in cells) if (d.roads.Contains(rc) && !d.bridges.Contains(rc)) return false; }
                if (!tunnel && nearRoad.Count > 0) foreach (var nc0 in cells) if (nearRoad.Contains(nc0)) return false;
                var def = new SnakeDef { cells = cells };
                var ray = R.Cast(def, k => occ.TryGetValue(k, out int v) ? v : -1, -1, NoRocks);
                if (!ray.exits) return false;
                foreach (var c in ray.fwd) if (own.Contains(c)) return false;
                int id = d.snakes.Count;
                d.snakes.Add(def);
                foreach (var k in keys) occ[k] = id;
                return true;
            }

            List<Vector2Int> Walk(Vector2Int head, Vector2Int dir, int len)
            {
                var neck = head - dir;
                if (!R.InB(neck) || !Free(neck) || d.bridges.Contains(neck) || (d.roads.Contains(neck) && !d.bridges.Contains(neck))) return null;
                var cells = new List<Vector2Int> { head, neck };
                var own = new HashSet<Vector2Int> { head, neck };
                var cur = neck; var heading = -dir;
                while (cells.Count < len)
                {
                    bool forced = d.bridges.Contains(cur);
                    bool straightFirst = rng.NextDouble() > p.turn;
                    var order = new List<Vector2Int>();
                    if (forced) order.Add(heading);
                    else
                    {
                        if (straightFirst) order.Add(heading);
                        var side = new List<Vector2Int>();
                        foreach (var dd in Dirs) if (dd != heading && dd != -heading) side.Add(dd);
                        if (rng.Next(2) == 0) side.Reverse();
                        order.AddRange(side);
                        if (!straightFirst) order.Add(heading);
                    }
                    bool moved = false;
                    foreach (var nd in order)
                    {
                        var nc = cur + nd;
                        if (!R.InB(nc) || !Free(nc) || own.Contains(nc) || (d.roads.Contains(nc) && !d.bridges.Contains(nc))) continue;
                        if (d.bridges.Contains(nc))
                        {
                            if (nd != heading) continue;
                            if (occ.ContainsKey(Rules.Key(nc, Rules.Axis(nd))) || occ.ContainsKey(Rules.Key(nc, 0))) continue;
                        }
                        else if (occ.ContainsKey(Rules.Key(nc, 0))) continue;
                        cells.Add(nc); own.Add(nc); cur = nc; heading = nd; moved = true; break;
                    }
                    if (!moved) break;
                }
                // never end on a bridge cell
                while (cells.Count > 2 && d.bridges.Contains(cells[cells.Count - 1])) cells.RemoveAt(cells.Count - 1);
                return cells;
            }

            // a straight snake that crosses a road (axis = direction of travel, len = road length in cells)
            void ThroughBridge(Vector2Int b, int axis, int len)
            {
                var step = axis == 1 ? Vector2Int.right : Vector2Int.up;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int a = rng.Next(2, 6), z = rng.Next(2, 6);
                    var cells = new List<Vector2Int>();
                    for (int i = -a; i <= len - 1 + z; i++) cells.Add(b + step * i);
                    cells.RemoveAll(c => !R.InB(c));
                    if (rng.Next(2) == 0) cells.Reverse();
                    if (TryPlace(new List<Vector2Int>(cells), true)) return;
                    cells.Reverse();
                    if (TryPlace(cells, true)) return;
                }
            }

            // how many cells lie between a cell and the nearest edge of the shape (4 directions)
            int Depth(Vector2Int c)
            {
                int best = int.MaxValue;
                foreach (var dir in Dirs)
                {
                    int k = 0; var p = c + dir;
                    while (R.InB(p) && d.mask[p.x, p.y]) { k++; p += dir; }
                    if (k < best) best = k;
                }
                return best;
            }

            public void Run()
            {
                foreach (var rcell in d.roads)
                    for (int ddx = -1; ddx <= 1; ddx++)
                        for (int ddy = -1; ddy <= 1; ddy++) nearRoad.Add(rcell + new Vector2Int(ddx, ddy));
                foreach (var b in new List<Vector2Int>(d.bridges))
                {
                    int ax = d.bridgeAxis[b];
                    var step = ax == 1 ? Vector2Int.right : Vector2Int.up;
                    bool runStart = !(d.bridges.Contains(b - step) && d.bridgeAxis[b - step] == ax);
                    if (runStart && !d.roads.Contains(b))
                    {
                        int len = 1;
                        while (d.bridges.Contains(b + step * len) && d.bridgeAxis[b + step * len] == ax) len++;
                        ThroughBridge(b, ax, len);
                    }
                    ThroughBridge(b, 3 - ax, 1);          // every underpass gets a snake that crosses under the road
                }
                // roads: line up a snake in front of each open end so it can ride through the road
                foreach (var e in new List<Vector2Int>(d.roads))
                {
                    int rn = 0; var nb = e;
                    foreach (var fd in Dirs) if (d.roads.Contains(e + fd)) { rn++; nb = e + fd; }
                    if (rn != 1 || rng.NextDouble() > 0.85) continue;
                    var din = nb - e;
                    for (int j = 2; j <= 2; j++)          // the rider waits with ONE empty cell between its head and the road end
                    {
                        var h = e - din * j;
                        if (!R.InB(h) || !Free(h) || d.roads.Contains(h)) break;
                        bool done = false;
                        for (int tr = 0; tr < 10 && !done; tr++)
                        {
                            var cells = Walk(h, din, rng.Next(p.minLen, p.maxLen + 1));
                            if (cells != null && cells.Count >= p.minLen && TryPlace(cells)) done = true;
                        }
                        if (done) break;
                    }
                }
                int total = d.MaskCount();
                int fails = 0;
                var empties = new List<Vector2Int>();
                while (fails < Mathf.Max(300, d.MaskCount()) && d.snakes.Count < p.cap)
                {
                    if (occ.Count >= total * p.fill && d.snakes.Count >= 3) break;
                    empties.Clear();
                    for (int x = 0; x < d.w; x++)
                        for (int y = 0; y < d.h; y++)
                        {
                            var c = new Vector2Int(x, y);
                            if (Free(c) && !d.bridges.Contains(c) && !occ.ContainsKey(Rules.Key(c, 0))) empties.Add(c);
                        }
                    if (empties.Count < p.minLen) break;
                    // spread the snakes evenly: snakes placed first can be blocked by later ones, snakes placed last need a clear way out.
                    // So start in the middle (deepest cells) and finish at the edges; this stops empty bands from forming in the middle.
                    // and pick the empty cell farthest from every snake already placed, so no part of the board stays bare.
                    var head = empties[rng.Next(empties.Count)];
                    int bestScore = -1, bestFar = 0;
                    for (int s = 0; s < 12; s++)
                    {
                        var cand = empties[rng.Next(empties.Count)];
                        int far = 99;
                        foreach (var sn in d.snakes)
                            foreach (var sc in sn.cells)
                            {
                                int dd = Mathf.Max(Mathf.Abs(sc.x - cand.x), Mathf.Abs(sc.y - cand.y));
                                if (dd < far) far = dd;
                            }
                        int score = far * 4 + Mathf.Min(Depth(cand), 3);
                        if (score > bestScore) { bestScore = score; head = cand; bestFar = far; }
                    }
                    if (bestFar <= 2 || fails > 0 && rng.Next(3) == 0) head = empties[rng.Next(empties.Count)];   // gaps are small now: any free cell will do
                    var dirs = new List<Vector2Int>(Dirs);
                    for (int i = dirs.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = dirs[i]; dirs[i] = dirs[j]; dirs[j] = t; }
                    bool placed = false;
                    foreach (var dir in dirs)
                    {
                        var cells = Walk(head, dir, rng.Next(p.minLen, p.maxLen + 1));
                        if (cells == null || cells.Count < p.minLen) continue;
                        if (TryPlace(cells)) { placed = true; break; }
                    }
                    if (!placed) fails++;
                }
                // gap fill: try every free cell in every direction, emptiest places first, with shorter snakes too.
                void GapFill(int minL)
                {
                for (int pass = 0; pass < 8 && d.snakes.Count < p.cap && occ.Count < total * Mathf.Min(1f, p.fill + 0.12f); pass++)
                {
                    var cand = new List<(Vector2Int c, int far)>();
                    for (int x = 0; x < d.w; x++)
                        for (int y = 0; y < d.h; y++)
                        {
                            var c = new Vector2Int(x, y);
                            if (!Free(c) || d.bridges.Contains(c) || occ.ContainsKey(Rules.Key(c, 0))) continue;
                            int far = 99;
                            foreach (var sn in d.snakes)
                                foreach (var sc in sn.cells)
                                {
                                    int dd = Mathf.Max(Mathf.Abs(sc.x - c.x), Mathf.Abs(sc.y - c.y));
                                    if (dd < far) far = dd;
                                }
                            cand.Add((c, far * 100 + rng.Next(100)));
                        }
                    cand.Sort((a, b) => b.far.CompareTo(a.far));
                    int added = 0;
                    foreach (var cd in cand)
                    {
                        if (d.snakes.Count >= p.cap || occ.Count >= total * Mathf.Min(1f, p.fill + 0.12f)) break;
                        if (occ.ContainsKey(Rules.Key(cd.c, 0)) || !Free(cd.c)) continue;
                        bool ok = false;
                        foreach (var dir in Dirs)
                        {
                            for (int len = p.maxLen; len >= minL && !ok; len -= Mathf.Max(1, (p.maxLen - minL) / 3))
                            {
                                var cells = Walk(cd.c, dir, Mathf.Max(minL, len));
                                if (cells != null && cells.Count >= minL && TryPlace(cells)) ok = true;
                            }
                            if (ok) break;
                        }
                        if (ok) added++;
                    }
                    if (added == 0) break;
                }
                // last step: grow snake tails into the free cells that are left, so the board is full like the reference levels.
                // (only on levels without a snake cap: the first levels keep a few short snakes on purpose)
                }
                GapFill(p.minLen);
                Extend();
                GapFill(Mathf.Max(4, p.minLen - 2));
                Extend();
                GapFill(4);
                Extend();
            }

            // grow the tail of one snake by one cell. Snakes leave in the reverse order they were placed, so snake i
            // only has to get past snakes with a smaller number, and no snake with a bigger number may be blocked by the new cell.
            bool TryExtend(int i, Vector2Int nc)
            {
                var def = d.snakes[i]; var old = def.cells;
                var nl = new List<Vector2Int>(old) { nc };
                var oldKeys = new List<long>();
                for (int k = 0; k < old.Count; k++) oldKeys.Add(Rules.Key(old[k], R.CellAxis(old, k)));
                foreach (var k in oldKeys) occ.Remove(k);
                bool ok = true;
                var keys = new List<long>();
                var own = new HashSet<Vector2Int>();
                for (int k = 0; k < nl.Count && ok; k++)
                {
                    var c = nl[k];
                    if (!R.InB(c) || !Free(c) || !own.Add(c)) { ok = false; break; }
                    int ax = R.CellAxis(nl, k);
                    if (ax < 0) { ok = false; break; }
                    long key = Rules.Key(c, ax);
                    if (occ.ContainsKey(key) || (ax != 0 && occ.ContainsKey(Rules.Key(c, 0)))) { ok = false; break; }
                    keys.Add(key);
                }
                if (ok && d.HasRoad) foreach (var rc in nl) if (d.roads.Contains(rc) && !d.bridges.Contains(rc)) { ok = false; break; }
                if (ok && nearRoad.Contains(nc)) ok = false;
                if (ok)
                {
                    def.cells = nl;
                    var ray = R.Cast(def, k => occ.TryGetValue(k, out int v) && v < i ? v : -1, -1, NoRocks);
                    if (!ray.exits) ok = false;
                    else foreach (var c in ray.fwd) if (own.Contains(c)) { ok = false; break; }
                    if (!ok) def.cells = old;
                }
                if (ok)
                {
                    // snakes placed later leave earlier: the new cell must not stand in their way
                    for (int j = i + 1; j < d.snakes.Count && ok; j++)
                    {
                        var fwd = R.Forward(d.snakes[j].cells[0], d.snakes[j].Dir);
                        if (fwd.Contains(nc)) ok = false;
                    }
                    if (!ok) def.cells = old;
                }
                if (ok) { foreach (var k in keys) occ[k] = i; return true; }
                foreach (var k in oldKeys) occ[k] = i;
                return false;
            }

            void Extend()
            {
                for (int round = 0; round < 60; round++)
                {
                    bool grew = false;
                    for (int i = 0; i < d.snakes.Count; i++)
                    {
                        var cells = d.snakes[i].cells;
                        if (cells.Count >= p.maxLen * 2) continue;
                        var tail = cells[cells.Count - 1];
                        var order = new List<Vector2Int>(Dirs);
                        for (int a = order.Count - 1; a > 0; a--) { int b = rng.Next(a + 1); var t = order[a]; order[a] = order[b]; order[b] = t; }
                        foreach (var dir in order)
                        {
                            var nc = tail + dir;
                            if (!R.InB(nc) || !Free(nc) || d.bridges.Contains(nc) || occ.ContainsKey(Rules.Key(nc, 0))) continue;
                            if (TryExtend(i, nc)) { grew = true; break; }
                        }
                    }
                    if (!grew) break;
                }
            }
        }

        static void ReservePortals(LevelData d, System.Random rng, int pairs)
        {
            for (int k = 0; k < pairs; k++)
            {
                for (int tries = 0; tries < 80; tries++)
                {
                    var a = new Vector2Int(rng.Next(d.w), rng.Next(d.h));
                    var b = new Vector2Int(rng.Next(d.w), rng.Next(d.h));
                    if (!d.mask[a.x, a.y] || !d.mask[b.x, b.y]) continue;
                    if (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) < 5) continue;
                    if (d.portals.ContainsKey(a) || d.portals.ContainsKey(b) || d.walls.Contains(a) || d.walls.Contains(b) || d.bridges.Contains(a) || d.bridges.Contains(b)) continue;
                    d.portals[a] = b; d.portals[b] = a; d.portalFirst.Add(a);
                    break;
                }
            }
        }

        static bool MaskAt(LevelData d, Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < d.w && c.y < d.h && d.mask[c.x, c.y] && !d.walls.Contains(c) && !d.portals.ContainsKey(c);

        static void ReserveBridges(LevelData d, System.Random rng, GenParams p)
        {
            if (p.runs != null)
            {
                foreach (var r in p.runs)
                {
                    var st = r.axis == 1 ? Vector2Int.right : Vector2Int.up;
                    for (int i = 0; i < r.len; i++)
                    {
                        var c = r.start + st * i;
                        if (MaskAt(d, c)) { d.bridges.Add(c); d.bridgeAxis[c] = r.axis; }
                    }
                }
                return;
            }
            int made = 0, tries = 0;
            while (made < p.bridges && tries++ < 400)
            {
                int axis = rng.Next(2) + 1, len = 1;
                var st = axis == 1 ? Vector2Int.right : Vector2Int.up;
                var perp = axis == 1 ? Vector2Int.up : Vector2Int.right;
                var s = new Vector2Int(rng.Next(2, Mathf.Max(3, d.w - 2)), rng.Next(2, Mathf.Max(3, d.h - 2)));
                var cells = new List<Vector2Int>();
                bool ok = MaskAt(d, s - st) && MaskAt(d, s + st * len);
                for (int i = 0; i < len && ok; i++)
                {
                    var c = s + st * i;
                    if (!MaskAt(d, c) || d.bridges.Contains(c) || !MaskAt(d, c + perp) || !MaskAt(d, c - perp)) ok = false;
                    cells.Add(c);
                }
                if (!ok) continue;
                bool near = false;
                foreach (var b in d.bridges) foreach (var c in cells) if (Mathf.Abs(b.x - c.x) + Mathf.Abs(b.y - c.y) < 3) near = true;
                if (near) continue;
                foreach (var c in cells) { d.bridges.Add(c); d.bridgeAxis[c] = axis; }
                made++;
            }
        }

        // every letter is ONE road with exactly two open ends (one way in, one way out); strokes keep a 1-cell gap
        // ---- road pieces (pictures from the generated sheet). Every piece is one road with exactly two open ends.
        // pts = polyline of the road in cells, under = underpass cells (straight pieces only), wf = share of the picture that is front wall
        class PieceDef
        {
            public string sprite; public int[] pts; public int[][] under; public float wf;
            public PieceDef(string s, int[] p, int[][] u, float w) { sprite = s; pts = p; under = u; wf = w; }
        }

        static readonly PieceDef[] Pieces =
        {
            new PieceDef("rp_h1", new[] { 0, 0, 2, 0 }, new[] { new[] { 1, 0 } }, 0.37f),
            new PieceDef("rp_h2", new[] { 0, 0, 5, 0 }, new[] { new[] { 1, 0 }, new[] { 4, 0 } }, 0.37f),
            new PieceDef("rp_h3", new[] { 0, 0, 8, 0 }, new[] { new[] { 1, 0 }, new[] { 4, 0 }, new[] { 7, 0 } }, 0.37f),
            new PieceDef("rp_l",  new[] { 0, 2, 0, 0, 2, 0 }, null, 0.14f),
            new PieceDef("rp_lr", new[] { 0, 0, 2, 0, 2, 2 }, null, 0.14f),
            new PieceDef("rp_u",  new[] { 0, 0, 0, 2, 3, 2, 3, 0 }, null, 0.14f),
            new PieceDef("rp_s",  new[] { 0, 3, 0, 2, 2, 2, 2, 0 }, null, 0.14f),
            new PieceDef("rp_z",  new[] { 0, 4, 3, 4, 3, 2, 0, 2, 0, 0, 3, 0 }, null, 0.14f),
            new PieceDef("rp_c",  new[] { 2, 2, 0, 2, 0, 0, 2, 0 }, null, 0.14f),
            new PieceDef("rp_m",  new[] { 0, 0, 0, 2, 2, 2, 2, 1, 4, 1, 4, 2, 6, 2, 6, 0 }, null, 0.14f),
            new PieceDef("rp_w",  new[] { 0, 2, 0, 0, 2, 0, 2, 1, 4, 1, 4, 0, 6, 0, 6, 2 }, null, 0.14f),
        };

        static bool PlacePiece(LevelData d, PieceDef pd, int rot, Vector2Int o, bool mustFit)
        {
            var cells = new List<Vector2Int>();
            for (int i = 0; i + 3 < pd.pts.Length; i += 2)
            {
                var a = new Vector2Int(pd.pts[i], pd.pts[i + 1]); var b = new Vector2Int(pd.pts[i + 2], pd.pts[i + 3]);
                var st = new Vector2Int(Math.Sign(b.x - a.x), Math.Sign(b.y - a.y));
                for (var c = a; ; c += st) { if (!cells.Contains(c)) cells.Add(c); if (c == b) break; }
            }
            var under = new List<Vector2Int>();
            if (pd.under != null) foreach (var u in pd.under) under.Add(new Vector2Int(u[0], u[1]));
            int w = 0, h = 0;
            foreach (var c in cells) { w = Mathf.Max(w, c.x + 1); h = Mathf.Max(h, c.y + 1); }
            for (int r = 0; r < rot; r++)
            {
                for (int i = 0; i < cells.Count; i++) cells[i] = new Vector2Int(-cells[i].y, cells[i].x);
                for (int i = 0; i < under.Count; i++) under[i] = new Vector2Int(-under[i].y, under[i].x);
            }
            int minx = int.MaxValue, miny = int.MaxValue, maxx = int.MinValue, maxy = int.MinValue;
            foreach (var c in cells) { minx = Mathf.Min(minx, c.x); miny = Mathf.Min(miny, c.y); maxx = Mathf.Max(maxx, c.x); maxy = Mathf.Max(maxy, c.y); }
            var shift = new Vector2Int(minx, miny);
            for (int i = 0; i < cells.Count; i++) cells[i] = cells[i] - shift + o;
            for (int i = 0; i < under.Count; i++) under[i] = under[i] - shift + o;
            int deck = rot % 2 == 0 ? 1 : 2;
            var perp = deck == 1 ? Vector2Int.up : Vector2Int.right;
            if (mustFit)
            {
                var own = new HashSet<Vector2Int>(cells);
                // a portal may never sit under the road picture: keep the whole picture area (plus one cell) free of portals
                foreach (var pk in d.portals.Keys)
                    if (pk.x >= o.x - 1 && pk.x <= o.x + (maxx - minx) + 1 && pk.y >= o.y - 1 && pk.y <= o.y + (maxy - miny) + 1) return false;
                foreach (var c in cells)
                {
                    if (!MaskAt(d, c) || d.bridges.Contains(c) || d.roads.Contains(c) || d.lights.Contains(c)) return false;
                    foreach (var fd in Dirs) { var n = c + fd; if (!own.Contains(n) && d.roads.Contains(n)) return false; }   // never touch another road
                }
                foreach (var u in under)
                    if (!MaskAt(d, u + perp) || !MaskAt(d, u - perp) || d.roads.Contains(u + perp) || d.roads.Contains(u - perp)) return false;
            }
            var pp = new PlacedPiece { sprite = pd.sprite, w = w, h = h, rot = rot, wf = pd.wf, center = new Vector2(o.x + (maxx - minx) / 2f, o.y + (maxy - miny) / 2f) };
            foreach (var c in cells) { if (MaskAt(d, c)) { d.roads.Add(c); d.pieceCells.Add(c); pp.cells.Add(c); } }
            foreach (var u in under) { d.bridges.Add(u); d.bridgeAxis[u] = deck; }
            d.pieces.Add(pp);
            return true;
        }

        static readonly Dictionary<char, int[][]> LetterDefs = new Dictionary<char, int[][]>
        {
            ['I'] = new[] { new[] { 0, 0, 0, 6 } },
            ['L'] = new[] { new[] { 0, 6, 0, 0, 4, 0 } },
            ['J'] = new[] { new[] { 4, 6, 4, 0, 0, 0, 0, 2 } },
            ['U'] = new[] { new[] { 0, 6, 0, 0, 4, 0, 4, 6 } },
            ['C'] = new[] { new[] { 4, 6, 0, 6, 0, 0, 4, 0 } },
            ['S'] = new[] { new[] { 4, 6, 0, 6, 0, 3, 4, 3, 4, 0, 0, 0 } },
            ['Z'] = new[] { new[] { 0, 6, 4, 6, 4, 4, 2, 4, 2, 2, 0, 2, 0, 0, 4, 0 } },
            ['N'] = new[] { new[] { 0, 0, 0, 6, 2, 6, 2, 0, 4, 0, 4, 6 } },
            ['M'] = new[] { new[] { 0, 0, 0, 6, 2, 6, 2, 2, 4, 2, 4, 6, 6, 6, 6, 0 } },        // I  V  I
            ['W'] = new[] { new[] { 0, 6, 0, 0, 2, 0, 2, 4, 4, 4, 4, 0, 6, 0, 6, 6 } },
            ['V'] = new[] { new[] { 0, 6, 0, 3, 2, 3, 2, 0, 4, 0, 4, 3, 6, 3, 6, 6 } },
        };
        const string LetterKeys = "MNSZCWUILJV";

        // road shaped like a letter (up to 7 x 7 cells) with its corner at o
        static bool PlaceLetter(LevelData d, char ch, Vector2Int o, bool mustFit)
        {
            var cells = new List<Vector2Int>();
            foreach (var pl in LetterDefs[ch])
                for (int i = 0; i + 3 < pl.Length; i += 2)
                {
                    var a = new Vector2Int(pl[i], pl[i + 1]) + o; var b = new Vector2Int(pl[i + 2], pl[i + 3]) + o;
                    var st = new Vector2Int(Math.Sign(b.x - a.x), Math.Sign(b.y - a.y));
                    for (var c = a; ; c += st) { cells.Add(c); if (c == b) break; }
                }
            if (mustFit)
                foreach (var c in cells)
                {
                    if (!MaskAt(d, c) || d.bridges.Contains(c) || d.roads.Contains(c) || d.lights.Contains(c)) return false;
                    foreach (var fd in Dirs) if (d.roads.Contains(c + fd)) return false;       // never touch another road
                }
            foreach (var c in cells) if (MaskAt(d, c)) d.roads.Add(c);
            return true;
        }

        static void ReserveRoads(LevelData d, System.Random rng, GenParams p)
        {
            if (p.pieceKeys != null)
            {
                foreach (var key in p.pieceKeys)
                {
                    var parts = key.Split(':'); int rot = int.Parse(parts[1]);
                    PieceDef pd = null; foreach (var q in Pieces) if (q.sprite == parts[0]) pd = q;
                    for (int tries = 0; tries < 400; tries++)
                        if (PlacePiece(d, pd, rot, new Vector2Int(rng.Next(0, Mathf.Max(1, d.w - 3)), rng.Next(0, Mathf.Max(1, d.h - 3))), true)) break;
                }
                return;
            }
            if (p.letters != null)
            {
                for (int i = 0; i < p.letters.Length; i++) PlaceLetter(d, p.letters[i], new Vector2Int((i % 3) * 11 + 3, (i / 3) * 11 + 3), false);
                return;
            }
            if (p.roadPaths != null)
            {
                foreach (var path in p.roadPaths)
                    for (int i = 0; i + 1 < path.Count; i++)
                    {
                        var a = path[i]; var b = path[i + 1];
                        var st = new Vector2Int(Math.Sign(b.x - a.x), Math.Sign(b.y - a.y));
                        for (var c = a; ; c += st)
                        {
                            if (MaskAt(d, c) && !d.bridges.Contains(c)) d.roads.Add(c);
                            if (c == b) break;
                        }
                    }
                return;
            }
            if (!p.require && (d.w < 9 || d.h < 9)) return;      // small tutorial boards may carry one small road piece
            if (d.w < 5 || d.h < 7) return;
            for (int k = 0; k < p.roads; k++)
                for (int tries = 0; tries < 80; tries++)
                    if (PlacePiece(d, Pieces[rng.Next(Pieces.Length)], rng.Next(4), new Vector2Int(rng.Next(0, d.w - 2), rng.Next(0, d.h - 2)), true)) break;
        }

        // mostly red, blue, green, yellow, violet; every 6th snake gets one of the 3 extra colours
        static int Pick(int i, int seed) { if (i % 6 == 5) return 5 + (i / 6 + seed) % 3; return (i + seed) % 5; }

        // nothing decorative may go unused: drop every bridge, road, light and portal that no snake sits on or travels through
        static void PruneUnused(LevelData d)
        {
            var R = d.R;
            var used = new HashSet<Vector2Int>();
            foreach (var sn in d.snakes)
            {
                foreach (var c in sn.cells) used.Add(c);
                foreach (var c in R.Forward(sn.cells[0], sn.Dir)) used.Add(c);
            }
            foreach (var bc in new List<Vector2Int>(d.bridges)) if (!used.Contains(bc)) { d.bridges.Remove(bc); d.bridgeAxis.Remove(bc); }
            foreach (var lc in new List<Vector2Int>(d.lights)) if (!used.Contains(lc)) { d.lights.Remove(lc); d.lightsRed.Remove(lc); }
            foreach (var pa in new List<Vector2Int>(d.portalFirst))
            {
                var pb = d.portals[pa];
                if (used.Contains(pa) || used.Contains(pb)) continue;
                d.portals.Remove(pa); d.portals.Remove(pb); d.portalFirst.Remove(pa);
            }
            var seen = new HashSet<Vector2Int>();
            foreach (var start in new List<Vector2Int>(d.roads))
            {
                if (seen.Contains(start)) continue;
                var comp = new List<Vector2Int>(); var q = new Queue<Vector2Int>();
                q.Enqueue(start); seen.Add(start);
                while (q.Count > 0)
                {
                    var c = q.Dequeue(); comp.Add(c);
                    foreach (var fd in Dirs) { var nn = c + fd; if (d.roads.Contains(nn) && seen.Add(nn)) q.Enqueue(nn); }
                }
                bool any = false;
                foreach (var c in comp) if (used.Contains(c)) { any = true; break; }
                if (!any) foreach (var c in comp) d.roads.Remove(c);
            }
            // keep pieces, underpasses and their pictures in step with the roads that survived
            foreach (var pc in new List<PlacedPiece>(d.pieces)) if (pc.cells.Count == 0 || !d.roads.Contains(pc.cells[0])) d.pieces.Remove(pc);
            d.pieceCells.RemoveWhere(c => !d.roads.Contains(c));
            foreach (var bc in new List<Vector2Int>(d.bridges)) if (!d.roads.Contains(bc)) { d.bridges.Remove(bc); d.bridgeAxis.Remove(bc); }
        }

        static void ReserveLights(LevelData d, System.Random rng, GenParams p)
        {
            for (int k = 0, tries = 0; k < p.lights && tries < 300; tries++)
            {
                var c = new Vector2Int(rng.Next(1, Mathf.Max(2, d.w - 1)), rng.Next(1, Mathf.Max(2, d.h - 1)));
                if (!MaskAt(d, c) || d.bridges.Contains(c) || d.roads.Contains(c) || d.lights.Contains(c) || d.walls.Contains(c)) continue;
                d.lights.Add(c);
                if (rng.Next(2) == 0) d.lightsRed.Add(c);
                k++;
            }
        }

        static void AddDrills(LevelData d, System.Random rng, int count)
        {
            var R = d.R;
            int made = 0;
            for (int tries = 0; tries < 200 && made < count; tries++)
            {
                if (d.snakes.Count == 0) return;
                var s = d.snakes[rng.Next(d.snakes.Count)];
                if (s.drill || s.thaw > 0) continue;
                var fwd = R.Forward(s.cells[0], s.Dir);
                if (fwd.Count < 3) continue;
                var c = fwd[rng.Next(0, fwd.Count)];
                if (d.walls.Contains(c) || d.rocks.Contains(c) || d.bridges.Contains(c) || d.roads.Contains(c) || d.lights.Contains(c) || d.portals.ContainsKey(c)) continue;
                bool taken = false;
                foreach (var o in d.snakes) if (o.cells.Contains(c)) { taken = true; break; }
                if (taken) continue;
                bool inOther = false;
                foreach (var o in d.snakes)
                {
                    if (o == s) continue;
                    foreach (var f in R.Forward(o.cells[0], o.Dir)) if (f == c) { inOther = true; break; }
                    if (inOther) break;
                }
                if (inOther) continue;
                d.rocks.Add(c); s.drill = true; made++;
            }
        }

        static LevelData Generate(string shape, int n, int seed, GenParams p, int level, List<Vector2Int> walls = null)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var rng = new System.Random(seed + attempt * 9973);
                int w, h;
                var mask = MakeMask(shape, n, out w, out h);
                var d = new LevelData { level = level, w = w, h = h, mask = mask, shapeName = shape };
                if (walls != null) foreach (var c in walls) if (c.x < w && c.y < h) d.walls.Add(c);
                ReserveBridges(d, rng, p);
                ReservePortals(d, rng, p.portalPairs);
                ReserveRoads(d, rng, p);
                ReserveLights(d, rng, p);
                new Gen(d, rng, p).Run();
                PruneUnused(d);
                if (d.snakes.Count < 2) continue;
                if (p.drills > 0) AddDrills(d, rng, p.drills);
                if (p.frozen > 0 && d.snakes.Count >= 8)
                {
                    int half = d.snakes.Count / 2;
                    for (int k = 0; k < p.frozen; k++)
                    {
                        var s = d.snakes[rng.Next(half)];
                        if (s.drill) continue;
                        s.thaw = Mathf.Min(2 + level / 60, d.snakes.Count - 1 - d.snakes.IndexOf(s));
                    }
                }
                for (int i = 0; i < d.snakes.Count; i++) d.snakes[i].colorIdx = Pick(i, seed);
                if (p.require)
                {
                    // tutorial and boss levels must really show the mechanics they promise
                    if (p.roads > 0 && d.roads.Count == 0) continue;
                    if (p.lights > 0 && d.lights.Count == 0) continue;
                    if (p.portalPairs > 0 && d.portals.Count == 0) continue;
                    if (p.drills > 0 && d.rocks.Count == 0) continue;
                    if (p.frozen > 0 && !d.HasFrozen) continue;
                }
                if (IsSolvable(d)) return d;
            }
            // fall back to the simplest board so the game never gets stuck
            var fb = Generate("Square", 5, seed + 1, new GenParams { minLen = 3, maxLen = 4, turn = 0.2f, fill = 0.6f }, level);
            return fb;
        }

        // ---------------------------------------------------------------- solver
        public static bool IsSolvable(LevelData d)
        {
            var R = d.R;
            var occ = new Dictionary<long, int>();
            for (int i = 0; i < d.snakes.Count; i++)
                for (int j = 0; j < d.snakes[i].cells.Count; j++)
                    occ[Rules.Key(d.snakes[i].cells[j], R.CellAxis(d.snakes[i].cells, j))] = i;
            var rocks = new HashSet<Vector2Int>(d.rocks);
            var alive = new List<int>(); for (int i = 0; i < d.snakes.Count; i++) alive.Add(i);
            int escaped = 0; bool progress = true;
            while (alive.Count > 0 && progress)
            {
                progress = false;
                foreach (int i in alive.ToArray())
                {
                    var s = d.snakes[i];
                    if (s.thaw - escaped > 0) continue;
                    var ray = R.Cast(s, k => occ.TryGetValue(k, out int v) ? v : -1, i, rocks);
                    if (!ray.exits) continue;
                    for (int j = 0; j < s.cells.Count; j++) occ.Remove(Rules.Key(s.cells[j], R.CellAxis(s.cells, j)));
                    if (ray.hasRock) rocks.Remove(ray.rock);
                    alive.Remove(i); escaped++; progress = true;
                }
            }
            return alive.Count == 0;
        }

        // ---------------------------------------------------------------- moves
        static void SetMoves(LevelData d, bool hard, bool easy)
        {
            int n = d.snakes.Count;
            d.minMoves = n;     // every snake needs exactly one tap when played perfectly
            int buffer = easy ? Mathf.Max(4, n) : (hard ? Mathf.CeilToInt(n * 0.3f) + 2 : Mathf.CeilToInt(n * 0.6f) + 2);
            d.moves = n + buffer + d.lights.Count * 2;
        }

        // ---------------------------------------------------------------- progression
        public static LevelData Build(int level)
        {
            bool boss = level % 10 == 0 && level >= 10;
            if (boss)
            {
                LevelData sp;
                if (level <= 50)
                {
                    // boss 1 mega grid, 2 roads, 3 roads + lights, 4 portals + lights + roads, 5 everything
                    int k = level / 10;
                    var bp = new GenParams { minLen = 6, maxLen = 14, turn = 0.4f, fill = 0.98f, require = true };
                    string bshape = "Square"; int bn = 10 + k;
                    if (k >= 2) { bp.roads = k >= 4 ? 3 : 2; }
                    if (k >= 3) bp.lights = k >= 5 ? 3 : 2;
                    if (k >= 4) bp.portalPairs = k >= 5 ? 2 : 1;
                    if (k >= 5) { bp.drills = 2; bp.frozen = 2; bshape = "Diamond"; bn = 15; }
                    sp = Generate(bshape, bn, level * 104729 + 7, bp, level);
                }
                else
                {
                    string[] keys = { "square", "flower", "road", "castle", "elephant", "giant" };
                    sp = BuildSpecial(keys[(level / 10 - 1) % 6], level);
                }
                sp.level = level; sp.hard = true;
                SetMargin(sp, MarginFor(level));
                sp.title = "";
                return sp;
            }

            var rng = new System.Random(level * 7919 + 13);
            bool breather = level > 11 && level % 10 == 1;
            float dd = Mathf.Clamp01(level / 120f);
            if (breather) dd *= 0.35f;

            string shape; int n;
            if (level <= 10)
            {
                shape = level <= 3 ? "Square" : (level % 2 == 0 ? "Wide" : "Square");
                n = 4 + (level + 1) / 3;
            }
            else if (level <= 30)
            {
                string[] pool = { "Square", "Circle", "Diamond", "Triangle", "Cross", "Heart", "Flower" };
                shape = pool[rng.Next(pool.Length)]; n = 8 + (level - 10) / 3;
            }
            else if (level <= 70)
            {
                string[] pool = { "Circle", "Star", "Heart", "Ring", "Flower", "Elephant", "Cross", "Diamond" };
                shape = pool[rng.Next(pool.Length)]; n = 14 + (level - 30) / 5;
            }
            else
            {
                string[] pool = { "Star", "Heart", "Circle", "Ring", "Elephant", "Cross", "Elephant" };
                shape = pool[rng.Next(pool.Length)]; n = 20 + Mathf.Min(6, (level - 70) / 10);
            }
            if (breather) { shape = "Square"; n = Mathf.Max(4, n - 3); }

            var p = new GenParams();
            p.minLen = level <= 3 ? 4 : (level <= 10 ? 5 : 6 + Mathf.RoundToInt(dd * 3f));
            p.maxLen = level <= 3 ? 5 : (level <= 10 ? 6 : Mathf.Max(8, n + 1 + Mathf.RoundToInt(dd * 6f)));
            p.turn = level <= 10 ? 0.05f + level * 0.02f : 0.18f + 0.45f * dd;
            p.fill = level <= 10 ? 0.55f + level * 0.03f : 0.9f + 0.08f * dd;
            if (level <= 3) p.cap = 3; else if (level <= 10) p.cap = 4 + level / 3;
            // mechanic roadmap: one new mechanic per chapter, always on level X2 (roads 12, lights 22, portals 32, drills 42, frozen 46)
            int pos = level % 10;
            bool On(int intro) => level == intro || (level > intro && pos != 1 && pos != 9 && (level * 7 + intro) % 3 != 0);
            bool tutorial = level == 12 || level == 22 || level == 32 || level == 42 || level == 46;
            if (tutorial)
            {
                // a tiny board that shows only the new mechanic
                shape = "Square"; n = level == 46 ? 7 : (level == 12 ? 9 : 6); p.require = true;
                p.minLen = 3; p.maxLen = 4; p.turn = 0.15f; p.fill = 0.8f;
                p.cap = level == 46 ? 8 : (level == 12 ? 3 : 4);
                if (level == 12) p.roads = 1;
                if (level == 22) p.lights = 1;
                if (level == 32) p.portalPairs = 1;
                if (level == 42) p.drills = 1;
                if (level == 46) p.frozen = 1;
            }
            else
            {
                // bridges are switched off for now (a road with underpass art will replace them)
                if (On(12)) p.roads = Mathf.Min(3, 1 + (level - 12) / 40);
                if (On(22)) p.lights = Mathf.Min(3, 1 + (level - 22) / 40);
                if (On(32)) p.portalPairs = 1 + (level >= 60 ? 1 : 0);
                if (On(42)) p.drills = 1 + (level - 42) / 40;
                if (On(46)) p.frozen = Mathf.Min(1 + (level - 46) / 25, 4);
            }
            var data = Generate(shape, n, level * 7919 + 13, p, level);
            SetMargin(data, MarginFor(level));
            return data;
        }

        // The 10 level sawtooth: extra moves = allowed moves - minimum moves.
        // X1 cool down +4, X2 tutorial +4, X3-X4 +3, X5 spike +1, X6 +3, X7 +2, X8 pre-boss test +1, X9 confidence +3, X0 boss 0 to +1.
        public static int MarginFor(int level)
        {
            int pos = level % 10;
            int m;
            switch (pos)
            {
                case 1: case 2: m = 4; break;
                case 3: case 4: m = 3; break;
                case 5: m = 1; break;
                case 6: m = 3; break;
                case 7: m = 2; break;
                case 8: m = 1; break;
                case 9: m = 3; break;
                default: m = level <= 20 ? 1 : 0; break;
            }
            if (level <= 9) m = Mathf.Max(m, level <= 3 ? 5 : 3);     // the very first levels stay friendly
            return m;
        }

        static void SetMargin(LevelData d, int margin)
        {
            d.minMoves = d.snakes.Count;      // one tap per snake when played perfectly
            d.moves = d.minMoves + margin + d.lights.Count * 2;
        }

        // ---------------------------------------------------------------- test / showcase levels
        public static readonly string[] SpecialKeys = { "portal", "drill", "frozen", "light", "square", "flower", "road", "pieces", "castle", "elephant", "giant", "long", "all" };
        public static readonly string[] SpecialTitles = { "Portal test", "Drill test", "Frozen test", "Light test", "Square", "Flower", "Zigzag road", "Road pieces", "Castle", "Elephant", "Giant map", "Long snakes", "All mechanics" };

        static SnakeDef S(string coords, int color, bool drill = false)
        {
            var d = new SnakeDef { colorIdx = color, drill = drill };
            foreach (var part in coords.Split(' '))
            {
                var xy = part.Split(',');
                d.cells.Add(new Vector2Int(int.Parse(xy[0]), int.Parse(xy[1])));
            }
            return d;
        }

        static LevelData Hand(int w, int h, string title)
        {
            var d = new LevelData { level = 0, w = w, h = h, mask = new bool[w, h], shapeName = "Rect", title = title };
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) d.mask[x, y] = true;
            return d;
        }

        public static LevelData BuildSpecial(string key, int seed = 0)
        {
            LevelData d;
            switch (key)
            {
                case "bridge":
                    d = Hand(7, 5, "Bridge test");
                    d.bridges.Add(new Vector2Int(3, 2));
                    d.bridgeAxis[new Vector2Int(3, 2)] = 1;
                    d.snakes.Add(S("3,4 3,3 3,2 3,1 3,0", 0));   // vertical, lies on the bridge
                    d.snakes.Add(S("2,2 1,2 0,2", 1));            // horizontal, passes over the other one
                    d.snakes.Add(S("4,4 4,3", 2));
                    d.snakes.Add(S("6,3 6,4", 3));
                    break;
                case "portal":
                    d = Generate("Circle", 9, 1111 + seed, new GenParams { minLen = 5, maxLen = 9, turn = 0.3f, fill = 0.95f, portalPairs = 2, require = true }, 0);
                    d.title = "Portal test"; break;
                case "drill":
                    d = Generate("Square", 8, 2222 + seed, new GenParams { minLen = 5, maxLen = 9, turn = 0.3f, fill = 0.95f, drills = 2, require = true }, 0);
                    d.title = "Drill test"; break;
                case "frozen":
                    {
                        d = Generate("Circle", 9, 4242 + seed, new GenParams { minLen = 5, maxLen = 9, turn = 0.3f, fill = 0.95f, frozen = 2, require = true }, 0);
                        d.title = "Frozen test";
                        if (!d.HasFrozen && d.snakes.Count > 3) d.snakes[0].thaw = 2;
                        break;
                    }
                case "square":
                    d = Generate("Square", 12, 100 + seed, new GenParams { minLen = 6, maxLen = 16, turn = 0.55f, fill = 0.98f }, 0);
                    d.title = "Square"; break;
                case "flower":
                    {
                        var walls = new List<Vector2Int> { new Vector2Int(8, 8), new Vector2Int(9, 8), new Vector2Int(8, 9), new Vector2Int(9, 9) };
                        d = Generate("Flower", 18, 200 + seed, new GenParams { minLen = 7, maxLen = 16, turn = 0.35f, fill = 0.98f }, 0, walls);
                        d.title = "Flower"; break;
                    }
                case "road":
                    {
                        var top = new List<Vector2Int> { new Vector2Int(1, 9), new Vector2Int(1, 13), new Vector2Int(4, 13), new Vector2Int(4, 10), new Vector2Int(7, 10), new Vector2Int(7, 13), new Vector2Int(10, 13), new Vector2Int(10, 10), new Vector2Int(13, 10), new Vector2Int(13, 13) };
                        var low = new List<Vector2Int> { new Vector2Int(1, 6), new Vector2Int(1, 3), new Vector2Int(4, 3), new Vector2Int(4, 5), new Vector2Int(7, 5), new Vector2Int(7, 3), new Vector2Int(10, 3), new Vector2Int(10, 5), new Vector2Int(13, 5), new Vector2Int(13, 3) };
                        d = Generate("Road", 20, 300 + seed, new GenParams { minLen = 7, maxLen = 16, turn = 0.14f, fill = 0.98f, roadPaths = new List<List<Vector2Int>> { top, low } }, 0);
                        d.title = "Zigzag road"; break;
                    }
                case "castle":
                    d = Generate("Castle", 27, 400 + seed, new GenParams { minLen = 10, maxLen = 46, turn = 0.1f, fill = 0.88f }, 0);
                    d.title = "Castle"; break;
                case "elephant":
                    d = Generate("ElephantBig", 46, 450 + seed, new GenParams { minLen = 8, maxLen = 34, turn = 0.42f, fill = 0.98f }, 0);
                    d.title = "Elephant";
                    foreach (var sn in d.snakes)
                    {
                        float cx = 0, cy = 0; foreach (var c in sn.cells) { cx += c.x; cy += c.y; }
                        cx /= sn.cells.Count; cy /= sn.cells.Count;
                        sn.colorIdx = (cy >= 22f && cx >= 37f) ? 3 : (cy < 8f ? 4 : (cx < 7f ? 0 : 1));   // trunk yellow, legs violet, tail red, body blue
                    }
                    break;
                case "pieces":
                    d = Generate("Square", 44, 800 + seed, new GenParams { minLen = 7, maxLen = 14, turn = 0.3f, fill = 0.98f,
                        pieceKeys = new[] { "rp_h1:0", "rp_h2:0", "rp_h3:0", "rp_h1:1", "rp_h2:1", "rp_h3:1", "rp_l:0", "rp_lr:0", "rp_u:0", "rp_s:0", "rp_z:0", "rp_c:0", "rp_m:0", "rp_w:0" } }, 0);
                    d.title = "Road pieces"; d.giant = true; break;
                case "light":
                    d = Generate("Diamond", 9, 3333 + seed, new GenParams { minLen = 5, maxLen = 9, turn = 0.3f, fill = 0.95f, lights = 2, require = true }, 0);
                    d.title = "Light test"; break;
                case "giant":
                    d = Generate("Square", 40, 700 + seed, new GenParams { minLen = 6, maxLen = 30, turn = 0.3f, fill = 0.93f, portalPairs = 3, roads = 4, lights = 4, drills = 3 }, 0);
                    d.title = "Giant map"; d.giant = true; break;
                case "long":
                    d = Generate("Square", 14, 500 + seed, new GenParams { minLen = 14, maxLen = 40, turn = 0.08f, fill = 0.9f }, 0);
                    d.title = "Long snakes"; break;
                default:
                    d = Generate("Diamond", 14, 600 + seed, new GenParams { minLen = 6, maxLen = 14, turn = 0.35f, fill = 0.98f, portalPairs = 1, drills = 2, frozen = 2, roads = 2 }, 90);
                    d.title = "All mechanics"; break;
            }
            for (int i = 0; i < d.snakes.Count; i++) if (key == "bridge" || key == "elephant") { } else d.snakes[i].colorIdx = Pick(i, 1);
            SetMoves(d, false, true);
            if (d.moves < 20) d.moves = 20;
            return d;
        }
    }
}
