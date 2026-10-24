using System.Diagnostics;

namespace RasMapperLib.ArrivalTime;

[DebuggerDisplay("Extrema: {LocalExtrema}, value={Value}")]
public struct LocalExtremaValue
{
	public LocalExtrema LocalExtrema;

	public float Value;

	public LocalExtremaValue(LocalExtrema localExtrema, float floatValue)
	{
		this = default;
		LocalExtrema = localExtrema;
		Value = floatValue;
	}
}
