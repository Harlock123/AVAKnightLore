using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAKnightLore.Engine;
using AVAKnightLore.Views;
using Controls = AVAKnightLore.Engine.Controls;

namespace AVAKnightLore.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set KNIGHT_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaknightlore-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("KNIGHT_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAKnightLore-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("SAB", 48210, 2);
        g.HighScores.Insert("AVA", 21500, 1);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A castle played by the computer Sabreman until something worth showing.</summary>
    private static void Play(int level, string name, Func<GameEngine, bool> moment, int seconds = 400)
    {
        var (w, c, g) = Open();
        g.Go(level);
        g.SetLives(20);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static bool In(GameEngine g, Func<RoomDef, bool> room) => room(g.World.Here) && g.World.Hero.X > 2 && g.World.Hero.X < 7 && g.World.Hero.Y > 2 && g.World.Hero.Y < 7;

    [AvaloniaFact]
    public void The_first_room() => Play(0, "start", g => g.World.Clock > 1.2);

    [AvaloniaFact]
    public void Spikes() => Play(1, "spikes", g => In(g, r => Enumerable.Range(0, 81).Count(i => r.Floor[i % 9, i / 9] == Floor.Spikes) > 12) && g.World.Hero.Airborne);

    [AvaloniaFact]
    public void A_guard() => Play(1, "guards", g => In(g, r => r.Guards.Count > 0) && !g.World.Night);

    [AvaloniaFact]
    public void Spiked_balls() => Play(2, "balls", g => In(g, r => r.Balls.Count >= 2) && !g.World.Night);

    [AvaloniaFact]
    public void A_charm() => Play(0, "charm", g => g.World.Here.Charm != null && Sim.Present(g.World, g.World.Here) && In(g, _ => true) && !g.World.Night);

    [AvaloniaFact]
    public void The_cauldron() => Play(0, "cauldron", g => g.World.Here.Cauldron && !g.World.Night && g.World.Clock > 3 && g.World.Delivered >= 1);

    [AvaloniaFact]
    public void The_wolf() => Play(1, "wolf", g => g.World.Night && g.World.Changing <= 0 && In(g, r => !r.Cauldron));

    [AvaloniaFact]
    public void Cured() => Play(0, "cured", g => g.Phase == Phase.Cured && g.PhaseTime > 0.5);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(0);
        g.SetLives(1);
        Sim.Die(g.World, "TEST");
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
