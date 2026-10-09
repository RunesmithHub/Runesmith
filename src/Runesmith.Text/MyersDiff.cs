namespace Runesmith.Text;

/// <summary>Myers' O(ND) difference algorithm over sequences of ids, in linear space, with a cost limit that trades the shortest script
/// for speed when two sequences have little in common.</summary>
internal static class MyersDiff
{
    private const int MinimumCostLimit = 256;

    /// <summary>Finds the runs that differ between <paramref name="a"/> and <paramref name="b"/>; equal ids mean equal units.</summary>
    /// <param name="isBlankA">Whether each unit of <paramref name="a"/> is blank, to prefer runs that end at a blank line; or null.</param>
    /// <param name="isBlankB">The same for <paramref name="b"/>.</param>
    public static List<DiffHunk> Compute(int[] a, int[] b, bool[]? isBlankA, bool[]? isBlankB, CancellationToken cancellationToken)
    {
        var changedA = new bool[a.Length];
        var changedB = new bool[b.Length];
        var diagonals = a.Length + b.Length + 3;
        var search = new Search(a, b, new int[diagonals], new int[diagonals], b.Length + 1, Math.Max(MinimumCostLimit, (int)Math.Sqrt(diagonals)), cancellationToken);

        var pending = new Stack<(int Off1, int Lim1, int Off2, int Lim2, bool NeedMinimal)>();
        pending.Push((0, a.Length, 0, b.Length, false));
        while (pending.TryPop(out var range))
        {
            var (off1, lim1, off2, lim2, needMinimal) = range;
            while (off1 < lim1 && off2 < lim2 && a[off1] == b[off2])
            {
                off1++;
                off2++;
            }

            while (off1 < lim1 && off2 < lim2 && a[lim1 - 1] == b[lim2 - 1])
            {
                lim1--;
                lim2--;
            }

            if (off1 == lim1)
            {
                changedB.AsSpan(off2, lim2 - off2).Fill(true);
            }
            else if (off2 == lim2)
            {
                changedA.AsSpan(off1, lim1 - off1).Fill(true);
            }
            else
            {
                var split = search.Split(off1, lim1, off2, lim2, needMinimal);
                pending.Push((split.I1, lim1, split.I2, lim2, split.MinimalHigh));
                pending.Push((off1, split.I1, off2, split.I2, split.MinimalLow));
            }
        }

        var hunks = Hunks(changedA, changedB);
        Slide(hunks, a, b, isBlankA, isBlankB);
        return hunks;
    }

    private static List<DiffHunk> Hunks(bool[] changedA, bool[] changedB)
    {
        var hunks = new List<DiffHunk>();
        int i = 0, j = 0;
        while (i < changedA.Length || j < changedB.Length)
        {
            if (i < changedA.Length && j < changedB.Length && !changedA[i] && !changedB[j])
            {
                i++;
                j++;
                continue;
            }

            int startA = i, startB = j;
            while (i < changedA.Length && changedA[i])
                i++;
            while (j < changedB.Length && changedB[j])
                j++;

            // Unchanged units left on one side only cannot pair up; they count as changed.
            if (i == startA && j == startB)
            {
                if (j >= changedB.Length)
                    i = changedA.Length;
                else
                    j = changedB.Length;
            }

            hunks.Add(new DiffHunk(startA, i - startA, startB, j - startB));
        }

        return hunks;
    }

    // An inserted or deleted run can often move up or down over equal units; it is placed as low as it goes, or where it ends at a blank line.
    private static void Slide(List<DiffHunk> hunks, int[] a, int[] b, bool[]? isBlankA, bool[]? isBlankB)
    {
        for (var index = 0; index < hunks.Count; index++)
        {
            var hunk = hunks[index];
            if (hunk.OldLength > 0 == hunk.NewLength > 0)
                continue;

            var adds = hunk.OldLength == 0;
            var units = adds ? b : a;
            var blanks = adds ? isBlankB : isBlankA;
            var start = adds ? hunk.NewStart : hunk.OldStart;
            var length = adds ? hunk.NewLength : hunk.OldLength;
            var lowerBound = index > 0 ? (adds ? hunks[index - 1].NewEnd : hunks[index - 1].OldEnd) + 1 : 0;
            var upperBound = index < hunks.Count - 1 ? (adds ? hunks[index + 1].NewStart : hunks[index + 1].OldStart) - 1 : units.Length;

            var first = start;
            while (first > lowerBound && units[first - 1] == units[first + length - 1])
                first--;
            var last = start;
            while (last + length < upperBound && units[last] == units[last + length])
                last++;
            if (first == last)
                continue;

            var chosen = last;
            if (blanks is not null)
            {
                for (var position = last; position >= first; position--)
                {
                    if (blanks[position + length - 1])
                    {
                        chosen = position;
                        break;
                    }
                }
            }

            var shift = chosen - start;
            hunks[index] = new DiffHunk(hunk.OldStart + shift, hunk.OldLength, hunk.NewStart + shift, hunk.NewLength);
        }
    }

    private readonly record struct SplitPoint(int I1, int I2, bool MinimalLow, bool MinimalHigh);

    private readonly struct Search(int[] a, int[] b, int[] forward, int[] backward, int offset, int costLimit, CancellationToken cancellationToken)
    {
        // Finds where the middle snake of the shortest edit script crosses, or, past the cost limit, the furthest reaching diagonal.
        public SplitPoint Split(int off1, int lim1, int off2, int lim2, bool needMinimal)
        {
            int dmin = off1 - lim2, dmax = lim1 - off2;
            int fmid = off1 - off2, bmid = lim1 - lim2;
            var odd = ((fmid - bmid) & 1) != 0;
            int fmin = fmid, fmax = fmid, bmin = bmid, bmax = bmid;
            forward[fmid + offset] = off1;
            backward[bmid + offset] = lim1;

            for (var cost = 1; ; cost++)
            {
                if ((cost & 63) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                if (fmin > dmin)
                    forward[--fmin - 1 + offset] = -1;
                else
                    ++fmin;
                if (fmax < dmax)
                    forward[++fmax + 1 + offset] = -1;
                else
                    --fmax;

                for (var d = fmax; d >= fmin; d -= 2)
                {
                    var i1 = forward[d - 1 + offset] >= forward[d + 1 + offset] ? forward[d - 1 + offset] + 1 : forward[d + 1 + offset];
                    var i2 = i1 - d;
                    while (i1 < lim1 && i2 < lim2 && a[i1] == b[i2])
                    {
                        i1++;
                        i2++;
                    }

                    forward[d + offset] = i1;
                    if (odd && bmin <= d && d <= bmax && backward[d + offset] <= i1)
                        return new SplitPoint(i1, i2, true, true);
                }

                if (bmin > dmin)
                    backward[--bmin - 1 + offset] = int.MaxValue;
                else
                    ++bmin;
                if (bmax < dmax)
                    backward[++bmax + 1 + offset] = int.MaxValue;
                else
                    --bmax;

                for (var d = bmax; d >= bmin; d -= 2)
                {
                    var i1 = backward[d - 1 + offset] < backward[d + 1 + offset] ? backward[d - 1 + offset] : backward[d + 1 + offset] - 1;
                    var i2 = i1 - d;
                    while (i1 > off1 && i2 > off2 && a[i1 - 1] == b[i2 - 1])
                    {
                        i1--;
                        i2--;
                    }

                    backward[d + offset] = i1;
                    if (!odd && fmin <= d && d <= fmax && i1 <= forward[d + offset])
                        return new SplitPoint(i1, i2, true, true);
                }

                if (needMinimal || cost < costLimit)
                    continue;

                return Furthest(off1, lim1, off2, lim2, fmin, fmax, bmin, bmax);
            }
        }

        private SplitPoint Furthest(int off1, int lim1, int off2, int lim2, int fmin, int fmax, int bmin, int bmax)
        {
            int forwardBest = -1, forwardBestI1 = -1;
            for (var d = fmax; d >= fmin; d -= 2)
            {
                var i1 = Math.Min(forward[d + offset], lim1);
                var i2 = i1 - d;
                if (lim2 < i2)
                {
                    i1 = lim2 + d;
                    i2 = lim2;
                }

                if (forwardBest < i1 + i2)
                {
                    forwardBest = i1 + i2;
                    forwardBestI1 = i1;
                }
            }

            int backwardBest = int.MaxValue, backwardBestI1 = int.MaxValue;
            for (var d = bmax; d >= bmin; d -= 2)
            {
                var i1 = Math.Max(off1, backward[d + offset]);
                var i2 = i1 - d;
                if (i2 < off2)
                {
                    i1 = off2 + d;
                    i2 = off2;
                }

                if (i1 + i2 < backwardBest)
                {
                    backwardBest = i1 + i2;
                    backwardBestI1 = i1;
                }
            }

            return lim1 + lim2 - backwardBest < forwardBest - (off1 + off2)
                ? new SplitPoint(forwardBestI1, forwardBest - forwardBestI1, true, false)
                : new SplitPoint(backwardBestI1, backwardBest - backwardBestI1, false, true);
        }
    }
}
