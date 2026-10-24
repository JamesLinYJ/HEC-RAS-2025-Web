namespace Utility.Matrices;

public sealed record SolverRequest(SolverPolicy Policy, LinearSystemInfo SystemInfo, SolverOptionsBundle Options);
