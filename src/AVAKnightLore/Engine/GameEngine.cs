namespace AVAKnightLore.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The castle named; a moment before the days start.</summary>
    Start,
    Play,
    /// <summary>Killed: a moment, then back at the door he came in by (or the game is over).</summary>
    Dying,
    /// <summary>Every charm in the cauldron: the curse lifted, the days left counted in, then the next castle.</summary>
    Cured,
    /// <summary>The fortieth day over: the curse is for ever.</summary>
    TooLate,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Jump, Land, Pickup, Cauldron, Cured, Door, Die, Dusk, Dawn, ExtraLife, DayCount,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.2, DyingSeconds = 1.8, CuredSeconds = 5, GameOverSeconds = 4;
    public const int Lives0 = 5, ExtraEvery = 10000, CharmPoints = 1000, RoomPoints = 50, DayPoints = 500;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music;
    private readonly HashSet<int> _visited = [];
    private int _nextExtra;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Level { get; private set; }
    /// <summary>Castles cured this game.</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Jump && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > GameOverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        Score = 0;
        _nextExtra = ExtraEvery;
        Enter(0);
    }

    public int Score { get; private set; }

    private void Enter(int level)
    {
        Level = level;
        Sim.Start(World, CastleBook.Build(level));
        _visited.Clear();
        _visited.Add(World.Room);
        _delivered = 0;
        SetPhase(Phase.Start);
        Message = World.Castle.Name;
        MessageTime = StartSeconds;
        Pilot.Reset();
    }

    private int _delivered;

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                int room = w.Room;
                Sim.Step(w, c);
                if (w.Room != room && _visited.Add(w.Room)) { Score += RoomPoints; ExtraLives(); }
                if (w.Delivered > _delivered) { _delivered = w.Delivered; Score += CharmPoints; ExtraLives(); }
                if (w.Hero.Dead) { SetPhase(Phase.Dying); Music(false); Message = w.Cause; MessageTime = DyingSeconds; }
                else if (w.Cured)
                {
                    SetPhase(Phase.Cured);
                    Music(false);
                    int bonus = (Sim.Days - w.Day) * DayPoints;
                    Score += bonus;
                    ExtraLives();
                    Message = $"THE CURSE IS LIFTED!  {Sim.Days - w.Day} DAYS LEFT  BONUS {bonus}";
                    MessageTime = CuredSeconds;
                }
                else if (w.Day >= Sim.Days)
                {
                    SetPhase(Phase.TooLate);
                    Music(false);
                    Message = "FORTY DAYS - THE CURSE IS FOR EVER";
                    MessageTime = 4;
                }
                break;
            }
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    Sim.Restart(w);
                    Pilot.Reset();
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Cured:
                if (PhaseTime > CuredSeconds)
                {
                    Cleared++;
                    if ((Level + 1) % CastleBook.Count == 0) Play(Sfx.Victory);
                    Enter(Level + 1);
                }
                break;
            case Phase.TooLate:
                if (PhaseTime > 4) { End(); return; }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void ExtraLives()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a castle, past its name.</summary>
    public void Go(int level = 0)
    {
        NewGame();
        State = GameState.Playing;
        Enter(level);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
