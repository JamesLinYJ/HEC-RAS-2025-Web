using System.Collections.Generic;

namespace Ras.Process.Helpers;

internal sealed class ModifyChange
{
	public string Selector { get; set; }

	public string Target { get; set; }

	public string Field { get; set; }

	public string Operation { get; set; }

	public string Value { get; set; }

	public int? Matched { get; set; }

	public int? Total { get; set; }

	public int Changed { get; set; }

	public List<string> Skipped { get; set; } = new List<string>();
}
