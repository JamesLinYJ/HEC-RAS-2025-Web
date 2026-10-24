using System.Collections.Generic;

namespace Utility.Matrices;

public sealed record SolverPolicy(SolverKind Primary = SolverKind.PcgManaged, IReadOnlyList<SolverKind> FallbackOrder = null, bool ThrowIfUnavailable = false);
