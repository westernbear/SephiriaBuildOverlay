using System.Numerics;

namespace SephiriaBuildOverlay.Plugin;

// Shortest augmenting paths for an m x n assignment (m <= n), using exact
// integers for lexicographic costs. See Crouse 2016, DOI 10.1109/TAES.2016.140952.
internal static class RectangularAssignment
{
    public static int[] Match(BigInteger[,] costs, BigInteger infinity, CancellationToken token)
    {
        var rows = costs.GetLength(0); var columns = costs.GetLength(1);
        if (rows > columns) throw new ArgumentException("More artifacts than available cells.");
        var rowMatch = Enumerable.Repeat(-1, rows).ToArray();
        var columnMatch = Enumerable.Repeat(-1, columns).ToArray();
        var u = new BigInteger[rows]; var v = new BigInteger[columns];
        for (var root = 0; root < rows; root++)
        {
            token.ThrowIfCancellationRequested();
            var distance = Enumerable.Repeat(infinity, columns).ToArray(); var path = new int[columns];
            var visitedRows = new bool[rows]; var visitedColumns = new bool[columns];
            var remaining = Enumerable.Range(0, columns).ToArray(); var count = columns;
            var row = root; var minimum = BigInteger.Zero; var sink = -1;
            while (sink < 0)
            {
                token.ThrowIfCancellationRequested(); visitedRows[row] = true;
                var lowest = infinity; var chosen = -1;
                for (var index = 0; index < count; index++)
                {
                    var column = remaining[index]; var reduced = minimum + costs[row, column] - u[row] - v[column];
                    if (reduced < distance[column]) { distance[column] = reduced; path[column] = row; }
                    var prefer = chosen < 0 || columnMatch[column] < 0 && columnMatch[remaining[chosen]] >= 0 ||
                        (columnMatch[column] < 0) == (columnMatch[remaining[chosen]] < 0) && column < remaining[chosen];
                    if (distance[column] < lowest || distance[column] == lowest && prefer) { lowest = distance[column]; chosen = index; }
                }
                if (chosen < 0 || lowest == infinity) throw new InvalidOperationException("No feasible assignment.");
                minimum = lowest; var next = remaining[chosen]; visitedColumns[next] = true;
                remaining[chosen] = remaining[--count];
                if (columnMatch[next] < 0) sink = next; else row = columnMatch[next];
            }
            u[root] += minimum;
            for (var i = 0; i < rows; i++) if (visitedRows[i] && i != root) u[i] += minimum - distance[rowMatch[i]];
            for (var j = 0; j < columns; j++) if (visitedColumns[j]) v[j] -= minimum - distance[j];
            var columnToAssign = sink;
            while (true)
            {
                var owner = path[columnToAssign]; var previous = rowMatch[owner];
                rowMatch[owner] = columnToAssign; columnMatch[columnToAssign] = owner;
                if (owner == root) break;
                columnToAssign = previous;
            }
        }
        return rowMatch;
    }
}
