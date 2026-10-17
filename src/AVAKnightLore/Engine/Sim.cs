namespace AVAKnightLore.Engine;

/// <summary>
/// The castle, step by step: Sabreman walking and jumping among the blocks (a jump is a fixed arc), falling,
/// guards pacing, spiked balls bouncing, spikes and pits; charms picked up and dropped into the cauldron; doors
/// into the next room; and the days and nights, when he becomes a wolf the wizard can't abide.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step, H = Hero.Half;
    public const double DayLength = 14, NightLength = 8, ChangeTime = 1.2;
    public const int Days = 40;
    public const double BallHeight = 1.7, WizardGrace = 2.5;

    public static void Start(World w, CastleDef c)
    {
        w.Castle = c;
        w.Carried.Clear();
        w.Delivered = 0;
        w.Clock = w.DayTime = 0;
        w.Day = 0;
        w.Night = false;
        w.Changing = 0;
        w.Cause = "";
        Enter(w, c.Start, null);
    }

    /// <summary>Into a room: through a door (just inside it), or (null) at the start, near the south wall.</summary>
    public static void Enter(World w, int room, Side? by)
    {
        w.Room = room;
        var r = w.Here;
        var h = w.Hero = new Hero { JumpPrev = w.Hero.JumpPrev };
        if (by is { } s)
        {
            var (x, y) = RoomDef.DoorCell(s);
            h.X = x + 0.5;
            h.Y = y + 0.5;
            var (ox, oy) = RoomDef.Out(s);
            (h.FaceX, h.FaceY) = (-ox, -oy);
        }
        else
        {
            h.X = Field.Mid + 0.5;
            h.Y = Field.N - 1.5;
        }
        h.Z = Math.Max(0, r.Top((int)h.X, (int)h.Y));
        w.Entry = (h.X, h.Y, h.Z);
        w.Guards = r.Guards.Select(g => new Guard { Def = g, Pos = (g.Min + g.Max) / 2 }).ToList();
        w.Wolf = 0;
    }

    /// <summary>Back where he came into the room, as if just arrived.</summary>
    public static void Restart(World w)
    {
        int room = w.Room;
        var (x, y, z) = w.Entry;
        w.Hero = new Hero { X = x, Y = y, Z = z };
        w.Guards = w.Here.Guards.Select(g => new Guard { Def = g, Pos = (g.Min + g.Max) / 2 }).ToList();
        w.Room = room;
        w.Cause = "";
        w.Wolf = 0;
    }

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        if (!w.Probe) Time(w);
        foreach (var g in w.Guards)
        {
            g.Pos += g.Dir * g.Def.Speed * Dt;
            if (g.Pos > g.Def.Max) { g.Pos = g.Def.Max; g.Dir = -1; }
            if (g.Pos < g.Def.Min) { g.Pos = g.Def.Min; g.Dir = 1; }
        }
        var h = w.Hero;
        if (h.Dead) return;
        if (w.Changing > 0) { w.Changing -= Dt; h.JumpPrev = c.Jump; return; }
        if (!h.Airborne) Ground(w, h, c);
        if (h.Airborne) Air(w, h);
        h.JumpPrev = c.Jump;
        if (!h.Dead) Touch(w, h);
        if (!h.Dead) Doors(w, h);
    }

    private static void Time(World w)
    {
        w.DayTime += Dt;
        if (!w.Night && w.DayTime >= DayLength)
        {
            w.Night = true;
            w.Changing = ChangeTime;
            w.Heard.Add(Sfx.Dusk);
        }
        else if (w.Night && w.DayTime >= DayLength + NightLength)
        {
            w.Night = false;
            w.Day++;
            w.DayTime = 0;
            w.Changing = ChangeTime;
            w.Heard.Add(Sfx.Dawn);
        }
    }

    /// <summary>The top of whatever is at a point: inside the room its square's, in a doorway the floor, else the wall.</summary>
    public static double TopAt(World w, double x, double y)
    {
        var r = w.Here;
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        if (ix >= 0 && iy >= 0 && ix < Field.N && iy < Field.N) return r.Top(ix, iy);
        // The way out through a door.
        if (iy == Field.Mid && ix < 0 && r.Doors[(int)Side.West]) return 0;
        if (iy == Field.Mid && ix >= Field.N && r.Doors[(int)Side.East]) return 0;
        if (ix == Field.Mid && iy < 0 && r.Doors[(int)Side.North]) return 0;
        if (ix == Field.Mid && iy >= Field.N && r.Doors[(int)Side.South]) return 0;
        return 99;
    }

    /// <summary>The highest thing under his feet.</summary>
    public static double Support(World w, double x, double y)
    {
        double s = -100;
        foreach (double fx in new[] { x - H + 0.02, x + H - 0.02 })
        foreach (double fy in new[] { y - H + 0.02, y + H - 0.02 })
            s = Math.Max(s, TopAt(w, fx, fy));
        return s;
    }

    private static bool Blocked(World w, double x, double y, double z)
    {
        foreach (double fx in new[] { x - H, x + H })
        foreach (double fy in new[] { y - H, y + H })
            if (TopAt(w, fx, fy) > z + 0.05) return true;
        return false;
    }

    private static void Ground(World w, Hero h, Controls c)
    {
        if (Support(w, h.X, h.Y) < h.Z - 0.01)
        {
            // Off an edge.
            h.Airborne = true;
            h.Vz = 0;
            h.Jdx = h.Jdy = 0;
            return;
        }
        int dx = c.Dx, dy = dx != 0 ? 0 : c.Dy;
        if (c.Jump && !h.JumpPrev)
        {
            h.Airborne = true;
            h.Vz = Hero.JumpSpeed;
            h.Jdx = dx;
            h.Jdy = dy;
            if (dx != 0 || dy != 0) (h.FaceX, h.FaceY) = (dx, dy);
            w.Heard.Add(Sfx.Jump);
            return;
        }
        if (dx == 0 && dy == 0) return;
        (h.FaceX, h.FaceY) = (dx, dy);
        h.Walk += Dt;
        double nx = h.X + dx * Hero.Speed * Dt, ny = h.Y + dy * Hero.Speed * Dt;
        if (!Blocked(w, nx, ny, h.Z)) { h.X = nx; h.Y = ny; return; }
        // Against a wall beside a doorway: slid into line with it.
        double door = Field.Mid + 0.5;
        var r = w.Here;
        bool doorX = dx > 0 && r.Doors[(int)Side.East] || dx < 0 && r.Doors[(int)Side.West];
        bool doorY = dy > 0 && r.Doors[(int)Side.South] || dy < 0 && r.Doors[(int)Side.North];
        if (doorX && Math.Abs(h.Y - door) < 0.6 && Math.Abs(h.Y - door) > 0.01)
        {
            double ty = h.Y + Math.Sign(door - h.Y) * Math.Min(Math.Abs(door - h.Y), Hero.Speed * Dt);
            if (!Blocked(w, h.X, ty, h.Z)) h.Y = ty;
        }
        if (doorY && Math.Abs(h.X - door) < 0.6 && Math.Abs(h.X - door) > 0.01)
        {
            double tx = h.X + Math.Sign(door - h.X) * Math.Min(Math.Abs(door - h.X), Hero.Speed * Dt);
            if (!Blocked(w, tx, h.Y, h.Z)) h.X = tx;
        }
    }

    private static void Air(World w, Hero h)
    {
        double nx = h.X + h.Jdx * Hero.Speed * Dt, ny = h.Y + h.Jdy * Hero.Speed * Dt;
        // Against a block's side in mid-air he keeps pressing on, and goes over once he's risen above it.
        if ((h.Jdx != 0 || h.Jdy != 0) && !Blocked(w, nx, ny, h.Z)) { h.X = nx; h.Y = ny; }
        double vz = h.Vz - Hero.Gravity * Dt, nz = h.Z + vz * Dt;
        if (vz <= 0)
        {
            double s = Support(w, h.X, h.Y);
            if (s > -50 && nz <= s && h.Z >= s - 0.05)
            {
                h.Z = s;
                h.Vz = 0;
                h.Airborne = false;
                w.Heard.Add(Sfx.Land);
                return;
            }
        }
        h.Vz = vz;
        h.Z = nz;
        if (h.Z < -1.6) Die(w, "FELL INTO THE DARK");
    }

    public static double BallZ(World w, BallDef b) => BallHeight * Math.Abs(Math.Sin(Math.PI * (w.Clock / b.Period + b.Phase)));

    private static void Touch(World w, Hero h)
    {
        var r = w.Here;
        int cx = (int)Math.Floor(h.X), cy = (int)Math.Floor(h.Y);
        if (!h.Airborne && h.Z < 0.05 && cx >= 0 && cy >= 0 && cx < Field.N && cy < Field.N && r.Block[cx, cy] == 0 && r.Floor[cx, cy] == Floor.Spikes)
        {
            Die(w, "SPIKED");
            return;
        }
        foreach (var g in w.Guards)
            if (Math.Abs(g.X - h.X) < 0.6 && Math.Abs(g.Y - h.Y) < 0.6 && h.Z < 1.0) { Die(w, "CAUGHT BY A GUARD"); return; }
        if (!w.Probe)
            foreach (var b in r.Balls)
            {
                double bz = BallZ(w, b);
                if (Math.Abs(b.X + 0.5 - h.X) < 0.58 && Math.Abs(b.Y + 0.5 - h.Y) < 0.58 && h.Z < bz + 0.55 && h.Z + Hero.Tall > bz) { Die(w, "A SPIKED BALL"); return; }
            }
        if (w.Probe) return;
        if (r.Charm is { } charm && Present(w, r))
        {
            double top = Math.Max(0, r.Top(r.CharmAt.X, r.CharmAt.Y));
            if (Math.Abs(r.CharmAt.X + 0.5 - h.X) < 0.55 && Math.Abs(r.CharmAt.Y + 0.5 - h.Y) < 0.55 && Math.Abs(h.Z - top) < 0.6)
            {
                w.Carried.Add(charm);
                w.Heard.Add(Sfx.Pickup);
            }
        }
        if (r.Cauldron)
        {
            // The wolf in his room: a moment to get out, then the wizard strikes.
            if (w.Night && w.Changing <= 0)
            {
                w.Wolf += Dt;
                if (w.Wolf > WizardGrace) { Die(w, "THE WIZARD'S CURSE"); return; }
            }
            else w.Wolf = 0;
            if (w.WantedNow is { } want && w.Carried.Contains(want) && Math.Abs(h.X - (Field.Mid + 0.5)) < 1.6 && Math.Abs(h.Y - (Field.Mid + 0.5)) < 1.6 && h.Z < 0.5)
            {
                w.Carried.Remove(want);
                w.Delivered++;
                w.Heard.Add(w.Cured ? Sfx.Cured : Sfx.Cauldron);
            }
        }
    }

    /// <summary>Whether the room's charm is still lying in it.</summary>
    public static bool Present(World w, RoomDef r) =>
        r.Charm is { } c && !w.Carried.Contains(c) && !w.Castle.Wanted.Take(w.Delivered).Contains(c);

    private static void Doors(World w, Hero h)
    {
        Side? through = h.Y < -0.3 ? Side.North : h.X > Field.N + 0.3 ? Side.East : h.Y > Field.N + 0.3 ? Side.South : h.X < -0.3 ? Side.West : null;
        if (through is not { } s) return;
        if (w.Probe) { h.Dead = true; w.Cause = "OUT"; return; }
        int next = w.Castle.Through(w.Room, s);
        if (next < 0) return;
        Enter(w, next, RoomDef.Opposite(s));
        w.Heard.Add(Sfx.Door);
    }

    public static void Die(World w, string cause)
    {
        var h = w.Hero;
        if (h.Dead) return;
        h.Dead = true;
        w.Cause = cause;
        if (!w.Probe) w.Heard.Add(Sfx.Die);
    }
}
