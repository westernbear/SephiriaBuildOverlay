using System.Numerics;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class RectangularAssignmentTests
{
    [Fact]
    public void MatchesIndependentExhaustiveOracleOn200NegativeRectangularMatrices()
    {
        var random = new Random(343322);
        for (var run = 0; run < 200; run++)
        {
            var rows = random.Next(1, 5); var columns = random.Next(rows, 7); var costs = new BigInteger[rows, columns];
            for (var i = 0; i < rows; i++) for (var j = 0; j < columns; j++) costs[i, j] = random.Next(-100, 100);
            var result = RectangularAssignment.Match(costs, 1000000, default);
            var best = new BigInteger(1000000); var used = new bool[columns];
            void Visit(int row, BigInteger sum)
            {
                if (row == rows) { if (sum < best) best = sum; return; }
                for (var column = 0; column < columns; column++)
                {
                    if (used[column]) continue;
                    used[column] = true; Visit(row + 1, sum + costs[row, column]); used[column] = false;
                }
            }
            Visit(0, 0);
            Assert.Equal(rows, result.Distinct().Count());
            Assert.Equal(best, Enumerable.Range(0, rows).Aggregate(BigInteger.Zero, (s, i) => s + costs[i, result[i]]));
        }
    }

    [Fact]
    public void PreservesLexicographicIntegersBeyondLongAndDeterministicTies()
    {
        var big = BigInteger.Pow(10, 40);
        var costs = new BigInteger[,] { { -big, -big + 1, 0 }, { -big + 1, -big, 0 } };
        Assert.Equal(new[] { 0, 1 }, RectangularAssignment.Match(costs, big * 100, default));
        Assert.Equal(new[] { 0, 1, 2 }, RectangularAssignment.Match(new BigInteger[3, 64], 10000, default));
    }

    [Fact]
    public void ImpossibleSizeAndCancellationFailSafely()
    {
        Assert.Throws<ArgumentException>(() => RectangularAssignment.Match(new BigInteger[3, 2], 1000, default));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => RectangularAssignment.Match(new BigInteger[2, 3], 1000, cts.Token));
    }
}
