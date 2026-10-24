using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RasMapperLib.Mapping;

[DebuggerDisplay("XSIS {ID}: {MapPoints.Count} points")]
[ComVisible(false)]
public class MappedXSIS
{
	public int ID;

	public List<XSISMapPoint> MapPoints;

	public MappedXSIS(int ID)
	{
		MapPoints = new List<XSISMapPoint>();
		this.ID = ID;
	}

	public void Add(int index, float z, float m, float streamNX, float streamNY)
	{
		MapPoints.Add(new XSISMapPoint(index, z, m, streamNX, streamNY));
	}

	public void Add(int index, float z, float m)
	{
		MapPoints.Add(new XSISMapPoint(index, z, m, 0f, 0f));
	}
}
