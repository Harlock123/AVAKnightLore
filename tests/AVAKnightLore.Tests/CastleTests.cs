using AVAKnightLore.Engine;

namespace AVAKnightLore.Tests;

/// <summary>The castles: built to be got round, and cured by the computer Sabreman.</summary>
public class CastleTests(ITestOutputHelper output)
{
    public static TheoryData<int> Castles() => [.. Enumerable.Range(0, CastleBook.Count)];

    [Theory]
    [MemberData(nameof(Castles))]
    public void Every_room_reached_every_door_and_charm_got_to_and_the_cauldron_in_the_middle(int level)
    {
        var c = CastleBook.Build(level);
        Assert.All(c.Distances(c.Start), d => Assert.True(d >= 0));
        Assert.True(c.Rooms[c.CauldronRoom].Cauldron);
        Assert.Equal(c.Wanted.Count, c.Rooms.Count(r => r.Charm != null));
        Assert.Equal(c.Wanted.Count, c.Wanted.Distinct().Count());
        Assert.Null(c.Rooms[c.Start].Charm);
        Assert.Null(c.Rooms[c.CauldronRoom].Charm);
        foreach (var r in c.Rooms)
        {
            Assert.True(CastleBook.Proven(r), $"room {r.Index}");
            for (int s = 0; s < 4; s++)
                if (c.Through(r.Index, (Side)s) is var n && n >= 0) Assert.True(c.Rooms[n].Doors[(int)RoomDef.Opposite((Side)s)]);
        }
    }

    [Fact]
    public void Each_castle_is_larger_with_more_charms_than_the_last()
    {
        for (int i = 1; i < CastleBook.Count; i++)
        {
            Assert.True(CastleBook.Build(i).Rooms.Count > CastleBook.Build(i - 1).Rooms.Count);
            Assert.True(CastleBook.Build(i).Wanted.Count > CastleBook.Build(i - 1).Wanted.Count);
        }
    }

    [Fact]
    public void The_castle_is_full_of_trouble()
    {
        var c = CastleBook.Build(1);
        Assert.Contains(c.Rooms, r => r.Guards.Count > 0);
        Assert.Contains(c.Rooms, r => r.Balls.Count > 0);
        Assert.Contains(c.Rooms, r => Enumerable.Range(0, 81).Any(i => r.Floor[i % 9, i / 9] == Floor.Spikes));
        Assert.Contains(c.Rooms, r => Enumerable.Range(0, 81).Any(i => r.Floor[i % 9, i / 9] == Floor.Pit));
    }

    [Theory]
    [MemberData(nameof(Castles))]
    public void Every_castle_is_cured_by_the_computer_Sabreman(int level)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaknightlore-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(level);
        g.SetLives(10);
        int t = 0, lost = 0;
        var last = g.Phase;
        for (; t < 60 * 600 && g.State == GameState.Playing && g.Phase != Phase.Cured; t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Phase == Phase.Dying && last != Phase.Dying) lost++;
            last = g.Phase;
        }
        output.WriteLine($"{g.World.Castle.Name}: {g.Phase} after {t / 60}s (day {g.World.Day + 1}), {lost} lives lost");
        Assert.Equal(Phase.Cured, g.Phase);
        Assert.True(lost <= 2, $"{lost} lives lost");
    }

    [Fact]
    public void Wandering_aimlessly_the_curse_wins()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaknightlore-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(0);
        for (int t = 0; t < 60 * 1000 && g.State == GameState.Playing; t++) g.Tick(new Controls(1, 0, t % 50 == 0));
        Assert.Equal(0, g.World.Delivered);
        Assert.NotEqual(GameState.Playing, g.State);
    }
}
