using Avalonia.Input;
using AVAKnightLore.Engine;

namespace AVAKnightLore.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaknightlore-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A bare room with doors all round, in a castle of three rooms in a row (the cauldron's in the middle one).</summary>
    private static World Bare(Action<RoomDef>? build = null)
    {
        var c = new CastleDef { Name = "TEST", Cols = 3, Rows = 1, Start = 0, CauldronRoom = 1, Wanted = [Charm.Gem, Charm.Bell] };
        for (int i = 0; i < 3; i++) c.Rooms.Add(new RoomDef { Index = i, Col = i, Row = 0 });
        c.Rooms[0].Doors[(int)Side.East] = c.Rooms[1].Doors[(int)Side.West] = true;
        c.Rooms[1].Doors[(int)Side.East] = c.Rooms[2].Doors[(int)Side.West] = true;
        c.Rooms[1].Cauldron = true;
        c.Rooms[1].Block[Field.Mid, Field.Mid] = 1;
        build?.Invoke(c.Rooms[0]);
        var w = new World();
        Sim.Start(w, c);
        return w;
    }

    private static void Put(World w, double x, double y, double z = 0)
    {
        w.Hero.X = x;
        w.Hero.Y = y;
        w.Hero.Z = z;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    // ───────────────────────────── walking and jumping ─────────────────────────────

    [Fact]
    public void He_walks_along_the_room_and_a_wall_stops_him()
    {
        var w = Bare();
        Put(w, 2.5, 2.5);
        Run(w, 30, new Controls(1, 0));
        Assert.Equal(2.5 + Hero.Speed / 2, w.Hero.X, 3);
        Run(w, 120, new Controls(0, -1));
        Assert.InRange(w.Hero.Y, Hero.Half, Hero.Half + 0.06);
        Assert.Equal(0, w.Room);
    }

    [Fact]
    public void A_jump_clears_a_block_and_lands_on_one_but_not_on_two()
    {
        var w = Bare(r => { r.Block[4, 2] = 1; r.Block[4, 6] = 2; });
        Put(w, 3.5, 2.5);
        Sim.Step(w, new Controls(1, 0, true));
        Run(w, 50);
        Assert.Equal(1, w.Hero.Z, 3);
        Put(w, 3.5, 6.5, 0);
        Run(w, 5);
        Sim.Step(w, new Controls(1, 0, true));
        Run(w, 50);
        Assert.Equal(0, w.Hero.Z, 3);
        Assert.True(w.Hero.X < 4, "two blocks high is a wall");
    }

    [Fact]
    public void Walking_off_a_block_he_drops_down()
    {
        var w = Bare(r => r.Block[3, 3] = 1);
        Put(w, 3.5, 3.5, 1);
        Run(w, 40, new Controls(1, 0));
        Assert.Equal(0, w.Hero.Z, 3);
        Assert.False(w.Hero.Dead);
    }

    [Fact]
    public void A_jump_carries_over_a_pit_and_walking_in_is_death()
    {
        var w = Bare(r => r.Floor[4, 3] = Floor.Pit);
        Put(w, 3.5, 3.5);
        Sim.Step(w, new Controls(1, 0, true));
        Run(w, 50);
        Assert.False(w.Hero.Dead);
        Assert.InRange(w.Hero.X, 5, 6);
        Put(w, 3.5, 3.5);
        Run(w, 90, new Controls(1, 0));
        Assert.True(w.Hero.Dead);
        Assert.Equal("FELL INTO THE DARK", w.Cause);
    }

    [Fact]
    public void Spikes_are_death_underfoot()
    {
        var w = Bare(r => r.Floor[4, 3] = Floor.Spikes);
        Put(w, 3.5, 3.5);
        Run(w, 40, new Controls(1, 0));
        Assert.True(w.Hero.Dead);
        Assert.Equal("SPIKED", w.Cause);
    }

    [Fact]
    public void Holding_jump_jumps_once()
    {
        var w = Bare();
        Put(w, 3.5, 3.5);
        int jumps = 0;
        bool was = false;
        for (int i = 0; i < 180; i++)
        {
            Sim.Step(w, new Controls(0, 0, true));
            if (w.Hero.Airborne && !was) jumps++;
            was = w.Hero.Airborne;
        }
        Assert.Equal(1, jumps);
    }

    // ───────────────────────────── doors, guards, balls ─────────────────────────────

    [Fact]
    public void Through_a_door_into_the_next_room_and_lined_up_with_it_on_the_way()
    {
        var w = Bare();
        Put(w, 7.5, 4.2);
        for (int i = 0; i < 120 && w.Room == 0; i++) Sim.Step(w, new Controls(1, 0));
        Assert.Equal(1, w.Room);
        Assert.InRange(w.Hero.X, 0, 1.5);
        Assert.Equal((w.Hero.X, w.Hero.Y, w.Hero.Z), w.Entry);
    }

    [Fact]
    public void A_guard_paces_its_line_and_catches_him_but_not_up_on_a_block()
    {
        var w = Bare(r => { r.Guards.Add(new GuardDef(true, 2.5, 1, 8, 2, 0)); r.Block[5, 2] = 1; });
        Sim.Enter(w, 0, null);
        Put(w, 5.5, 2.5, 1);
        Run(w, 60 * 6);
        Assert.False(w.Hero.Dead);
        Put(w, 3.5, 2.5);
        Run(w, 60 * 6);
        Assert.True(w.Hero.Dead);
        Assert.Equal("CAUGHT BY A GUARD", w.Cause);
    }

    [Fact]
    public void A_spiked_ball_bounces_and_kills_when_it_comes_down_on_him()
    {
        var w = Bare(r => r.Balls.Add(new BallDef(4, 4, 2, 0)));
        Put(w, 4.5, 4.5);
        double low = 9, high = 0;
        for (int i = 0; i < 120; i++)
        {
            w.Clock += Field.Step;
            low = Math.Min(low, Sim.BallZ(w, w.Here.Balls[0]));
            high = Math.Max(high, Sim.BallZ(w, w.Here.Balls[0]));
        }
        Assert.InRange(low, 0, 0.05);
        Assert.InRange(high, Sim.BallHeight - 0.05, Sim.BallHeight);
        Run(w, 120);
        Assert.True(w.Hero.Dead);
        Assert.Equal("A SPIKED BALL", w.Cause);
    }

    // ───────────────────────────── charms and the cauldron ─────────────────────────────

    [Fact]
    public void A_charm_is_picked_up_and_the_cauldron_takes_the_one_it_wants()
    {
        var w = Bare(r => { r.Charm = Charm.Gem; r.CharmAt = (5, 4); });
        Put(w, 4.5, 4.5);
        Run(w, 30, new Controls(1, 0));
        Assert.Contains(Charm.Gem, w.Carried);
        Assert.False(Sim.Present(w, w.Here));
        w.Carried.Add(Charm.Bell);
        Sim.Enter(w, 1, Side.West);
        Put(w, 3.5, 4.5);
        Run(w, 1);
        Assert.Equal(1, w.Delivered);
        Assert.Equal(Charm.Bell, w.WantedNow);
        Assert.Contains(Charm.Bell, w.Carried);
        Run(w, 1);
        Assert.Equal(2, w.Delivered);
        Assert.True(w.Cured);
    }

    [Fact]
    public void A_charm_up_on_a_block_needs_a_jump()
    {
        var w = Bare(r => { r.Charm = Charm.Gem; r.CharmAt = (5, 4); r.Block[5, 4] = 1; });
        Put(w, 4.5, 4.5);
        Run(w, 40, new Controls(1, 0));
        Assert.Empty(w.Carried);
        Put(w, 4.5, 4.5);
        Sim.Step(w, new Controls(1, 0, true));
        Run(w, 50);
        Assert.Contains(Charm.Gem, w.Carried);
    }

    [Fact]
    public void Day_turns_to_night_and_the_wolf_in_the_wizards_room_is_struck_down()
    {
        var w = Bare();
        Run(w, (int)(60 * Sim.DayLength) + 2);
        Assert.True(w.Night);
        Assert.True(w.Changing > 0);
        double x = w.Hero.X;
        Run(w, 30, new Controls(1, 0));
        Assert.Equal(x, w.Hero.X);
        Run(w, 60);
        Sim.Enter(w, 1, Side.West);
        Run(w, (int)(60 * (Sim.WizardGrace - 0.5)));
        Assert.False(w.Hero.Dead);
        Run(w, 60);
        Assert.True(w.Hero.Dead);
        Assert.Equal("THE WIZARD'S CURSE", w.Cause);
    }

    [Fact]
    public void Night_turns_to_day_and_the_days_are_counted()
    {
        var w = Bare();
        Run(w, (int)(60 * (Sim.DayLength + Sim.NightLength)) + 2);
        Assert.False(w.Night);
        Assert.Equal(1, w.Day);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void Killed_he_starts_again_at_the_door_he_came_in_by()
    {
        var g = Engine();
        g.Go(0);
        var entry = g.World.Entry;
        g.World.Hero.X = 2.5;
        Sim.Die(g.World, "TEST");
        g.Run(1);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal(entry, (g.World.Hero.X, g.World.Hero.Y, g.World.Hero.Z));
        Assert.Equal(Phase.Play, g.Phase);
    }

    [Fact]
    public void Every_charm_in_the_cauldron_lifts_the_curse_with_the_days_left_counted_in()
    {
        var g = Engine();
        g.Go(0);
        g.World.Delivered = g.World.Castle.Wanted.Count;
        g.World.Day = 30;
        g.Run(1);
        Assert.Equal(Phase.Cured, g.Phase);
        Assert.Equal((Sim.Days - 30) * GameEngine.DayPoints + GameEngine.CharmPoints, g.Score);
        g.Run((int)(60 * GameEngine.CuredSeconds) + 2);
        Assert.Equal(1, g.Level);
        Assert.Equal(1, g.Cleared);
    }

    [Fact]
    public void Forty_days_gone_is_the_end_then_the_high_scores()
    {
        var g = Engine();
        g.Go(0);
        g.World.Day = Sim.Days;
        g.Run(1);
        Assert.Equal(Phase.TooLate, g.Phase);
        g.Run(60 * 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_last_life_lost_is_game_over_and_a_good_score_goes_on_the_table()
    {
        var g = Engine();
        g.Go(0);
        g.SetLives(1);
        g.World.Delivered = 0;
        // Points come from rooms and charms; a big pile of them for the table.
        typeof(GameEngine).GetProperty(nameof(GameEngine.Score))!.SetValue(g, 25000);
        Sim.Die(g.World, "TEST");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 3);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.GameOverSeconds) + 3);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "SAB") g.Initials!.Type(ch);
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("SAB", g.HighScores.Entries[g.NewEntryIndex].Initials);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        var (x, y) = (g.World.Hero.X, g.World.Hero.Y);
        g.Idle(60 * 5);
        Assert.NotEqual((x, y), (g.World.Hero.X, g.World.Hero.Y));
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(GameEngine.Lives0, g.Lives);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go(0);
        g.Run(10, new Controls(0, -1));
        g.Pause();
        var at = (g.World.Hero.X, g.World.Hero.Y, g.World.DayTime);
        g.Run(120, new Controls(0, -1));
        Assert.Equal(at, (g.World.Hero.X, g.World.Hero.Y, g.World.DayTime));
        g.Resume();
        g.Run(10, new Controls(0, -1));
        Assert.NotEqual(at.Item2, g.World.Hero.Y);
    }

    [Fact]
    public void The_keys_walk_and_jump_him()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Down);
        Assert.Equal(1, input.Player.Dy);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyUp(Key.Right);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Jump);
        input.OnKeyUp(Key.Space);

        // Up through to Sabreman: away up the screen (north).
        var w = Bare();
        Put(w, 4.5, 4.5);
        input.OnKeyDown(Key.Up);
        Run(w, 20, input.Player);
        Assert.True(w.Hero.Y < 4.4);
    }
}
