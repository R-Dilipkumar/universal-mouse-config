namespace MouseConfig.Core.Input;

/// <summary>
/// Measures how many movement reports per second a mouse sends (its report or "polling" rate), from Raw Input.
/// Works for any mouse. Move the mouse quickly in circles: the rate only shows while reports keep coming.
/// </summary>
public sealed class ReportRateMeter
{
    public static readonly int[] StandardRates = [125, 250, 500, 1000, 2000, 4000, 8000];
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private readonly Queue<TimeSpan> _moves = new();

    /// <summary>Reports counted in the last second.</summary>
    public int Current { get; private set; }

    /// <summary>The highest one-second count seen so far.</summary>
    public int Peak { get; private set; }

    public void AddMove(TimeSpan now)
    {
        _moves.Enqueue(now);
        while (_moves.Count > 0 && now - _moves.Peek() > Window)
        {
            _moves.Dequeue();
        }
        // The first second isn't full yet, so it would read too low; count only once a full second is covered.
        Current = now - FirstMoveTime >= Window ? _moves.Count : 0;
        Peak = Math.Max(Peak, Current);
    }

    public void Reset()
    {
        _moves.Clear();
        Current = 0;
        Peak = 0;
        _first = null;
    }

    private TimeSpan? _first;

    private TimeSpan FirstMoveTime => _first ??= _moves.Count > 0 ? _moves.Peek() : TimeSpan.Zero;

    /// <summary>The usual rate this measurement is closest to, if it's within 20% of one. Null otherwise.</summary>
    public static int? NearestStandard(int measured)
    {
        if (measured <= 0)
        {
            return null;
        }
        int nearest = StandardRates.MinBy(rate => Math.Abs(rate - measured));
        return Math.Abs(nearest - measured) <= nearest * 0.2 ? nearest : null;
    }
}
