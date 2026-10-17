namespace AVAKnightLore.Engine;

/// <summary>
/// The computer Sabreman, for the demonstration and the tests. It knows the castle's plan and where the charms
/// lie. It makes for the charm the wizard wants (or the cauldron, with it in its pack), room by room by the
/// fewest doors, picking up any charm in a room it passes through. In a room it knows every place it can stand
/// and where each walk or jump takes it (the room's <see cref="Graph"/>), and plans the quickest way, keeping
/// off the guards' lines where it can. Before each move it plays the move out on a copy of the castle, guards
/// and spiked balls and all, and if it would be caught it waits for the moment it wouldn't. By night it keeps out
/// of the wizard's room.
/// </summary>
public sealed class Pilot
{
    private readonly Queue<Controls> _plan = new();
    private readonly Dictionary<int, (Graph Graph, HashSet<Spot> Lines)> _maps = [];
    private CastleDef? _for;
    private int _lining;
    public const int WaitStep = 6, MaxWait = 240, Settle = 20, LineCost = 600;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _plan.Clear();
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var h = w.Hero;
        if (h.Dead || w.Changing > 0) { _plan.Clear(); return default; }
        if (_plan.Count > 0) return _plan.Dequeue();
        if (h.Airborne) return default;
        if (_for != w.Castle) { _maps.Clear(); _for = w.Castle; }
        var c = w.Castle;
        var r = w.Here;
        // Where to: the wanted charm, or the cauldron with it; by night, out of the wizard's way.
        int goal;
        bool toCauldron = w.WantedNow is { } want && w.Carried.Contains(want);
        if (toCauldron) goal = c.CauldronRoom;
        else goal = c.Rooms.FirstOrDefault(x => x.Charm == w.WantedNow && Sim.Present(w, x))?.Index ?? c.CauldronRoom;
        bool dark = w.Night || Sim.DayLength - w.DayTime < 5 && !w.Night;
        if (dark && goal == c.CauldronRoom)
        {
            // Wait out the night next door (or pick up another charm meanwhile).
            var other = c.Rooms.Where(x => x.Charm != null && Sim.Present(w, x) && x.Index != c.CauldronRoom).OrderBy(x => c.Distances(w.Room)[x.Index]).FirstOrDefault();
            goal = other?.Index ?? -1;
            if (goal < 0)
            {
                if (r.Cauldron)
                {
                    // Out by the nearest door.
                    var door = Enumerable.Range(0, 4).Where(s => c.Through(w.Room, (Side)s) >= 0)
                        .OrderBy(s => { var (x, y) = RoomDef.DoorCell((Side)s); return Math.Abs(x + 0.5 - h.X) + Math.Abs(y + 0.5 - h.Y); }).First();
                    goal = c.Through(w.Room, (Side)door);
                }
                else { Doing = "waiting for the day"; return default; }
            }
        }
        var targets = new List<Spot>();
        Side? exit = null;
        // A charm in this room is picked up on the way, whatever it is.
        if (r.Charm != null && Sim.Present(w, r) && !(dark && r.Cauldron))
            targets.Add(new Spot(r.CharmAt.X, r.CharmAt.Y, (int)Math.Max(0, r.Top(r.CharmAt.X, r.CharmAt.Y))));
        else if (goal == w.Room && r.Cauldron && toCauldron)
        {
            for (int s = 0; s < 4; s++)
            {
                var (ox, oy) = RoomDef.Out((Side)s);
                targets.Add(new Spot(Field.Mid + ox, Field.Mid + oy, 0));
            }
        }
        else if (goal != w.Room)
        {
            // (By night, round the wizard's room rather than through it.)
            var d = c.Distances(goal, dark && goal != c.CauldronRoom ? c.CauldronRoom : -1);
            if (d[w.Room] < 0) d = c.Distances(goal);
            for (int s = 0; s < 4; s++)
            {
                int n = c.Through(w.Room, (Side)s);
                if (n >= 0 && d[n] >= 0 && d[n] < d[w.Room]) { exit = (Side)s; break; }
            }
            if (exit is { } e)
            {
                var (x, y) = RoomDef.DoorCell(e);
                targets.Add(new Spot(x, y, 0));
            }
        }
        if (targets.Count == 0) { Doing = "nothing to do"; return default; }
        var here = Spot.Of(h);
        // At the door: straight out through it.
        if (exit is { } way && here == targets[0])
        {
            Doing = "through the door";
            var (ox, oy) = RoomDef.Out(way);
            for (int i = 0; i < 30; i++) _plan.Enqueue(new Controls(ox, oy));
            return _plan.Dequeue();
        }
        // Half out through a doorway: on out.
        if (h.X < 0 || h.Y < 0 || h.X > Field.N || h.Y > Field.N)
        {
            Doing = "through the door";
            int ox = h.X < 0 ? -1 : h.X > Field.N ? 1 : 0, oy = ox != 0 ? 0 : h.Y < 0 ? -1 : 1;
            for (int i = 0; i < 12; i++) _plan.Enqueue(new Controls(ox, oy));
            return _plan.Dequeue();
        }
        var (graph, lines) = Map(r);
        if (!graph.Edges.ContainsKey(here))
        {
            // Off the map (between squares): back to the middle of the one underfoot.
            Doing = "stepping on";
            double tx = Math.Floor(h.X) + 0.5, ty = Math.Floor(h.Y) + 0.5;
            int dx = Math.Abs(tx - h.X) > 0.05 ? Math.Sign(tx - h.X) : 0, dy = dx == 0 && Math.Abs(ty - h.Y) > 0.05 ? Math.Sign(ty - h.Y) : 0;
            if (dx != 0 || dy != 0)
            {
                for (int i = 0; i < 6; i++) _plan.Enqueue(new Controls(dx, dy));
                return _plan.Dequeue();
            }
        }
        var far = Distances(graph, targets, lines);
        int now = far.TryGetValue(here, out int hh) ? hh : int.MaxValue;
        if ((Math.Abs(h.X - here.Cx) > 0.06 || Math.Abs(h.Y - here.Cy) > 0.06) && _lining < 8)
        {
            // Off the middle of the square (every move is planned from a square's middle): into the middle.
            _lining++;
            Doing = "lining up";
            bool alongX = Math.Abs(here.Cx - h.X) > 0.06;
            double off = alongX ? here.Cx - h.X : here.Cy - h.Y;
            int steps = Math.Clamp((int)Math.Round(Math.Abs(off) / (Hero.Speed * Field.Step)), 1, 6);
            for (int i = 0; i < steps; i++) _plan.Enqueue(alongX ? new Controls(Math.Sign(off), 0) : new Controls(0, Math.Sign(off)));
            return _plan.Dequeue();
        }
        _lining = 0;
        var options = new List<(Move Move, int Cost)>();
        if (graph.Edges.TryGetValue(here, out var edges))
            foreach (var (mv, to, ticks) in edges)
                if (far.TryGetValue(to, out int dd) && (dd < now || now == int.MaxValue)) options.Add((mv, ticks + dd + (lines.Contains(to) ? LineCost : 0)));
        options.Sort((a, b) => a.Cost.CompareTo(b.Cost));
        int best = int.MaxValue;
        List<Controls>? pick = null;
        int pickWait = 0;
        foreach (var (mv, cost) in options.Take(3))
            for (int wait = 0; wait <= MaxWait && wait + cost < best; wait += WaitStep)
            {
                var run = Rehearse(w, mv, wait);
                if (run == null) continue;
                if (wait + cost < best) { best = wait + cost; pick = run; pickWait = wait; }
                break;
            }
        if (pick == null)
        {
            // Nothing safe on the way: anything at all that keeps him alive.
            if (graph.Edges.TryGetValue(here, out var all))
                foreach (var (mv, _, _) in all)
                    if (Rehearse(w, mv, 0) is { } run) { pick = run; break; }
        }
        if (pick == null)
        {
            Doing = "stuck";
            for (int i = 0; i < WaitStep; i++) _plan.Enqueue(default);
            return _plan.Dequeue();
        }
        Doing = pickWait > 0 ? "waiting" : toCauldron ? "to the cauldron" : "after a charm";
        if (pickWait > 0)
        {
            for (int i = 0; i < Math.Min(pickWait, WaitStep); i++) _plan.Enqueue(default);
            return _plan.Dequeue();
        }
        foreach (var ctl in pick) _plan.Enqueue(ctl);
        return _plan.Count > 0 ? _plan.Dequeue() : default;
    }

    private (Graph, HashSet<Spot>) Map(RoomDef r)
    {
        if (_maps.TryGetValue(r.Index, out var m)) return m;
        var lines = new HashSet<Spot>();
        foreach (var g in r.Guards)
            for (int k = (int)g.Min; k <= (int)g.Max; k++)
            {
                int x = g.AlongX ? k : (int)g.Fixed, y = g.AlongX ? (int)g.Fixed : k;
                if (x >= 0 && y >= 0 && x < Field.N && y < Field.N && r.Block[x, y] == 0) lines.Add(new Spot(x, y, 0));
            }
        foreach (var b in r.Balls) lines.Add(new Spot(b.X, b.Y, 0));
        return _maps[r.Index] = (Graph.Build(r), lines);
    }

    /// <summary>A move played out on a copy after a wait, then a moment standing: its controls if he comes through it.</summary>
    private static List<Controls>? Rehearse(World start, Move mv, int wait)
    {
        var w = start.Copy();
        int room = w.Room;
        for (int i = 0; i < wait; i++)
        {
            Sim.Step(w, default);
            if (w.Hero.Dead || w.Hero.Airborne || w.Changing > 0) return null;
        }
        var controls = new List<Controls>();
        foreach (var c in Graph.Script(w, mv))
        {
            controls.Add(c);
            Sim.Step(w, c);
            if (w.Hero.Dead) return null;
            if (w.Room != room) return controls;
        }
        for (int i = 0; i < Settle; i++)
        {
            Sim.Step(w, default);
            if (w.Hero.Dead) return null;
        }
        return controls;
    }

    /// <summary>How long (in steps) from every spot to the nearest of the targets, going backwards from them.</summary>
    private static Dictionary<Spot, int> Distances(Graph g, List<Spot> targets, HashSet<Spot> lines)
    {
        var back = new Dictionary<Spot, List<(Spot From, int Ticks)>>();
        foreach (var (from, list) in g.Edges)
            foreach (var (_, to, ticks) in list)
            {
                if (!back.TryGetValue(to, out var l)) back[to] = l = [];
                l.Add((from, ticks));
            }
        var dist = new Dictionary<Spot, int>();
        var pq = new PriorityQueue<Spot, int>();
        foreach (var t in targets) { dist[t] = 0; pq.Enqueue(t, 0); }
        while (pq.TryDequeue(out var s, out int d))
        {
            if (d > dist[s]) continue;
            if (!back.TryGetValue(s, out var l)) continue;
            foreach (var (from, ticks) in l)
            {
                int nd = d + ticks + (lines.Contains(s) ? LineCost : 0);
                if (dist.TryGetValue(from, out int od) && od <= nd) continue;
                dist[from] = nd;
                pq.Enqueue(from, nd);
            }
        }
        return dist;
    }
}
