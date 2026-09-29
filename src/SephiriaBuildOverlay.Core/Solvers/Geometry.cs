namespace SephiriaBuildOverlay.Core.Solvers;

public readonly struct GridPoint : IEquatable<GridPoint>
{
    public GridPoint(int x, int y) { X = x; Y = y; }
    public int X { get; }
    public int Y { get; }
    public GridPoint Rotate90() => new(-Y, X);
    public GridPoint Add(GridPoint other) => new(X + other.X, Y + other.Y);
    public int ManhattanDistance(GridPoint other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    public bool Equals(GridPoint other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is GridPoint other && Equals(other);
    public override int GetHashCode() => (X * 397) ^ Y;
    public override string ToString() => $"({X},{Y})";
}
