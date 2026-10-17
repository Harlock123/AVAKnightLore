using AVAKnightLore.Engine;

namespace AVAKnightLore.Tests;

/// <summary>How the computer Sabreman gets on in each castle (set KNIGHT_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_Sabreman_does()
    {
        if (Environment.GetEnvironmentVariable("KNIGHT_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < CastleBook.Count; i++)
        {
            var b = System.Diagnostics.Stopwatch.StartNew();
            var castle = CastleBook.Build(i);
            output.WriteLine($"castle {i} built in {b.ElapsedMilliseconds} ms");
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "knight-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(i);
            g.SetLives(40);
            var deaths = new Dictionary<string, int>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int t = 0;
            for (; t < 60 * 1500 && g.State == GameState.Playing && g.Cleared == 0 && g.Phase != Phase.TooLate; t++)
            {
                var c = mode == "lazy" ? new Controls(1, 0, t % 50 == 0) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[g.World.Cause] = deaths.GetValueOrDefault(g.World.Cause) + 1;
                last = g.Phase;
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
            }
            var w = g.World;
            output.WriteLine($"{i + 1} {castle.Name}: {(g.Cleared > 0 ? "CURED" : g.Phase.ToString())} after {t / 60}s (day {w.Day}), {deaths.Values.Sum()} deaths ({string.Join(", ", deaths.Select(d => $"{d.Key} {d.Value}"))}), delivered {w.Delivered}/{castle.Wanted.Count}, room {w.Room}; {string.Join(", ", doing.OrderByDescending(d => d.Value).Take(5).Select(d => $"{d.Key} {d.Value / 60}s"))}");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
