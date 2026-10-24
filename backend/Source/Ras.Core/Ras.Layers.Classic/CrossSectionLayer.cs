using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.Vectors;
using Ras.Hydraulics;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Cross Sections")]
public class CrossSectionLayer : ClassicGeometryLayerBase<CrossSection>
{
	public static readonly string HDF5GroupName = "/Geometry/Cross Sections";

	private IPolylineCollection _plWrapper;

	protected override string HDF5Group => HDF5GroupName;

	public bool HasAnyIce => this.Any((CrossSection xs) => !xs.IceProperties.IsDefault());

	internal CrossSectionLayer(ClassicGeometry geom)
		: base(geom)
	{
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineFtLayerWrapped<CrossSection>(this, (CrossSection riv) => riv.Polyline, (Polyline pl) => new CrossSection
			{
				Polyline = pl
			});
		}
		return _plWrapper;
	}

	public bool IsReadyForCompute(ProgressReporter errMessages = null)
	{
		if (errMessages == null)
		{
			errMessages = ProgressReporter.None();
		}
		List<LineCollectionIntersection> allIntersections = PolylineCollection.GetAllIntersections(this.Select((CrossSection xs) => xs.Polyline).ToList());
		bool result = false;
		if (allIntersections.Count > 0)
		{
			result = true;
			List<int> distinct = allIntersections.Select((LineCollectionIntersection li) => li.APolylineIndex).Concat(allIntersections.Select((LineCollectionIntersection li) => li.BPolylineIndex)).Distinct()
				.ToList();
			errMessages.ReportMessage(distinct.Count + " Cross-sections with intersection errors: " + string.Join(", ", distinct).InParentheses());
		}
		for (int intervalIndex = 0; intervalIndex < Count; intervalIndex++)
		{
			if (string.IsNullOrEmpty(base[intervalIndex].RiverName))
			{
				result = true;
			}
		}
		return result;
	}

	internal void ClearAllGeospatialData()
	{
		for (int i = 0; i < Count; i++)
		{
			CrossSection crossSection = base[i];
			crossSection.UnsetRiver();
			crossSection.ClearBankProperties();
			crossSection.ClearReachLengths();
			crossSection.ClearLeveeProperties();
		}
	}
}
