using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public interface IAreaGeometry
{
	bool Contains(Polyline pline);

	bool Contains(PointM ptm);

	List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline);
}
