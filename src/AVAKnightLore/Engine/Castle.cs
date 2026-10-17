namespace AVAKnightLore.Engine;

/// <summary>
/// The controls: four ways to walk (along the room's two sides; up the screen is away from you, -1) and jump.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Jump = false);

/// <summary>The screen, and a room: nine squares by nine.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int N = 9;
    /// <summary>The middle square of each side, where the doors are.</summary>
    public const int Mid = 4;
}

public enum Floor : byte
{
    Stone,
    /// <summary>Spikes up through the floor: death to step on.</summary>
    Spikes,
    /// <summary>No floor at all: a drop into the dark.</summary>
    Pit,
}

/// <summary>Which wall: north is away up the screen (-y), then round clockwise.</summary>
public enum Side { North, East, South, West }

/// <summary>The charms the wizard needs for his cure.</summary>
public enum Charm { Gem, Chalice, Bottle, Boot, Diamond, Poison, Bell, Crown }

/// <summary>A guard pacing up and down a line of squares (along x, or along y), its feet on the floor.</summary>
public readonly record struct GuardDef(bool AlongX, double Fixed, double Min, double Max, double Speed, int Look);

/// <summary>A spiked ball bouncing up and down on one square.</summary>
public readonly record struct BallDef(int X, int Y, double Period, double Phase);

/// <summary>A room: its blocks and floor, its doors, what moves in it, and the charm (or the cauldron) in it.</summary>
public sealed class RoomDef
{
    public int Index, Col, Row;
    /// <summary>How many blocks are stacked on each square (0 for bare floor).</summary>
    public int[,] Block = new int[Field.N, Field.N];
    public Floor[,] Floor = new Floor[Field.N, Field.N];
    public bool[] Doors = new bool[4];
    public List<GuardDef> Guards = [];
    public List<BallDef> Balls = [];
    public Charm? Charm;
    public (int X, int Y) CharmAt;
    public bool Cauldron;
    public int Style;

    /// <summary>The top of whatever stands on a square: its blocks, the floor, or (a pit) nothing at all.</summary>
    public double Top(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Field.N || y >= Field.N) return 99;
        if (Block[x, y] > 0) return Block[x, y];
        return Floor[x, y] == Engine.Floor.Pit ? -100 : 0;
    }

    public static (int X, int Y) DoorCell(Side s) => s switch
    {
        Side.North => (Field.Mid, 0),
        Side.East => (Field.N - 1, Field.Mid),
        Side.South => (Field.Mid, Field.N - 1),
        _ => (0, Field.Mid),
    };

    public static (int Dx, int Dy) Out(Side s) => s switch
    {
        Side.North => (0, -1),
        Side.East => (1, 0),
        Side.South => (0, 1),
        _ => (-1, 0),
    };

    public static Side Opposite(Side s) => (Side)(((int)s + 2) % 4);
}

/// <summary>A castle: its rooms on a grid, where Sabreman starts, the wizard's cauldron, and the charms it needs.</summary>
public sealed class CastleDef
{
    public int Level, Cols, Rows;
    public string Name = "";
    public List<RoomDef> Rooms = [];
    public int Start, CauldronRoom;
    /// <summary>The charms the wizard will ask for, in the order he'll ask.</summary>
    public List<Charm> Wanted = [];

    public RoomDef Room(int col, int row) => Rooms[row * Cols + col];

    public int Through(int room, Side s)
    {
        var r = Rooms[room];
        if (!r.Doors[(int)s]) return -1;
        var (dx, dy) = RoomDef.Out(s);
        int c = r.Col + dx, w = r.Row + dy;
        return c < 0 || w < 0 || c >= Cols || w >= Rows ? -1 : w * Cols + c;
    }

    /// <summary>How many doors from every room to one (going round a room to be kept out of, if any).</summary>
    public int[] Distances(int to, int avoid = -1)
    {
        var d = Enumerable.Repeat(-1, Rooms.Count).ToArray();
        d[to] = 0;
        var q = new Queue<int>([to]);
        while (q.Count > 0)
        {
            int r = q.Dequeue();
            for (int s = 0; s < 4; s++)
            {
                int n = Through(r, (Side)s);
                if (n >= 0 && d[n] < 0 && n != avoid) { d[n] = d[r] + 1; q.Enqueue(n); }
            }
        }
        return d;
    }
}

/// <summary>The castles, each larger and with more charms to find than the last.</summary>
public static class CastleBook
{
    private static readonly (string Name, int Cols, int Rows, int Charms)[] Plans =
    [
        ("THE CASTLE OF MELKHIOR", 4, 4, 5),
        ("THE TOWER OF NIGHT", 5, 4, 6),
        ("THE KEEP OF THE CURSE", 5, 5, 7),
    ];

    public static int Count => Plans.Length;

    public static CastleDef Build(int level, int seed = 0)
    {
        var plan = Plans[level % Plans.Length];
        for (int attempt = 0; ; attempt++)
        {
            var c = Try(level, plan, 4000 + level * 211 + seed * 13 + attempt * 7919);
            if (c != null) return c;
        }
    }

    private static CastleDef? Try(int level, (string Name, int Cols, int Rows, int Charms) plan, int seed)
    {
        var d = new Dice(seed);
        var c = new CastleDef { Level = level, Name = plan.Name, Cols = plan.Cols, Rows = plan.Rows };
        for (int r = 0; r < plan.Rows; r++)
        for (int k = 0; k < plan.Cols; k++)
            c.Rooms.Add(new RoomDef { Index = r * plan.Cols + k, Col = k, Row = r, Style = d.Next(4) });
        // A maze through every room, then a few more doors.
        var seen = new bool[c.Rooms.Count];
        var stack = new Stack<int>([0]);
        seen[0] = true;
        while (stack.Count > 0)
        {
            var room = c.Rooms[stack.Peek()];
            var ways = new List<Side>();
            for (int s = 0; s < 4; s++)
            {
                var (dx, dy) = RoomDef.Out((Side)s);
                int nc = room.Col + dx, nr = room.Row + dy;
                if (nc >= 0 && nr >= 0 && nc < c.Cols && nr < c.Rows && !seen[nr * c.Cols + nc]) ways.Add((Side)s);
            }
            if (ways.Count == 0) { stack.Pop(); continue; }
            var way = ways[d.Next(ways.Count)];
            Open(c, room.Index, way);
            int n = c.Through(room.Index, way);
            seen[n] = true;
            stack.Push(n);
        }
        foreach (var room in c.Rooms)
            foreach (var s in new[] { Side.East, Side.South })
            {
                var (dx, dy) = RoomDef.Out(s);
                if (room.Col + dx < c.Cols && room.Row + dy < c.Rows && !room.Doors[(int)s] && d.Next(100) < 30) Open(c, room.Index, s);
            }
        // The start in a corner, the cauldron in the middle of the castle.
        c.Start = 0;
        c.CauldronRoom = (c.Rows / 2) * c.Cols + c.Cols / 2;
        c.Rooms[c.CauldronRoom].Cauldron = true;
        // The charms, in rooms well apart, never the start's or the cauldron's.
        var charms = Enum.GetValues<Charm>().OrderBy(_ => d.Next()).Take(plan.Charms).ToList();
        var places = Enumerable.Range(0, c.Rooms.Count).Where(i => i != c.Start && i != c.CauldronRoom).OrderBy(_ => d.Next()).Take(plan.Charms).ToList();
        if (places.Count < plan.Charms) return null;
        for (int i = 0; i < charms.Count; i++) c.Rooms[places[i]].Charm = charms[i];
        c.Wanted = charms.OrderBy(_ => d.Next()).ToList();
        // Fill each room with something to get past, and prove it can be got past.
        foreach (var room in c.Rooms)
        {
            bool ok = false;
            for (int tries = 0; tries < 40 && !ok; tries++) ok = Furnish(room, ref d, level, room.Index == c.Start || room.Cauldron ? 0 : 1 + d.Next(5));
            if (!ok && !Furnish(room, ref d, level, 0)) return null;
        }
        return c;
    }

    private static void Open(CastleDef c, int room, Side s)
    {
        c.Rooms[room].Doors[(int)s] = true;
        int n = c.Through(room, s);
        c.Rooms[n].Doors[(int)RoomDef.Opposite(s)] = true;
    }

    /// <summary>
    /// A room's contents: 0 a plain hall with pillars, 1 a field of spikes with stepping blocks, 2 a floor full of
    /// holes, 3 steps and ledges, 4 guards, 5 bouncing spiked balls. Kept only if every door, and the charm, can be
    /// reached from every door.
    /// </summary>
    private static bool Furnish(RoomDef r, ref Dice d, int level, int kind)
    {
        const int n = Field.N;
        Array.Clear(r.Block);
        Array.Clear(r.Floor);
        r.Guards.Clear();
        r.Balls.Clear();
        bool Reserved(int x, int y)
        {
            // The doorways and the square inside each, kept clear.
            foreach (Side s in Enum.GetValues<Side>())
            {
                var (dx, dy) = RoomDef.DoorCell(s);
                var (ox, oy) = RoomDef.Out(s);
                if (x == dx && y == dy || x == dx - ox && y == dy - oy) return true;
            }
            return r.Cauldron && Math.Abs(x - Field.Mid) <= 1 && Math.Abs(y - Field.Mid) <= 1;
        }
        switch (kind)
        {
            case 0:
                foreach (var (x, y) in new[] { (1, 1), (7, 1), (1, 7), (7, 7) })
                    if (d.Next(3) > 0) r.Block[x, y] = 3;
                break;
            case 1:
                for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    if (!Reserved(x, y) && d.Next(100) < 34) r.Floor[x, y] = Floor.Spikes;
                for (int k = 0; k < 5; k++)
                {
                    int x = 1 + d.Next(n - 2), y = 1 + d.Next(n - 2);
                    if (!Reserved(x, y)) { r.Block[x, y] = 1; r.Floor[x, y] = Floor.Stone; }
                }
                break;
            case 2:
                for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    if (!Reserved(x, y) && d.Next(100) < 30) r.Floor[x, y] = Floor.Pit;
                break;
            case 3:
            {
                // A raised walk of blocks, with steps up to it.
                int y0 = 2 + d.Next(5);
                for (int x = 1; x < n - 1; x++)
                    if (!Reserved(x, y0)) r.Block[x, y0] = x % 3 == 0 ? 1 : 2;
                for (int k = 0; k < 4; k++)
                {
                    int x = 1 + d.Next(n - 2), y = 1 + d.Next(n - 2);
                    if (!Reserved(x, y) && r.Block[x, y] == 0) r.Block[x, y] = 1 + d.Next(2);
                }
                break;
            }
            case 4:
            {
                int guards = 1 + Math.Min(2, level + d.Next(2));
                for (int k = 0; k < guards; k++)
                {
                    bool alongX = d.Next(2) == 0;
                    double line = 1.5 + d.Next(n - 2);
                    r.Guards.Add(new GuardDef(alongX, line, 0.8, n - 0.8, 1.4 + level * 0.25 + d.Next() * 0.6, d.Next(3)));
                }
                // Something to stand on out of their way.
                for (int k = 0; k < 3; k++)
                {
                    int x = 1 + d.Next(n - 2), y = 1 + d.Next(n - 2);
                    if (!Reserved(x, y)) r.Block[x, y] = 1;
                }
                break;
            }
            default:
            {
                int balls = 2 + Math.Min(3, level + d.Next(2));
                for (int k = 0; k < balls; k++)
                {
                    int x = 1 + d.Next(n - 2), y = 1 + d.Next(n - 2);
                    if (!Reserved(x, y)) r.Balls.Add(new BallDef(x, y, 1.6 + d.Next() * 0.8, d.Next()));
                }
                break;
            }
        }
        if (r.Cauldron) r.Block[Field.Mid, Field.Mid] = 1;
        // The charm: on the floor, or up on a block.
        if (r.Charm != null)
        {
            var spots = new List<(int, int)>();
            for (int x = 1; x < n - 1; x++)
            for (int y = 1; y < n - 1; y++)
                if (!Reserved(x, y) && r.Floor[x, y] == Floor.Stone && r.Block[x, y] <= 2) spots.Add((x, y));
            if (spots.Count == 0) return false;
            r.CharmAt = spots[d.Next(spots.Count)];
        }
        return Proven(r);
    }

    /// <summary>Every door, and the charm, reached from every door (keeping off any guard's line).</summary>
    public static bool Proven(RoomDef r)
    {
        var avoid = new HashSet<Spot>();
        foreach (var g in r.Guards)
            for (int k = (int)g.Min; k <= (int)g.Max; k++)
            {
                int x = g.AlongX ? k : (int)g.Fixed, y = g.AlongX ? (int)g.Fixed : k;
                if (x >= 0 && y >= 0 && x < Field.N && y < Field.N && r.Block[x, y] == 0) avoid.Add(new Spot(x, y, 0));
            }
        var graph = Graph.Build(r, avoid.Count > 0 ? avoid : null);
        var targets = new List<Spot>();
        for (int s = 0; s < 4; s++)
            if (r.Doors[s])
            {
                var (x, y) = RoomDef.DoorCell((Side)s);
                targets.Add(new Spot(x, y, 0));
            }
        if (r.Charm != null) targets.Add(new Spot(r.CharmAt.X, r.CharmAt.Y, (int)Math.Max(0, r.Top(r.CharmAt.X, r.CharmAt.Y))));
        if (r.Cauldron)
            for (int s = 0; s < 4; s++)
            {
                var (ox, oy) = RoomDef.Out((Side)s);
                targets.Add(new Spot(Field.Mid + ox, Field.Mid + oy, 0));
            }
        if (targets.Count == 0) return true;
        foreach (var t in targets)
        {
            if (!graph.Edges.ContainsKey(t)) return false;
            var reach = graph.Reach(t);
            if (!targets.All(reach.Contains)) return false;
        }
        return true;
    }
}
