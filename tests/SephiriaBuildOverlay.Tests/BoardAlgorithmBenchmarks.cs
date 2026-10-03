using System.Diagnostics;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;
using Xunit.Abstractions;

namespace SephiriaBuildOverlay.Tests;

public sealed class BoardAlgorithmBenchmarks
{
    private readonly ITestOutputHelper _output;
    public BoardAlgorithmBenchmarks(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ExactSmallBoardsMatchIndependentSlotEnumerationIncludingDuplicateCaps()
    {
        var random = new Random(88732);
        for (var test = 0; test < 100; test++)
        {
            var levels = Enumerable.Range(0, 5).Select(_ => random.Next(-2, 8)).ToArray();
            var artifacts = new[] { new BoardArtifact("a", "ice", new(0, 0), 3, 0, true), new BoardArtifact("b", "ice", new(1, 0), 5, 0, true),
                new BoardArtifact("c", "other", new(2, 0), 7, 0, true) };
            var input = new BoardOptimizationInput(5, 1, 5, levels.Select((v, i) => new BoardCell(new(i, 0), v)), artifacts,
                Array.Empty<BoardTablet>(), artifacts.ToDictionary(x => x.Id, x => x.Position),
                new[] { new ArtifactTarget("ice", 1, TargetRole.Required, 0), new ArtifactTarget("other", 1, TargetRole.Recommended, 0) });
            var result = new JointBoardPlanner().Solve(input);
            (int Active, int Level, int RecActive, int RecLevel) best = (-1, -1, -1, -1);
            for (var a = 0; a < 5; a++) for (var b = 0; b < 5; b++) for (var c = 0; c < 5; c++)
            {
                if (a == b || a == c || b == c) continue;
                var score = (Math.Max(levels[a] >= 0 ? 1 : 0, levels[b] >= 0 ? 1 : 0),
                    Math.Max(levels[a] >= 0 ? Math.Min(3, levels[a]) : 0, levels[b] >= 0 ? Math.Min(5, levels[b]) : 0),
                    levels[c] >= 0 ? 1 : 0, Math.Max(0, Math.Min(7, levels[c])));
                if (score.CompareTo(best) > 0) best = score;
            }
            Assert.True(result.ProvenOptimal); Assert.Equal(best.Active, result.After.RequiredActive); Assert.Equal(best.Level, result.After.RequiredLevel);
            Assert.Equal(best.RecActive, result.After.RecommendedActive); Assert.Equal(best.RecLevel, result.After.RecommendedLevel);
            Assert.InRange(result.Evaluations, 1, 6000);
        }
    }

    [Fact]
    public void BenchmarkLegacyAgainstHybridOnDenseBoards()
    {
        var legacy = new LegacyBoardPlanner(); var hybrid = new JointBoardPlanner();
        var oldTime = TimeSpan.Zero; var newTime = TimeSpan.Zero;
        for (var run = -1; run < 8; run++)
        {
            var input = DenseBoard(Math.Max(0, run));
            var watch = Stopwatch.StartNew(); var old = legacy.Solve(input, evaluationBudget: 1200); watch.Stop(); var oldElapsed = watch.Elapsed;
            watch.Restart(); var current = hybrid.Solve(input, evaluationBudget: 1200); watch.Stop();
            Assert.Equal(input.Items.Count, current.Layout.Positions.Where(x => input.Items.ContainsKey(x.Key)).Select(x => x.Value).Distinct().Count());
            Assert.True(current.After.CompareBenefits(current.Before) >= 0); Assert.InRange(current.Evaluations, 0, 1200);
            Assert.True((current.After.RequiredActive, current.After.RequiredLevel, current.After.RecommendedActive, current.After.RecommendedLevel)
                .CompareTo((old.After.RequiredActive, old.After.RequiredLevel, old.After.RecommendedActive, old.After.RecommendedLevel)) >= 0);
            if (run < 0) continue;
            oldTime += oldElapsed; newTime += watch.Elapsed;
            _output.WriteLine($"board {run}: legacy={oldElapsed.TotalMilliseconds:F1}ms hybrid={watch.Elapsed.TotalMilliseconds:F1}ms; required levels {old.After.RequiredLevel} -> {current.After.RequiredLevel}; recommended {old.After.RecommendedLevel} -> {current.After.RecommendedLevel}");
        }
        _output.WriteLine($"8-board aggregate: legacy={oldTime.TotalMilliseconds:F1}ms hybrid={newTime.TotalMilliseconds:F1}ms ratio={oldTime.TotalMilliseconds / newTime.TotalMilliseconds:F2}x. Synthetic CPU measurement, not game FPS.");
    }

    private static BoardOptimizationInput DenseBoard(int seed)
    {
        var random = new Random(seed + 47752); var cells = Enumerable.Range(0, 36).Select(i => new BoardCell(new(i % 6, i / 6), random.Next(0, 4))).ToArray();
        var artifacts = Enumerable.Range(0, 12).Select(i => new BoardArtifact("a" + i, "key" + i, cells[i].Position, 5 + i % 3, 0, true)).ToArray();
        var tablets = Enumerable.Range(0, 2).Select(i => new BoardTablet("t" + i, "tablet", cells[12 + i].Position, 0, true,
            cells.SelectMany(c => Enumerable.Range(0, 4).Select(r => new BoardTabletOption(c.Position, r,
                new[] { new BoardEffect(c.Position.Add(new GridPoint(r % 2 == 0 ? 1 : -1, r >= 2 ? 1 : 0)), BoardEffectKind.Add, 3) }))))).ToArray();
        var items = artifacts.ToDictionary(a => a.Id, a => a.Position); foreach (var t in tablets) items[t.Id] = t.Position;
        var goals = artifacts.Select((a, i) => new ArtifactTarget(a.Key, 1, i < 6 ? TargetRole.Required : TargetRole.Recommended, 0));
        return new BoardOptimizationInput(6, 6, 36, cells, artifacts, tablets, items, goals);
    }
}
