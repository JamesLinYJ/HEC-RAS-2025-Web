using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Structures;

[StandardModule]
public static class StructureElementLayerExtensions
{
	[CompilerGenerated]
private sealed class _Closure_0024__3_002D0<_0024CLS0> where _0024CLS0 : FeatureLayer
,IStructureElementLayer	{
		public _0024CLS0 lyr;

		public _Closure_0024__3_002D0(_Closure_0024__3_002D0<_0024CLS0> other)
		{
			if (other != null)
			{
				lyr = other.lyr;
			}
		}

		[SpecialName]
		internal string _Lambda_0024__0(int f)
		{
			return lyr.GetFeatureName(f);
		}
	}

	public const string sRRRSGroup = "River, Reach, River Station";

	public const string sConnectionGroup = "Connection Information";

	public static List<int> FindElementsWithinStructure<T>(this T lyr, int structIdx) where T : FeatureLayer
,IStructureElementLayer	{
		List<int> elementsWithinStructure = new List<int>();
		checked
		{
			int count = lyr.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (lyr.TryGetStructureIndex(i) == structIdx)
				{
					elementsWithinStructure.Add(i);
				}
			}
			return elementsWithinStructure;
		}
	}

	public static string GetUniqueNameWithinStructure<T>(this T lyr, int structFId) where T : FeatureLayer
,IStructureElementLayer	{
		_Closure_0024__3_002D0<T> capturedLocals = default;
		_Closure_0024__3_002D0<T> capturedLocals2 = new _Closure_0024__3_002D0<T>(capturedLocals);
		capturedLocals2.lyr = lyr;
		string simpleFeatureTypeNameTerm = capturedLocals2.lyr.SimpleFeatureTypeName + " #";
		if (structFId == -1)
		{
			return capturedLocals2.lyr.GetUniqueFeatureName(simpleFeatureTypeNameTerm);
		}
		List<int> elementsWithinStructure = FindElementsWithinStructure(capturedLocals2.lyr, structFId);
		HashSet<string> toHashSet = elementsWithinStructure.Select([SpecialName] (int f) => capturedLocals2.lyr.GetFeatureName(f)).ToHashSet();
		int count = elementsWithinStructure.Count;
		checked
		{
			for (int unity = 1; unity <= count; unity++)
			{
				string textValue = simpleFeatureTypeNameTerm + unity;
				if (!toHashSet.Contains(textValue))
				{
					return textValue;
				}
			}
			return simpleFeatureTypeNameTerm + (elementsWithinStructure.Count + 1);
		}
	}

	public static Polygon RecomputeGroupPolygon<T, K>(this T lyr, K barrelLayer, int groupFID) where T : FeatureLayer, IGroupLayer where K : PolylineFeatureLayer, IOpeningLayer
	{
		if (barrelLayer == null)
		{
			return Polygon.NullPolygon();
		}
		float width = lyr.GetWidth(groupFID);
		if (float.IsNaN(width) || width <= 0f)
		{
			return Polygon.NullPolygon();
		}
		List<Polygon> polygons = new List<Polygon>();
		foreach (int elementsWithinGroup in IOpeningLayerExtensions.FindElementsWithinGroup(barrelLayer, groupFID))
		{
			PointMs leftOffset = new PointMs();
			PointMs rightOffset = new PointMs();
			Polygon offsetPolygon = barrelLayer.Polyline(elementsWithinGroup).GetOffsetPolygon(width, ref leftOffset, ref rightOffset);
			polygons.Add(offsetPolygon);
		}
		return Polygon.MergeToMultipartPolygon(polygons);
	}
}
