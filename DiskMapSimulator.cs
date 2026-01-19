using System.Text;

namespace FTMapScreenSaver;

// Color legend
// Black: empty
// Green: regular files, unfragmented
// Dark green: spacehogs, unfragmented
// Yellow: fragmented
// Red: unmovable
// White: busy
// Pink: MFT reserved zone (NTFS only)
// Gray: unknown/in use
public enum CellType
{
    Empty = 0,
    Regular = 1,
    SpaceHog = 2,
    Fragmented = 3,
    Unmovable = 4,
    Busy = 5,
    MftReserved = 6,
    Unknown = 7
}

public static class DiskMapPalette
{
    // Intentionally loud, Windows-2006-era palette.
    public static Color GetColor(CellType t) => t switch
    {
        CellType.Empty => Color.Black,
        CellType.Regular => Color.Lime,
        CellType.SpaceHog => Color.FromArgb(0, 140, 0),
        CellType.Fragmented => Color.OrangeRed,
        CellType.Unmovable => Color.Red,
        CellType.Busy => Color.White,
        CellType.MftReserved => Color.DarkGreen,
        CellType.Unknown => Color.Black,
        _ => Color.Gray
    };
}

public readonly record struct SegmentChange(int StartIndex, int Length, CellType NewType);

public sealed class DiskMapSimulator
{
    private readonly Random _rng;

    private CellType[] _cells = Array.Empty<CellType>();
    private int _width;
    private int _height;

    private int _ticks;
    private long _lastMoveFrom;
    private long _lastMoveTo;
    private int _lastMoveLen;

    private int _fragmentedSegments;

    public DiskMapSimulator(int seed) => _rng = new Random(seed);

    public void Reset(int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        _cells = new CellType[_width * _height];
        GenerateInitialMap();

        _ticks = 0;
        _lastMoveFrom = 0;
        _lastMoveTo = 0;
        _lastMoveLen = 0;
    }

    private int NextLengthCapped(int minLen, int maxLen)
    {
        if (maxLen <= 0) return 0;

        int min = Math.Min(minLen, maxLen);

        // Random.Next(min, maxExclusive) is max-exclusive; +1 makes it inclusive.
        // This keeps us safe when remaining space is smaller than the desired minimum.
        return _rng.Next(min, maxLen + 1);
    }

    public CellType GetCellByIndex(int index) => _cells[index];

    public IReadOnlyList<string> GetStatusLines()
    {
        // Keep it deterministic and short.
        var lines = new List<string>(capacity: 2);

        // Fake progress (loops)
        double progress = ((_ticks % 6000) / 6000.0) * 100.0;

        lines.Add($"v3.36   Phase 2: Defragment   {progress:0.00}%   Moving {_lastMoveLen} clusters from {_lastMoveFrom} to {_lastMoveTo}");
        lines.Add($"Fragments: {_fragmentedSegments}   Busy: white   Unmovable: red   MFT: dark green");

        return lines;
    }

    public List<SegmentChange> Step(int steps)
    {
        var changes = new List<SegmentChange>(capacity: steps * 2);

        for (int i = 0; i < steps; i++)
        {
            _ticks++;

            // Fade old "busy" highlights back to regular
            if (_ticks % 10 == 0)
            {
                var clear = ClearBusyHighlights(maxSegments: 12);
                changes.AddRange(clear);
            }

            // Occasionally inject new fragmentation to keep it lively
            if (_ticks % 240 == 0)
            {
                changes.AddRange(InjectFragmentation());
            }

            // Main act: move one fragmented segment into a nearby empty gap.
            if (!TryMoveOneFragmentedSegment(out var moveChanges))
            {
                // If no suitable move, do subtle ambient motion.
                moveChanges = AmbientNudge();
            }

            if (moveChanges.Count > 0)
                changes.AddRange(moveChanges);
        }

        return changes;
    }

    private void GenerateInitialMap()
    {
        Array.Fill(_cells, CellType.Regular);

        int total = _cells.Length;

        // Big empty tail (common on not-full disks)
        int emptyTail = (int)(total * 0.18);
        for (int i = total - emptyTail; i < total; i++)
            _cells[i] = CellType.Empty;

        // A pink MFT reserved zone-ish stripe somewhere near the start.
        int mftStart = (int)(total * 0.06);
        int mftLen = Math.Max(800, total / 400);
        PaintSegment(mftStart, mftLen, CellType.MftReserved);

        // Spacehogs band near later zone
        int hogStart = (int)(total * 0.72);
        int hogLen = Math.Max(2000, total / 120);
        PaintSegment(hogStart, hogLen, CellType.SpaceHog);

        // Unmovable scattered
        for (int k = 0; k < 40; k++)
        {
            int start = _rng.Next(0, (int)(total * 0.35));
            int len = _rng.Next(120, 1200);
            PaintSegment(start, len, CellType.Unmovable);
        }

        // Fragmented long stripes (yellow)
        _fragmentedSegments = 0;
        for (int k = 0; k < 140; k++)
        {
            int row = _rng.Next(0, _height);
            int xStart = _rng.Next(0, _width);
            int maxLen = Math.Min(_width - xStart, 600);
            int len = NextLengthCapped(40, maxLen);
            if (len <= 0) continue;
            int start = row * _width + xStart;

            // Don't overwrite MFT band too much
            if (_cells[start] == CellType.MftReserved) continue;

            PaintSegment(start, len, CellType.Fragmented);
            _fragmentedSegments++;
        }

        // Random empty specks/holes to create black lines
        for (int k = 0; k < 120; k++)
        {
            int row = _rng.Next(0, _height);
            int xStart = _rng.Next(0, _width);
            int maxLen = Math.Min(_width - xStart, 200);
            int len = NextLengthCapped(10, maxLen);
            if (len <= 0) continue;
            int start = row * _width + xStart;
            PaintSegment(start, len, CellType.Empty);
        }
    }

    private void PaintSegment(int start, int length, CellType type)
    {
        if (length <= 0) return;

        int s = Math.Clamp(start, 0, _cells.Length - 1);
        int e = Math.Clamp(start + length, 0, _cells.Length);

        for (int i = s; i < e; i++)
            _cells[i] = type;
    }

    private List<SegmentChange> ClearBusyHighlights(int maxSegments)
    {
        var changes = new List<SegmentChange>(capacity: maxSegments);

        for (int i = 0; i < maxSegments; i++)
        {
            int idx = _rng.Next(0, _cells.Length);

            // Find a short busy run starting here
            if (_cells[idx] != CellType.Busy) continue;

            int run = 0;
            int start = idx;
            while (idx + run < _cells.Length && _cells[idx + run] == CellType.Busy && run < 2000)
                run++;

            if (run <= 0) continue;

            // Revert to Regular by default; preserve Empty if we accidentally highlight empties
            for (int p = start; p < start + run; p++)
                _cells[p] = CellType.Regular;

            changes.Add(new SegmentChange(start, run, CellType.Regular));
        }

        return changes;
    }

    private List<SegmentChange> InjectFragmentation()
    {
        // Add a couple new yellow stripes so the animation never "finishes".
        var changes = new List<SegmentChange>(capacity: 8);

        int segCount = _rng.Next(2, 6);
        for (int i = 0; i < segCount; i++)
        {
            int row = _rng.Next(0, _height);
            int xStart = _rng.Next(0, _width);
            int maxLen = Math.Min(_width - xStart, 900);
            int len = NextLengthCapped(40, maxLen);
            if (len <= 0) continue;
            int start = row * _width + xStart;

            // Avoid dumping fragmentation into the empty tail too much
            if (start > (int)(_cells.Length * 0.86)) continue;

            PaintSegment(start, len, CellType.Fragmented);
            _fragmentedSegments++;
            changes.Add(new SegmentChange(start, len, CellType.Fragmented));
        }

        return changes;
    }

    private bool TryMoveOneFragmentedSegment(out List<SegmentChange> changes)
    {
        changes = new List<SegmentChange>(capacity: 4);

        // Find a fragmented segment start
        int start = -1;
        for (int tries = 0; tries < 400; tries++)
        {
            int idx = _rng.Next(0, _cells.Length);
            if (_cells[idx] != CellType.Fragmented) continue;

            // Walk back to segment start
            int s = idx;
            while (s > 0 && _cells[s - 1] == CellType.Fragmented) s--;
            start = s;
            break;
        }

        if (start < 0) return false;

        // Determine length of this fragmented run
        int len = 0;
        while (start + len < _cells.Length && _cells[start + len] == CellType.Fragmented && len < 6000)
            len++;

        if (len < 8) return false;

        // Find an empty gap of similar size somewhere earlier
        int gapStart = FindEmptyGapBefore(start, minLen: Math.Min(len, 1200));
        if (gapStart < 0) return false;

        int moveLen = Math.Min(len, NextLengthCapped(80, Math.Min(len, 1200)));

        // Apply: source becomes Regular (green), destination becomes Busy (white) for a bit.
        PaintSegment(start, moveLen, CellType.Regular);
        PaintSegment(gapStart, moveLen, CellType.Busy);

        _fragmentedSegments = Math.Max(0, _fragmentedSegments - 1);

        _lastMoveFrom = start;
        _lastMoveTo = gapStart;
        _lastMoveLen = moveLen;

        changes.Add(new SegmentChange(start, moveLen, CellType.Regular));
        changes.Add(new SegmentChange(gapStart, moveLen, CellType.Busy));

        return true;
    }

    private int FindEmptyGapBefore(int beforeIndex, int minLen)
    {
        // Scan a handful of random earlier positions for a sufficiently long empty run.
        minLen = Math.Max(20, minLen);

        int attempts = 120;
        int upper = Math.Max(1, beforeIndex);

        for (int a = 0; a < attempts; a++)
        {
            int idx = _rng.Next(0, upper);

            // Seek to start of empty run
            if (_cells[idx] != CellType.Empty) continue;

            int s = idx;
            while (s > 0 && _cells[s - 1] == CellType.Empty) s--;

            int run = 0;
            while (s + run < upper && _cells[s + run] == CellType.Empty && run < 8000)
                run++;

            if (run >= minLen)
                return s + _rng.Next(0, Math.Max(1, run - minLen));
        }

        return -1;
    }

    private List<SegmentChange> AmbientNudge()
    {
        var changes = new List<SegmentChange>(capacity: 2);

        // Make a tiny busy blip that fades quickly.
        int row = _rng.Next(0, _height);
        int x = _rng.Next(0, _width);
        int start = row * _width + x;
        int len = NextLengthCapped(30, Math.Min(260, _width - x));

        // Don't scribble into the MFT reserved zone too much
        if (_cells[start] == CellType.MftReserved) return changes;

        PaintSegment(start, len, CellType.Busy);
        _lastMoveFrom = start;
        _lastMoveTo = start + len;
        _lastMoveLen = len;

        changes.Add(new SegmentChange(start, len, CellType.Busy));
        return changes;
    }
}
