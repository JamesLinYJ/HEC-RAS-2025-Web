using System;
using System.Collections.Generic;

namespace Utility.Filtering;

public sealed class SelectorResolution
{
	public ISelectorCollection Collection { get; set; }

	public List<int> MatchedIndices { get; set; } = new List<int>();

	public ISelectorNode ScalarNode { get; set; }

	public bool IsScalar => ScalarNode != null;

	public string TerminalField { get; set; }

	public string TerminalFieldNormalized { get; set; }

	public Type TerminalFieldType { get; set; }

	public bool HasAssignment { get; set; }

	public SelectorOperation Operation { get; set; }

	public bool HasOperation => Operation != null;

	public object AssignmentValue { get; set; }

	public FilterLiteral AssignmentLiteral { get; set; }
}
