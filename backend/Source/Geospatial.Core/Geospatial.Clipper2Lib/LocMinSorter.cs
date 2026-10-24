using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Geospatial.Clipper2Lib;

[StructLayout(LayoutKind.Sequential, Size = 1)]
internal struct LocMinSorter : IComparer<LocalMinima>
{
	public readonly int Compare(LocalMinima locMin1, LocalMinima locMin2)
	{
		return locMin2.vertex.pt.Y.CompareTo(locMin1.vertex.pt.Y);
	}
}
