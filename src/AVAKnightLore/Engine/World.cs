namespace AVAKnightLore.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>Sabreman (or, by night, the wolf he becomes): where he stands in the room, in squares, and how high.</summary>
public sealed class Hero
{
    public const double Half = 0.27, Speed = 3.0, JumpSpeed = 6.6, Gravity = 20, Tall = 1.2;
    public double X, Y, Z, Vz, Walk;
    public int Jdx, Jdy, FaceX = 0, FaceY = 1;
    public bool Airborne, Dead, JumpPrev;
    public Hero Copy() => (Hero)MemberwiseClone();
}

public sealed class Guard
{
    public GuardDef Def;
    public double Pos;
    public int Dir = 1;
    public double X => Def.AlongX ? Pos : Def.Fixed;
    public double Y => Def.AlongX ? Def.Fixed : Pos;
    public Guard Copy() => (Guard)MemberwiseClone();
}

/// <summary>Everything in the castle that changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public CastleDef Castle = null!;
    public int Room;
    public Hero Hero = new();
    public List<Guard> Guards = [];
    /// <summary>The charms picked up (in his pack), and those already in the cauldron.</summary>
    public HashSet<Charm> Carried = [];
    public int Delivered;
    /// <summary>Where he came into this room (he starts again here if killed).</summary>
    public (double X, double Y, double Z) Entry;
    public double Clock;
    /// <summary>Time of day: days since the curse began (whole days), and how far through this one.</summary>
    public double DayTime;
    public int Day;
    /// <summary>Changing (into the wolf at dusk, back at dawn): frozen for this long.</summary>
    public double Changing;
    public bool Night;
    /// <summary>How long the wolf has been in the wizard's room.</summary>
    public double Wolf;
    public string Cause = "";
    public List<Sfx> Heard = [];
    /// <summary>A world used only to try moves out: no charms picked up, no doors gone through.</summary>
    public bool Probe;
    public bool Cured => Delivered >= Castle.Wanted.Count;
    public Charm? WantedNow => Cured ? null : Castle.Wanted[Delivered];
    public RoomDef Here => Castle.Rooms[Room];

    public World Copy() => new()
    {
        Castle = Castle, Room = Room, Hero = Hero.Copy(), Guards = Guards.Select(g => g.Copy()).ToList(), Carried = [.. Carried],
        Delivered = Delivered, Entry = Entry, Clock = Clock, DayTime = DayTime, Day = Day, Changing = Changing, Night = Night, Wolf = Wolf,
        Cause = Cause, Probe = Probe,
    };
}
