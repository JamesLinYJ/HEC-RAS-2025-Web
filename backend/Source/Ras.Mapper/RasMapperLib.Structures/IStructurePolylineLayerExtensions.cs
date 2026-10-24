using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Structures;

[StandardModule]
public static class IStructurePolylineLayerExtensions
{
	[CompilerGenerated]
private sealed class _Closure_0024__0_002D0<_0024CLS0> where _0024CLS0 : FeatureLayer
,IStructurePolylineLayer	{
		public _0024CLS0 lyr;

		public _Closure_0024__0_002D0(_Closure_0024__0_002D0<_0024CLS0> other)
		{
			if (other != null)
			{
				lyr = other.lyr;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(object ob, FeatureChangedEventArgs args)
		{
			if (args.ChangeType != FeatureChangedType.FeatureModified)
			{
				return;
			}
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				NotifyStructurePolylineChanged(lyr, featureIndicesChanged);
			}
		}
	}

	public static void InitializeCommonStructurePolylineColumns<T>(this T lyr) where T : FeatureLayer
,IStructurePolylineLayer	{
		_Closure_0024__0_002D0<T> capturedLocals = default;
		_Closure_0024__0_002D0<T> capturedLocals2 = new _Closure_0024__0_002D0<T>(capturedLocals);
		capturedLocals2.lyr = lyr;
		capturedLocals2.lyr.AddAttributeColumn("US Segment", typeof(SegmentM), null, DataColumnViewState.Invisible);
		capturedLocals2.lyr.AddAttributeColumn("DS Segment", typeof(SegmentM), null, DataColumnViewState.Invisible);
		capturedLocals2.lyr.AddAttributeColumn("US Cells Crossed", typeof(List<MeshFV2D.ProfileCellRange>), null, DataColumnViewState.Invisible);
		capturedLocals2.lyr.AddAttributeColumn("DS Cells Crossed", typeof(List<MeshFV2D.ProfileCellRange>), null, DataColumnViewState.Invisible);
		capturedLocals2.lyr.FeatureChanged += [SpecialName] (object ob, FeatureChangedEventArgs args) =>
		{
			if (args.ChangeType == FeatureChangedType.FeatureModified)
			{
				foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
				{
					NotifyStructurePolylineChanged(capturedLocals2.lyr, featureIndicesChanged);
				}
			}
		};
	}

	private static void NotifyStructurePolylineChanged<T>(this T lyr, int fID) where T : FeatureLayer
,IStructurePolylineLayer	{
		SetUSCellList(lyr, fID, null);
		SetDSCellList(lyr, fID, null);
		lyr.FeatureRow(fID)["US Segment"] = DBNull.Value;
		lyr.FeatureRow(fID)["DS Segment"] = DBNull.Value;
	}

	public static List<MeshFV2D.ProfileCellRange> GetUSCellList<T>(this T lyr, int fID) where T : FeatureLayer
,IStructurePolylineLayer	{
		object objectValue = RuntimeHelpers.GetObjectValue(lyr.FeatureRow(fID)["US Cells Crossed"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as List<MeshFV2D.ProfileCellRange>;
	}

	public static void SetUSCellList<T>(this T lyr, int fID, List<MeshFV2D.ProfileCellRange> profileCellRanges) where T : FeatureLayer
,IStructurePolylineLayer	{
		lyr.FeatureRow(fID)["US Cells Crossed"] = profileCellRanges;
	}

	public static List<MeshFV2D.ProfileCellRange> GetDSCellList<T>(this T lyr, int fID) where T : FeatureLayer
,IStructurePolylineLayer	{
		object objectValue = RuntimeHelpers.GetObjectValue(lyr.FeatureRow(fID)["DS Cells Crossed"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as List<MeshFV2D.ProfileCellRange>;
	}

	public static void SetDSCellList<T>(this T lyr, int fID, List<MeshFV2D.ProfileCellRange> profileCellRanges) where T : FeatureLayer
,IStructurePolylineLayer	{
		lyr.FeatureRow(fID)["DS Cells Crossed"] = profileCellRanges;
	}

	public static SegmentM GetUSSegment<T>(this T lyr, int fid) where T : PolylineFeatureLayer
,IStructurePolylineLayer	{
		return GetSegmentInternal(lyr, "US Segment", fid);
	}

	private static void SetUSSegment<T>(this T lyr, int fID, SegmentM seg) where T : PolylineFeatureLayer
,IStructurePolylineLayer	{
		lyr.FeatureRow(fID)["US Segment"] = seg;
	}

	public static SegmentM GetDSSegment<T>(this T lyr, int fid) where T : PolylineFeatureLayer
,IStructurePolylineLayer	{
		return GetSegmentInternal(lyr, "DS Segment", fid);
	}

	private static void SetDSSegment<T>(this T lyr, int fID, SegmentM seg) where T : PolylineFeatureLayer
,IStructurePolylineLayer	{
		lyr.FeatureRow(fID)["DS Segment"] = seg;
	}

	public static SegmentM GetSegmentInternal<T>(this T lyr, string colName, int fid) where T : PolylineFeatureLayer
,IStructurePolylineLayer	{
		object objectValue = RuntimeHelpers.GetObjectValue(lyr.FeatureRow(fid)[colName]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			if (SetCachedSegments(lyr, fid))
			{
				object featureRow = lyr.FeatureRow(fid)[colName];
				return (featureRow != null) ? ((SegmentM)featureRow) : default(SegmentM);
			}
			return SegmentM.NullSegment();
		}
		return (objectValue != null) ? ((SegmentM)objectValue) : default(SegmentM);
	}

	private static bool SetCachedSegments<T>(this T lyr, int fid) where T : PolylineFeatureLayer
,IStructurePolylineLayer	{
		float width = lyr.GetWidth(fid);
		if (float.IsNaN(width) || width <= 0f)
		{
			return false;
		}
		Polyline polyline = lyr.Polyline(fid);
		if (polyline.Count <= 1)
		{
			return false;
		}
		PointMs leftOffset = null;
		PointMs rightOffset = null;
		polyline.GetOffsetPolygon(width, ref leftOffset, ref rightOffset);
		SegmentM seg = new SegmentM(leftOffset.FirstPointM(), rightOffset.FirstPointM());
		SegmentM seg2 = new SegmentM(leftOffset.LastPointM(), rightOffset.LastPointM());
		SetUSSegment(lyr, fid, seg);
		SetDSSegment(lyr, fid, seg2);
		return true;
	}
}
