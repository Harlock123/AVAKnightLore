namespace AVAKnightLore.Engine;

/// <summary>A move from one standing place to another: a walk to the next square, or a jump (in place, or one of four ways).</summary>
public readonly record struct Move(int Dx, int Dy, bool Jump);

/// <summary>Where he stands: a square, and the height of what's under him there.</summary>
public readonly record struct Spot(int X, int Y, int Z)
{
    public double Cx => X + 0.5;
    public double Cy => Y + 0.5;
    public static Spot Of(Hero h) => new((int)Math.Floor(h.X), (int)Math.Floor(h.Y), (int)Math.Round(h.Z));
}

/// <summary>
/// Every place he can stand in a room, and where each move takes him from there, found by trying the moves out
/// (with nothing moving about) in the game's own physics. The rooms are kept only if this shows their doors and
/// charms can be got to; the computer player plans its route by it.
/// </summary>
public sealed class Graph
{
    public static readonly Move[] Moves =
    [
        new(1, 0, false), new(-1, 0, false), new(0, 1, false), new(0, -1, false),
        new(1, 0, true), new(-1, 0, true), new(0, 1, true), new(0, -1, true),
    ];

    public readonly Dictionary<Spot, List<(Move Move, Spot To, int Ticks)>> Edges = [];

    public static Graph Build(RoomDef r, HashSet<Spot>? avoid = null)
    {
        var g = new Graph();
        var c = new CastleDef { Cols = 1, Rows = 1, Rooms = [r] };
        for (int x = 0; x < Field.N; x++)
        for (int y = 0; y < Field.N; y++)
        {
            double top = r.Top(x, y);
            if (top < 0 || top > 3 || r.Block[x, y] == 0 && r.Floor[x, y] == Floor.Spikes) continue;
            var s = new Spot(x, y, (int)top);
            if (avoid != null && avoid.Contains(s)) continue;
            var list = new List<(Move, Spot, int)>();
            foreach (var mv in Moves)
                if (Try(c, s, mv) is { } to && (avoid == null || !avoid.Contains(to.Spot))) list.Add((mv, to.Spot, to.Ticks));
            g.Edges[s] = list;
        }
        return g;
    }

    /// <summary>A move tried from a spot: where it ends, standing (and alive), and how long it took.</summary>
    public static (Spot Spot, int Ticks)? Try(CastleDef c, Spot s, Move mv)
    {
        var w = new World { Castle = c, Room = 0, Probe = true };
        w.Hero = new Hero { X = s.Cx, Y = s.Cy, Z = s.Z };
        int ticks = 0;
        foreach (var ctl in Script(w, mv))
        {
            Sim.Step(w, ctl);
            ticks++;
            if (w.Hero.Dead || ticks > 200) return null;
        }
        if (w.Hero.Airborne) return null;
        var end = Spot.Of(w.Hero);
        if (end == s) return null;
        return (end, ticks);
    }

    /// <summary>
    /// The controls for a move, one step at a time, stopping once he's standing again: a walk goes on to the middle
    /// of the next square (or off the edge and down); a jump goes until he lands.
    /// </summary>
    public static IEnumerable<Controls> Script(World w, Move mv)
    {
        var h = w.Hero;
        if (mv.Jump)
        {
            yield return new Controls(mv.Dx, mv.Dy, true);
            for (int i = 0; i < 200 && h.Airborne && !h.Dead; i++) yield return new Controls(mv.Dx, mv.Dy);
            yield break;
        }
        double tx = Math.Floor(h.X) + mv.Dx + 0.5, ty = Math.Floor(h.Y) + mv.Dy + 0.5;
        for (int i = 0; i < 200 && !h.Dead; i++)
        {
            if (h.Airborne) { yield return default; continue; }
            if (mv.Dx > 0 && h.X >= tx - 0.03 || mv.Dx < 0 && h.X <= tx + 0.03 || mv.Dy > 0 && h.Y >= ty - 0.03 || mv.Dy < 0 && h.Y <= ty + 0.03) yield break;
            double bx = h.X, by = h.Y;
            yield return new Controls(mv.Dx, mv.Dy);
            if (Math.Abs(h.X - bx) + Math.Abs(h.Y - by) < 0.001 && !h.Airborne) yield break;
        }
    }

    public HashSet<Spot> Reach(Spot from)
    {
        var seen = new HashSet<Spot> { from };
        var q = new Queue<Spot>(seen);
        while (q.Count > 0)
        {
            var s = q.Dequeue();
            if (!Edges.TryGetValue(s, out var list)) continue;
            foreach (var (_, to, _) in list)
                if (seen.Add(to)) q.Enqueue(to);
        }
        return seen;
    }
}
