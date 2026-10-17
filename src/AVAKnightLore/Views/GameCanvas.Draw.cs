using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAKnightLore.Engine;

namespace AVAKnightLore.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAKnightLore/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double TW = 38, TH = 19, BH = 38, OX = 512, OY = 120, ViewH = 560;

    private static readonly (uint Top, uint Left, uint Right, uint Floor1, uint Floor2, uint Wall)[] RoomStyles =
    [
        (0xFFB8B0A0, 0xFF8A8478, 0xFF6A6458, 0xFF5A5448, 0xFF4E4840, 0xFF3A4A6A),
        (0xFFA0B8B0, 0xFF6E8A82, 0xFF52685F, 0xFF48564F, 0xFF3E4A44, 0xFF5A3A4A),
        (0xFFC0A888, 0xFF967E60, 0xFF705C44, 0xFF5E4C3A, 0xFF524232, 0xFF2E4A3A),
        (0xFFA8A8C8, 0xFF7C7C9C, 0xFF5C5C78, 0xFF4C4C64, 0xFF424258, 0xFF5A4A2A),
    ];

    private static Point Iso(double x, double y, double z) => new(OX + (x - y) * TW, OY + (x + y) * TH - z * BH);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawRoom(dc, g.World);
                DrawHud(dc, g.World);
                if (g.Message is { } msg)
                {
                    double wdt = Measure(msg, 40) + 40;
                    dc.FillRectangle(Brush(0xC0000000), new Rect(512 - wdt / 2, 30, wdt, 56), 6);
                    Text(dc, msg, new Point(512, 58), 40, g.Phase == Phase.Dying || g.Phase == Phase.TooLate ? Red : Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, ViewH - 14), 22, Yellow, center: true);
    }

    private static double Measure(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(White)).Width;

    // ───────────────────────────── the room ─────────────────────────────

    private void DrawRoom(DrawingContext dc, World w)
    {
        var r = w.Here;
        var st = RoomStyles[r.Style % RoomStyles.Length];
        const int n = Field.N;
        // The two back walls, with arches where the doors are.
        Poly(dc, st.Wall, null, 0, Iso(0, 0, 0), Iso(n, 0, 0), Iso(n, 0, 3.2), Iso(0, 0, 3.2));
        Poly(dc, Shade(st.Wall, 0.8), null, 0, Iso(0, 0, 0), Iso(0, n, 0), Iso(0, n, 3.2), Iso(0, 0, 3.2));
        for (int i = 0; i < n; i++)
        {
            // Stone courses.
            dc.DrawLine(Pen(Shade(st.Wall, 0.7), 1), Iso(i, 0, 0), Iso(i, 0, 3.2));
            dc.DrawLine(Pen(Shade(st.Wall, 0.6), 1), Iso(0, i, 0), Iso(0, i, 3.2));
        }
        for (double z = 0.8; z < 3.2; z += 0.8)
        {
            dc.DrawLine(Pen(Shade(st.Wall, 0.7), 1), Iso(0, 0, z), Iso(n, 0, z));
            dc.DrawLine(Pen(Shade(st.Wall, 0.6), 1), Iso(0, 0, z), Iso(0, n, z));
        }
        if (r.Doors[(int)Side.North]) Arch(dc, Iso(Field.Mid, 0, 0), Iso(Field.Mid + 1, 0, 0), st);
        if (r.Doors[(int)Side.West]) Arch(dc, Iso(0, Field.Mid, 0), Iso(0, Field.Mid + 1, 0), st);
        // The floor.
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
        {
            var f = r.Floor[x, y];
            if (f == Floor.Pit && r.Block[x, y] == 0) continue;
            Poly(dc, (x + y) % 2 == 0 ? st.Floor1 : st.Floor2, Shade(st.Floor2, 0.8), 1, Iso(x, y, 0), Iso(x + 1, y, 0), Iso(x + 1, y + 1, 0), Iso(x, y + 1, 0));
            if (f == Floor.Spikes && r.Block[x, y] == 0)
                for (int k = 0; k < 4; k++)
                {
                    double sx = x + 0.25 + (k % 2) * 0.5, sy = y + 0.25 + (k / 2) * 0.5;
                    Poly(dc, 0xFFD0D0D8, 0xFF707078, 1, Iso(sx - 0.12, sy, 0), Iso(sx + 0.12, sy, 0), Iso(sx, sy, 0.5));
                }
        }
        // Doorways through the near walls: just the stones of the frame.
        foreach (var s in new[] { Side.East, Side.South })
            if (r.Doors[(int)s])
            {
                var (dx, dy) = RoomDef.DoorCell(s);
                var (ox, oy) = RoomDef.Out(s);
                double ex = s == Side.East ? n : dx, ey = s == Side.South ? n : dy;
                var a = s == Side.East ? Iso(ex, ey, 0) : Iso(ex, ey, 0);
                var b = s == Side.East ? Iso(ex, ey + 1, 0) : Iso(ex + 1, ey, 0);
                Poly(dc, 0x60FFE080, null, 0, a, b, new Point(b.X, b.Y + 4), new Point(a.X, a.Y + 4));
            }
        // Everything standing in the room, back to front.
        var things = new List<(double Depth, double Z, Action Draw)>();
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
            for (int k = 0; k < r.Block[x, y]; k++)
            {
                int bx = x, by = y, bz = k;
                things.Add((x + y + 1, k, () => Cube(dc, bx, by, bz, st, r.Cauldron && bx == Field.Mid && by == Field.Mid)));
            }
        if (r.Cauldron) things.Add((Field.Mid * 2 + 1.05, 1, () => Cauldron(dc, w)));
        if (r.Charm is { } charm && Sim.Present(w, r))
        {
            double top = Math.Max(0, r.Top(r.CharmAt.X, r.CharmAt.Y));
            things.Add((r.CharmAt.X + r.CharmAt.Y + 1.02, top, () => CharmOnFloor(dc, charm, r.CharmAt.X + 0.5, r.CharmAt.Y + 0.5, top)));
        }
        foreach (var b in r.Balls)
        {
            var bb = b;
            things.Add((b.X + b.Y + 1.01, Sim.BallZ(w, b), () => Ball(dc, bb.X + 0.5, bb.Y + 0.5, Sim.BallZ(w, bb))));
        }
        foreach (var gd in w.Guards)
        {
            var gg = gd;
            things.Add((gd.X + gd.Y, 0, () => GuardFigure(dc, gg)));
        }
        var h = w.Hero;
        if (!h.Dead || (int)(Engine.PhaseTime * 10) % 2 == 0)
            things.Add((h.X + h.Y, h.Z, () => HeroFigure(dc, h, w.Night, w.Changing)));
        foreach (var t in things.OrderBy(t => t.Depth).ThenBy(t => t.Z)) t.Draw();
        // Moonlight.
        if (w.Night) dc.FillRectangle(Brush(0x48000830), new Rect(0, 0, Field.Width, ViewH));
    }

    private static uint Shade(uint c, double k)
    {
        uint r = (uint)Math.Min(255, ((c >> 16) & 0xFF) * k), gg = (uint)Math.Min(255, ((c >> 8) & 0xFF) * k), b = (uint)Math.Min(255, (c & 0xFF) * k);
        return 0xFF000000 | r << 16 | gg << 8 | b;
    }

    private static void Arch(DrawingContext dc, Point a, Point b, (uint Top, uint Left, uint Right, uint Floor1, uint Floor2, uint Wall) st)
    {
        double h = BH * 1.9;
        Poly(dc, 0xFF0A0A10, null, 0, a, b, new Point(b.X, b.Y - h), new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2 - h - 14), new Point(a.X, a.Y - h));
        dc.DrawLine(Pen(st.Top, 3), a, new Point(a.X, a.Y - h));
        dc.DrawLine(Pen(st.Top, 3), b, new Point(b.X, b.Y - h));
    }

    private static void Cube(DrawingContext dc, int x, int y, int z, (uint Top, uint Left, uint Right, uint Floor1, uint Floor2, uint Wall) st, bool plinth)
    {
        uint top = plinth ? 0xFF8A6A40 : st.Top, left = plinth ? 0xFF6A4A28 : st.Left, right = plinth ? 0xFF54381C : st.Right;
        Poly(dc, left, 0xFF202020, 1, Iso(x, y + 1, z), Iso(x + 1, y + 1, z), Iso(x + 1, y + 1, z + 1), Iso(x, y + 1, z + 1));
        Poly(dc, right, 0xFF202020, 1, Iso(x + 1, y, z), Iso(x + 1, y + 1, z), Iso(x + 1, y + 1, z + 1), Iso(x + 1, y, z + 1));
        Poly(dc, top, 0xFF202020, 1, Iso(x, y, z + 1), Iso(x + 1, y, z + 1), Iso(x + 1, y + 1, z + 1), Iso(x, y + 1, z + 1));
        // A carved line round each face.
        var c = Iso(x + 0.5, y + 0.5, z + 1);
        dc.DrawEllipse(null, Pen(Shade(top, 0.85), 1), c, TW * 0.45, TH * 0.45);
    }

    private void Cauldron(DrawingContext dc, World w)
    {
        var c = Iso(Field.Mid + 0.5, Field.Mid + 0.5, 1);
        dc.DrawEllipse(Brush(0xFF202020), Pen(0xFF404040, 2), new Point(c.X, c.Y - 18), 34, 26);
        dc.DrawEllipse(Brush(w.Night ? 0xFFC03030 : 0xFF30C060), null, new Point(c.X, c.Y - 36), 28, 9);
        for (int i = 0; i < 4; i++)
        {
            double t = (_clock * 0.8 + i * 0.25) % 1;
            dc.FillEllipse(c.X - 14 + i * 9 + Math.Sin(_clock * 3 + i) * 4, c.Y - 44 - t * 50, 5 + t * 6, (uint)((1 - t) * 160) << 24 | 0xE0F0E0);
        }
        // Melkhior, behind it.
        var m = Iso(Field.Mid + 0.5, Field.Mid - 0.6, 0);
        Poly(dc, 0xFF4A2A8A, 0xFF2A1A5A, 2, new Point(m.X - 22, m.Y), new Point(m.X + 22, m.Y), new Point(m.X + 10, m.Y - 70), new Point(m.X - 10, m.Y - 70));
        dc.FillEllipse(m.X, m.Y - 78, 11, 0xFFE8C8A0);
        Poly(dc, 0xFFF0F0F0, null, 0, new Point(m.X - 9, m.Y - 74), new Point(m.X + 9, m.Y - 74), new Point(m.X, m.Y - 44));
        Poly(dc, 0xFF4A2A8A, 0xFF2A1A5A, 2, new Point(m.X - 16, m.Y - 84), new Point(m.X + 16, m.Y - 84), new Point(m.X + 2, m.Y - 122));
        Star(dc, m.X + 3, m.Y - 104, 4, Yellow);
        // What he wants, in a bubble.
        if (w.WantedNow is { } want)
        {
            var bp = new Point(m.X + 70, m.Y - 120);
            dc.DrawEllipse(Brush(0xE0FFFFF0), Pen(0xFF404040, 2), bp, 30, 24);
            Poly(dc, 0xE0FFFFF0, null, 0, new Point(bp.X - 22, bp.Y + 14), new Point(bp.X - 10, bp.Y + 20), new Point(m.X + 18, m.Y - 96));
            CharmIcon(dc, want, bp.X, bp.Y, 0.8);
        }
        if (w.Night && (int)(_clock * 8) % 3 == 0)
        {
            dc.DrawLine(Pen(0xFFFFFF80, 3), new Point(m.X + 10, m.Y - 100), new Point(m.X + 60, m.Y - 60));
            dc.DrawLine(Pen(0xFFFFFF80, 3), new Point(m.X + 60, m.Y - 60), new Point(m.X + 40, m.Y - 30));
        }
    }

    private void CharmOnFloor(DrawingContext dc, Charm c, double x, double y, double z)
    {
        var p = Iso(x, y, z);
        dc.DrawEllipse(Brush(0x50000000), null, p, 14, 7);
        double bob = Math.Sin(_clock * 3 + x) * 4;
        dc.FillEllipse(p.X, p.Y - 22 + bob, 16, 0x40FFFF80);
        CharmIcon(dc, c, p.X, p.Y - 22 + bob, 0.9);
    }

    public static void CharmIcon(DrawingContext dc, Charm c, double x, double y, double k)
    {
        switch (c)
        {
            case Charm.Gem:
                Poly(dc, 0xFF40E0A0, 0xFF107050, 1.5, new Point(x - 10 * k, y - 4 * k), new Point(x - 5 * k, y - 10 * k), new Point(x + 5 * k, y - 10 * k), new Point(x + 10 * k, y - 4 * k), new Point(x, y + 11 * k));
                break;
            case Charm.Chalice:
                Poly(dc, 0xFFE0B030, 0xFF805010, 1.5, new Point(x - 10 * k, y - 10 * k), new Point(x + 10 * k, y - 10 * k), new Point(x + 3 * k, y), new Point(x + 3 * k, y + 6 * k), new Point(x + 8 * k, y + 11 * k), new Point(x - 8 * k, y + 11 * k), new Point(x - 3 * k, y + 6 * k), new Point(x - 3 * k, y));
                break;
            case Charm.Bottle:
                dc.DrawEllipse(Brush(0xFF60A0FF), Pen(0xFF204080, 1.5), new Point(x, y + 4 * k), 9 * k, 9 * k);
                dc.FillRectangle(Brush(0xFF60A0FF), new Rect(x - 3 * k, y - 12 * k, 6 * k, 10 * k));
                dc.FillRectangle(Brush(0xFF8A5A2A), new Rect(x - 3.5 * k, y - 14 * k, 7 * k, 4 * k));
                break;
            case Charm.Boot:
                Poly(dc, 0xFF9A5A2A, 0xFF4A2A0A, 1.5, new Point(x - 6 * k, y - 12 * k), new Point(x + 3 * k, y - 12 * k), new Point(x + 3 * k, y + 2 * k), new Point(x + 12 * k, y + 5 * k), new Point(x + 12 * k, y + 11 * k), new Point(x - 6 * k, y + 11 * k));
                break;
            case Charm.Diamond:
                Poly(dc, 0xFFE8F8FF, 0xFF6090B0, 1.5, new Point(x, y - 12 * k), new Point(x + 10 * k, y), new Point(x, y + 12 * k), new Point(x - 10 * k, y));
                break;
            case Charm.Poison:
                Poly(dc, 0xFF60D040, 0xFF205010, 1.5, new Point(x - 8 * k, y - 4 * k), new Point(x + 8 * k, y - 4 * k), new Point(x + 10 * k, y + 11 * k), new Point(x - 10 * k, y + 11 * k));
                dc.FillRectangle(Brush(0xFF60D040), new Rect(x - 3 * k, y - 12 * k, 6 * k, 8 * k));
                dc.FillEllipse(x, y + 4 * k, 3 * k, 0xFFFFFFFF);
                break;
            case Charm.Bell:
                Poly(dc, 0xFFD0A040, 0xFF705010, 1.5, new Point(x - 4 * k, y - 10 * k), new Point(x + 4 * k, y - 10 * k), new Point(x + 11 * k, y + 8 * k), new Point(x - 11 * k, y + 8 * k));
                dc.FillEllipse(x, y + 10 * k, 3 * k, 0xFF705010);
                break;
            default:
                Poly(dc, 0xFFF0C030, 0xFF806010, 1.5, new Point(x - 11 * k, y + 8 * k), new Point(x - 11 * k, y - 6 * k), new Point(x - 5 * k, y), new Point(x, y - 10 * k), new Point(x + 5 * k, y), new Point(x + 11 * k, y - 6 * k), new Point(x + 11 * k, y + 8 * k));
                break;
        }
    }

    private void Ball(DrawingContext dc, double x, double y, double z)
    {
        var f = Iso(x, y, 0);
        dc.DrawEllipse(Brush(0x60000000), null, f, 16 - z * 4, 8 - z * 2);
        var p = Iso(x, y, z + 0.4);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4 + _clock;
            dc.DrawLine(Pen(0xFFD0D0D8, 2), p, new Point(p.X + Math.Cos(a) * 20, p.Y + Math.Sin(a) * 20));
        }
        dc.FillEllipse(p.X, p.Y, 13, 0xFF404048);
        dc.FillEllipse(p.X - 4, p.Y - 4, 4, 0xFF8A8A98);
    }

    private void GuardFigure(DrawingContext dc, Guard g)
    {
        var f = Iso(g.X, g.Y, 0);
        dc.DrawEllipse(Brush(0x50000000), null, f, 15, 7);
        double step = Math.Sin(_clock * 8 + g.Pos) * 4;
        uint metal = g.Def.Look switch { 0 => 0xFFA0A8B8u, 1 => 0xFFB89848u, _ => 0xFF8A6AA8u };
        dc.FillRectangle(Brush(Shade(metal, 0.6)), new Rect(f.X - 8 + step, f.Y - 18, 6, 18));
        dc.FillRectangle(Brush(Shade(metal, 0.6)), new Rect(f.X + 2 - step, f.Y - 18, 6, 18));
        dc.FillRectangle(Brush(metal), new Rect(f.X - 11, f.Y - 42, 22, 26), 4);
        dc.FillEllipse(f.X, f.Y - 50, 10, metal);
        dc.FillRectangle(Brush(0xFF101010), new Rect(f.X - 7, f.Y - 52, 14, 3));
        Poly(dc, 0xFFD03030, null, 0, new Point(f.X - 3, f.Y - 60), new Point(f.X + 3, f.Y - 60), new Point(f.X, f.Y - 72));
        // The spear.
        dc.DrawLine(Pen(0xFF6A4A2A, 3), new Point(f.X + 16, f.Y - 4), new Point(f.X + 16, f.Y - 66));
        Poly(dc, 0xFFD0D0D8, null, 0, new Point(f.X + 12, f.Y - 66), new Point(f.X + 20, f.Y - 66), new Point(f.X + 16, f.Y - 80));
    }

    /// <summary>Sabreman in his pith helmet, or (by night) the wolf; mid-change, flickering between the two.</summary>
    private void HeroFigure(DrawingContext dc, Hero h, bool night, double changing)
    {
        var f = Iso(h.X, h.Y, h.Z);
        var shadow = Iso(h.X, h.Y, Math.Max(0, Math.Floor(h.Z + 0.01)));
        dc.DrawEllipse(Brush(0x50000000), null, shadow, 14, 7);
        bool wolf = night;
        if (changing > 0) wolf = (int)(changing * 12) % 2 == 0 ? night : !night;
        double step = h.Airborne ? 0 : Math.Sin(h.Walk * 12) * 4;
        // Which way he faces on the screen.
        double sx = (h.FaceX - h.FaceY) * 0.5;
        if (!wolf)
        {
            dc.FillRectangle(Brush(0xFF5A3A1A), new Rect(f.X - 8 + step, f.Y - 16, 6, 16));
            dc.FillRectangle(Brush(0xFF5A3A1A), new Rect(f.X + 2 - step, f.Y - 16, 6, 16));
            dc.FillRectangle(Brush(0xFFC8B070), new Rect(f.X - 11, f.Y - 40, 22, 26), 4);
            dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(f.X - 11, f.Y - 22, 22, 4));
            dc.FillEllipse(f.X + sx * 3, f.Y - 48, 10, 0xFFE8C090);
            dc.FillEllipse(f.X + sx * 7, f.Y - 48, 2, 0xFF202020);
            // The pith helmet.
            dc.DrawEllipse(Brush(0xFFF0E8D0), Pen(0xFF8A7A50, 1.5), new Point(f.X, f.Y - 55), 15, 5);
            dc.DrawEllipse(Brush(0xFFF0E8D0), Pen(0xFF8A7A50, 1.5), new Point(f.X, f.Y - 60), 10, 8);
        }
        else
        {
            dc.FillRectangle(Brush(0xFF4A3A2A), new Rect(f.X - 9 + step, f.Y - 18, 7, 18));
            dc.FillRectangle(Brush(0xFF4A3A2A), new Rect(f.X + 2 - step, f.Y - 18, 7, 18));
            dc.DrawEllipse(Brush(0xFF6A5038), Pen(0xFF3A2818, 1.5), new Point(f.X, f.Y - 30), 13, 16);
            dc.DrawEllipse(Brush(0xFF6A5038), Pen(0xFF3A2818, 1.5), new Point(f.X + sx * 4, f.Y - 52), 11, 10);
            Poly(dc, 0xFF6A5038, 0xFF3A2818, 1, new Point(f.X + sx * 6, f.Y - 54), new Point(f.X + sx * 22, f.Y - 48), new Point(f.X + sx * 6, f.Y - 44));
            Poly(dc, 0xFF6A5038, null, 0, new Point(f.X - 9, f.Y - 58), new Point(f.X - 3, f.Y - 60), new Point(f.X - 8, f.Y - 72));
            Poly(dc, 0xFF6A5038, null, 0, new Point(f.X + 9, f.Y - 58), new Point(f.X + 3, f.Y - 60), new Point(f.X + 8, f.Y - 72));
            dc.FillEllipse(f.X + sx * 8 - 3, f.Y - 55, 2.5, 0xFFFFE040);
            dc.FillEllipse(f.X + sx * 8 + 3, f.Y - 55, 2.5, 0xFFFFE040);
        }
    }

    private static void Star(DrawingContext dc, double x, double y, double r, uint c)
    {
        var pts = new Point[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? r : r * 0.45;
            pts[i] = new Point(x + Math.Cos(a) * rr, y + Math.Sin(a) * rr);
        }
        Poly(dc, c, null, 0, pts);
    }

    // ───────────────────────────── the scroll below ─────────────────────────────

    private void DrawHud(DrawingContext dc, World w)
    {
        var g = Engine;
        double y0 = ViewH;
        dc.FillRectangle(Brush(0xFF1A1410), new Rect(0, y0, Field.Width, Field.Height - y0));
        dc.FillRectangle(Brush(0xFF8A6A3A), new Rect(0, y0, Field.Width, 4));
        // The window: the sun or the moon crossing it.
        double wx = 512, wy = y0 + 100;
        dc.DrawEllipse(Brush(w.Night ? 0xFF101830 : 0xFF6AA8E0), Pen(0xFF8A6A3A, 6), new Point(wx, wy), 70, 70);
        double t = w.Night ? (w.DayTime - Sim.DayLength) / Sim.NightLength : w.DayTime / Sim.DayLength;
        double a = Math.PI * (1 - t);
        var body = new Point(wx + Math.Cos(a) * 50, wy + 30 - Math.Sin(a) * 60);
        if (w.Night)
        {
            dc.FillEllipse(body.X, body.Y, 14, 0xFFF0F0D8);
            dc.FillEllipse(body.X + 6, body.Y - 3, 12, 0xFF101830);
        }
        else
        {
            dc.FillEllipse(body.X, body.Y, 15, 0xFFFFE040);
            for (int i = 0; i < 8; i++)
            {
                double ra = i * Math.PI / 4 + _clock;
                dc.DrawLine(Pen(0xFFFFE040, 2), new Point(body.X + Math.Cos(ra) * 19, body.Y + Math.Sin(ra) * 19), new Point(body.X + Math.Cos(ra) * 25, body.Y + Math.Sin(ra) * 25));
            }
        }
        dc.FillRectangle(Brush(0xFF1A1410), new Rect(wx - 76, wy + 30, 152, 50));
        dc.DrawLine(Pen(0xFF8A6A3A, 6), new Point(wx - 70, wy + 30), new Point(wx + 70, wy + 30));
        Text(dc, $"DAY {w.Day + 1} OF {Sim.Days}", new Point(wx, wy + 52), 26, w.Day >= Sim.Days - 5 ? Red : 0xFFE8D8B0, center: true);
        // The wizard's wish, and what's in the pack.
        Text(dc, "THE WIZARD WANTS", new Point(40, y0 + 30), 22, 0xFFB09870);
        dc.FillRectangle(Brush(0xFFE8D8B0), new Rect(40, y0 + 48, 70, 70), 6);
        if (w.WantedNow is { } want) CharmIcon(dc, want, 75, y0 + 83, 1.6);
        Text(dc, $"{w.Delivered} OF {w.Castle.Wanted.Count} IN THE CAULDRON", new Point(130, y0 + 70), 22, 0xFFE8D8B0);
        Text(dc, "CARRYING", new Point(130, y0 + 104), 20, 0xFFB09870);
        int i2 = 0;
        foreach (var c in w.Carried)
        {
            dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(230 + i2 * 40, y0 + 88, 34, 34), 4);
            CharmIcon(dc, c, 247 + i2 * 40, y0 + 105, 1.0);
            i2++;
        }
        // Score and lives.
        Text(dc, $"SCORE {g.Score:000000}", new Point(640, y0 + 34), 28, Yellow);
        Text(dc, $"HIGH  {Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(640, y0 + 66), 22, 0xFFB09870);
        Text(dc, w.Castle.Name, new Point(640, y0 + 98), 20, 0xFFB09870);
        for (int i = 0; i < Math.Min(8, g.Lives - 1); i++)
        {
            double lx = 650 + i * 34, ly = y0 + 160;
            dc.DrawEllipse(Brush(0xFFF0E8D0), Pen(0xFF8A7A50, 1.5), new Point(lx, ly - 10), 13, 4);
            dc.DrawEllipse(Brush(0xFFF0E8D0), Pen(0xFF8A7A50, 1.5), new Point(lx, ly - 14), 8, 6);
        }
    }

    // ───────────────────────────── the title and the end ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF100C08), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "AVA KNIGHT LORE", new Point(cx, 80), 96, 0xFFE8C860, center: true);
        Text(dc, "AFTER ULTIMATE PLAY THE GAME'S 1984 ZX SPECTRUM GAME", new Point(cx, 140), 22, 0xFFB09870, center: true);
        // Sabreman, and the wolf he becomes.
        var day = new Hero { X = 0, Y = 0, FaceX = 1, FaceY = 0, Walk = _clock };
        using (dc.PushTransform(Matrix.CreateScale(2.4, 2.4) * Matrix.CreateTranslation(cx - 160 - 2.4 * OX, 400 - 2.4 * OY)))
            HeroFigure(dc, day, false, 0);
        using (dc.PushTransform(Matrix.CreateScale(2.4, 2.4) * Matrix.CreateTranslation(cx + 160 - 2.4 * OX, 400 - 2.4 * OY)))
            HeroFigure(dc, new Hero { FaceX = 0, FaceY = 1, Walk = _clock }, true, 0);
        Text(dc, "→", new Point(cx, 330), 60, 0xFF8A6A3A, center: true);
        Text(dc, "BY DAY", new Point(cx - 160, 430), 24, 0xFFE8D8B0, center: true);
        Text(dc, "BY NIGHT", new Point(cx + 160, 430), 24, 0xFFE8D8B0, center: true);
        Text(dc, "SABREMAN IS CURSED: EACH NIGHT HE TURNS INTO A WOLF. THE WIZARD MELKHIOR CAN CURE HIM,", new Point(cx, 492), 21, White, center: true);
        Text(dc, "IF HE BRINGS THE CHARMS THE CAULDRON ASKS FOR - ONE AT A TIME, IN DAYLIGHT - WITHIN FORTY DAYS.", new Point(cx, 518), 21, White, center: true);
        Text(dc, "THE WIZARD WILL NOT ABIDE THE WOLF.", new Point(cx, 544), 21, White, center: true);
        Text(dc, "PRESS ENTER TO START", new Point(cx, 610), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "ARROWS: WALK (UP IS AWAY)   SPACE: JUMP   P: PAUSE   M: SOUND", new Point(cx, 670), 20, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 716), 24, Cyan, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 220), 110, Red, center: true);
        Text(dc, Engine.World.Day >= Sim.Days ? "THE CURSE IS FOR EVER" : "SABREMAN IS NO MORE", new Point(cx, 310), 34, White, center: true);
        using (dc.PushTransform(Matrix.CreateScale(2.2, 2.2) * Matrix.CreateTranslation(cx - 2.2 * OX, 560 - 2.2 * OY)))
            HeroFigure(dc, new Hero { FaceY = 1, Walk = _clock }, true, 0);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 640), 44, Yellow, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE BOLDEST ADVENTURERS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE  CASTLES", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,5}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
