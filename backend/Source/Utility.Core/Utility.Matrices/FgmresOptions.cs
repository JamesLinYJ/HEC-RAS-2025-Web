namespace Utility.Matrices;

public sealed record FgmresOptions(int MaxIterations = 1, double Tolerance = 0.0, int Verbosity = 0, int MatrixType = 11);
