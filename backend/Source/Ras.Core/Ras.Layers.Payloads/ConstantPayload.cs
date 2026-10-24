using System.Diagnostics;
using Utility.Hashing;

namespace Ras.Layers.Payloads;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class ConstantPayload : PayloadBase
{
	[HashContributor]
	public double Value { get; set; }

	public ConstantPayload(string variable, double doubleValue)
		: base(variable)
	{
		Value = doubleValue;
	}

	private string GetDebuggerDisplay()
	{
		return (Name ?? "(No Name)") + " " + Variable + ": " + Value;
	}
}
