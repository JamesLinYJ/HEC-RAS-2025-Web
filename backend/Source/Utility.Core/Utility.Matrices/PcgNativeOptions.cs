namespace Utility.Matrices;

public sealed record PcgNativeOptions(int MaxIterations = 2000, double Tolerance = 1E-08, int Verbosity = 0);
