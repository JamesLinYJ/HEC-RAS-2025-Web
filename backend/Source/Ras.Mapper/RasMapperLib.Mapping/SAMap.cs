using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RasMapperLib.Mapping;

[DebuggerDisplay("Storage Area {ID}: {MPs.Count} points")]
[ComVisible(false)]
public class SAMap
{
	public int ID;

	public List<int> MapPoints;

	public SAMap(int ID)
	{
		MapPoints = new List<int>();
		this.ID = ID;
	}
}
