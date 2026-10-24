using System.Diagnostics;

namespace Ras.Mapping.ArrivalTime;

[DebuggerDisplay("Extrema: {LocalExtrema}, value={Value}")]
public struct LocalExtremaValue(LocalExtrema localExtrema, float floatValue)
{
	public LocalExtrema LocalExtrema = localExtrema;

	public float Value = floatValue;
}
