using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class PolylineFeatureLayer : FeatureLayer, IProvidePolyline
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__26_002D0
	{
		public int ftIdx;

		public int segIdx;

		public Func<double, double, bool> closeTo;

		public _Closure_0024__26_002D1 outerClosure;

		public _Closure_0024__26_002D0(_Closure_0024__26_002D0 other)
		{
			if (other != null)
			{
				ftIdx = other.ftIdx;
				segIdx = other.segIdx;
				closeTo = other.closeTo;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(PolylineIntersection @int)
		{
			if (@int.APolylineIndex == ftIdx && @int.ASegmentIndex == checked(segIdx + 1))
			{
				return closeTo(@int.AFraction, 0.0);
			}
			return false;
		}

		[SpecialName]
		internal bool _Lambda_0024__2(PolylineIntersection @int)
		{
			if (@int.APolylineIndex == ftIdx && @int.ASegmentIndex == checked(segIdx - 1))
			{
				return closeTo(@int.AFraction, 1.0);
			}
			return false;
		}

		[SpecialName]
		internal bool _Lambda_0024__3(PolylineIntersection @int)
		{
			if (@int.BSegmentIndex == checked(outerClosure.plSegIdx + 1))
			{
				return closeTo(@int.BFraction, 0.0);
			}
			return false;
		}

		[SpecialName]
		internal bool _Lambda_0024__4(PolylineIntersection @int)
		{
			if (@int.BSegmentIndex == checked(outerClosure.plSegIdx - 1))
			{
				return closeTo(@int.BFraction, 1.0);
			}
			return false;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__26_002D1
	{
		public int plSegIdx;

		public _Closure_0024__26_002D1(_Closure_0024__26_002D1 other)
		{
			if (other != null)
			{
				plSegIdx = other.plSegIdx;
			}
		}
	}

	private const string sCount = "Count";

	private const string sLength = "Length";

	private const string sParts = "Parts";

	private SpatialIndex<Tuple<int, int>> _spatialIndexPolylines;

	private string _simpleFeatureTypeName;


	public override string SimpleFeatureTypeName
	{
		get
		{
			if (string.IsNullOrEmpty(_simpleFeatureTypeName))
			{
				if (Operators.CompareString(Name, "Profile Lines", TextCompare: false) == 0)
				{
					return "Profile Line";
				}
				return "Line";
			}
			return _simpleFeatureTypeName;
		}
		set
		{
			_simpleFeatureTypeName = value;
		}
	}

	public override bool CanRemove { get; }

	public override bool CanRename { get; }

	public Polyline this[int index] => Polyline(index);

	public PolylineFeatureLayer()
		: base("Polyline Layer")
	{
		_simpleFeatureTypeName = "";
		CanRemove = true;
		CanRename = true;
	}

	public PolylineFeatureLayer(string name)
		: base(name)
	{
		_simpleFeatureTypeName = "";
		CanRemove = true;
		CanRename = true;
	}

	public PolylineFeatureLayer(string name, string filename, bool canRemove = true, bool canRename = true)
		: base(name, filename)
	{
		_simpleFeatureTypeName = "";
		CanRemove = true;
		CanRename = true;
		CanRemove = canRemove;
		CanRename = canRename;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Count", typeof(int), _featureColumnGroup, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Length", typeof(double), _featureColumnGroup, DataColumnViewState.GrayedOut);
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.AddRange(RasMapperLib.Polyline.GetAvailablePlotOptions(SharedData.DebugMode));
		return staticPlotOptions;
	}

	public IEnumerable<Polyline> Polylines()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return Polyline(i);
			}
		}
	}

	public Polyline Polyline(int fid)
	{
		if (!(Feature(fid) is Polyline result))
		{
			return (Polyline)ResolveNullFeature();
		}
		return result;
	}

	Polyline IProvidePolyline.GetPolyline(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Polyline
		return this.Polyline(fid);
	}

	public override Feature ResolveNullFeature()
	{
		return RasMapperLib.Polyline.NullPolyline();
	}

	public SpatialIndex<Tuple<int, int>> GetSegmentSpatialIndex()
	{
		return EnsurePolylineSpatialIndex();
	}

	public List<PolylineIntersection> SpatialIntersect(Polyline polyline)
	{
		return SpatialIntersect(-1, polyline);
	}

	public List<PolylineIntersection> SpatialIntersect(int polylineIndex, Polyline polyline)
	{
		SpatialIndex<Tuple<int, int>> spatialIndex = EnsurePolylineSpatialIndex();
		List<PolylineIntersection> polylineIntersections = new List<PolylineIntersection>();
		if (spatialIndex == null)
		{
			return polylineIntersections;
		}
		Polyline polyline2 = null;
		int capturedLocals = -1;
		checked
		{
			int count = polyline.SegmentCount() - 1;
			_Closure_0024__26_002D1 capturedLocals2 = default;
			_Closure_0024__26_002D0 capturedLocals3 = default;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2 = new _Closure_0024__26_002D1(capturedLocals2);
				capturedLocals2.plSegIdx = i;
				SegmentM segmentM = polyline.Segment(i);
				List<Tuple<int, int>> elementsWithDupes = spatialIndex.GetElementsWithDupes(segmentM);
				HashSet<Tuple<int, int>> valueTupleSet = new HashSet<Tuple<int, int>>();
				int count2 = elementsWithDupes.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					capturedLocals3 = new _Closure_0024__26_002D0(capturedLocals3);
					capturedLocals3.outerClosure = capturedLocals2;
					Tuple<int, int> tuple = elementsWithDupes[j];
					capturedLocals3.ftIdx = tuple.Item1;
					capturedLocals3.segIdx = tuple.Item2;
					if (capturedLocals3.ftIdx != capturedLocals)
					{
						polyline2 = Polyline(capturedLocals3.ftIdx);
						capturedLocals = capturedLocals3.ftIdx;
					}
					if (!polyline2.Segment(capturedLocals3.segIdx).Intersects(segmentM, ref x, ref y, ref t, ref t2) || !valueTupleSet.Add(tuple))
					{
						continue;
					}
					capturedLocals3.closeTo = [SpecialName] (double a, double b) => Math.Abs(a - b) < 9.999999974752427E-07;
					if (capturedLocals3.closeTo(t, 1.0))
					{
						if (polylineIntersections.Where(capturedLocals3._Lambda_0024__1).Count() > 0)
						{
							continue;
						}
					}
					else if (capturedLocals3.closeTo(t, 0.0) && polylineIntersections.Where(capturedLocals3._Lambda_0024__2).Count() > 0)
					{
						continue;
					}
					if (capturedLocals3.closeTo(t2, 1.0))
					{
						if (polylineIntersections.Where(capturedLocals3._Lambda_0024__3).Count() > 0)
						{
							continue;
						}
					}
					else if (capturedLocals3.closeTo(t2, 0.0) && polylineIntersections.Where(capturedLocals3._Lambda_0024__4).Count() > 0)
					{
						continue;
					}
					polylineIntersections.Add(new PolylineIntersection(new PointM(x, y), capturedLocals3.ftIdx, capturedLocals3.segIdx, t, polylineIndex, i, t2));
				}
			}
			return (from @int in polylineIntersections
				orderby @int.BSegmentIndex, @int.BFraction
				select @int).ToList();
		}
	}

	private SpatialIndex<Tuple<int, int>> EnsurePolylineSpatialIndex()
	{
		if (_spatialIndexPolylines == null)
		{
			_spatialIndexPolylines = SpatialIndex.GetSegmentSpatialIndex(FeaturesOfType<Polyline>().ToList());
		}
		return _spatialIndexPolylines;
	}

	public Polyline CopyToMultiPartPolyline()
	{
		if (FeatureCount() == 0)
		{
			return null;
		}
		PointMs pointMs = new PointMs();
		List<int> intList = new List<int>();
		foreach (Polyline polyline in Polylines())
		{
			intList.Add(pointMs.Count);
			polyline.CopyTo(pointMs);
		}
		return new Polyline(pointMs, intList);
	}

	protected override void OnFeatureTableCleared()
	{
		base.OnFeatureTableCleared();
		_spatialIndexPolylines = null;
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		_spatialIndexPolylines = null;
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(Polyline) }.ToList();
	}

	protected override void ResetFeatureAttributes(int fid)
	{
		FeatureRow(fid)["Count"] = Polyline(fid).Count;
		FeatureRow(fid)["Length"] = Polyline(fid).Length;
	}

	public void SetToNamedEditor()
	{
	}

	public override int SpatialQueryFID(PointM pt, double dist)
	{
		if (FilteredFeatureCount() == 0)
		{
			return -1;
		}
		List<Tuple<int, int>> elements = EnsurePolylineSpatialIndex().GetElements(pt, dist);
		if (elements.Count > 0)
		{
			double maxValue = double.MaxValue;
			int result = -1;
			HashSet<int> intSet = new HashSet<int>();
			elements.OrderByDescending([SpecialName] (Tuple<int, int> x) => x.Item1);
			foreach (Tuple<int, int> element in elements)
			{
				if ((FeatureRowFilter == null || FeatureRowFilter(FeatureRow(element.Item1))) && !intSet.Contains(element.Item1))
				{
					double distanceToPoint = Polyline(element.Item1).DistanceToPoint(pt);
					if (Polyline(element.Item1).Intersects(pt, dist) && FeatureIsHitTestable(element.Item1) && distanceToPoint < maxValue)
					{
						result = element.Item1;
						maxValue = distanceToPoint;
					}
					intSet.Add(element.Item1);
				}
			}
			return result;
		}
		return -1;
	}

	public override int SpatialQueryFID(Extent ext)
	{
		if (FeatureCount() == 0)
		{
			return -1;
		}
		List<Tuple<int, int>> elements = EnsurePolylineSpatialIndex().GetElements(ext);
		elements.OrderByDescending([SpecialName] (Tuple<int, int> x) => x.Item1);
		if (elements.Count > 0)
		{
			int ordinal = -1;
			foreach (Tuple<int, int> element in elements)
			{
				if (FeatureRowFilter == null || FeatureRowFilter(FeatureRow(element.Item1)))
				{
					if (Polyline(element.Item1).Intersects(ext) && FeatureIsHitTestable(element.Item1))
					{
						return element.Item1;
					}
					ordinal = checked(ordinal + 1);
				}
			}
			return -1;
		}
		return -1;
	}

	public override List<int> SpatialQueryFIDs(Extent ext)
	{
		if (FeatureCount() == 0)
		{
			return new List<int>();
		}
		return (from x in (from x in EnsurePolylineSpatialIndex().GetElements(ext)
				select x.Item1).Distinct()
			where (FeatureRowFilter != null) ? (FeatureRowFilter(FeatureRow(x)) && Polyline(x).Intersects(ext) && FeatureIsHitTestable(x)) : (Polyline(x).Intersects(ext) && FeatureIsHitTestable(x))
			select x).ToList();
	}

	public override List<int> SpatialQueryFIDs(PointM pt, double dist)
	{
		if (FeatureCount() == 0)
		{
			return new List<int>();
		}
		return (from x in EnsurePolylineSpatialIndex().GetElements(pt, dist)
			select x.Item1).Distinct().Where([SpecialName] (int x) =>
		{
			Polyline(x).DistanceToPoint(pt);
			return (FeatureRowFilter != null) ? (Polyline(x).Intersects(pt, dist) && FeatureRowFilter(FeatureRow(x)) && FeatureIsHitTestable(x)) : (Polyline(x).Intersects(pt, dist) && FeatureIsHitTestable(x));
		}).ToList();
	}
}
