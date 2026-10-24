namespace Utility.Matrices;

public sealed record LinearSystemInfo(bool IsSparse, bool IsSymmetric, bool IsPositiveDefinite, int SystemSize);
