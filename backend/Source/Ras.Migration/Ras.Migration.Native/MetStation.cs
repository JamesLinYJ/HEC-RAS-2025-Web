using System.Diagnostics;

namespace Ras.Migration.Native;

[DebuggerDisplay("{Name}")]
public class MetStation
{
	public string Name;

	public float GaugeHeight;

	public double Latitude;

	public double Longitude;

	public double X;

	public double Y;
}
