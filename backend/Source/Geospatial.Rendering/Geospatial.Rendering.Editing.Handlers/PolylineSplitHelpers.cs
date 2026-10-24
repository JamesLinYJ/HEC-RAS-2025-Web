using System;
using Geospatial.Vectors;
using Utility.Systems;

namespace Geospatial.Rendering.Editing.Handlers;

public static class PolylineSplitHelpers
{
	public static readonly KeyCode SplitKey = KeyCode.S;

	public const string ChannelPrefix = "polyline_split";

	public static (Polyline first, Polyline second) CreateSplitPolylinesAtSegment(Polyline original, int segmentIndex, Point splitPoint)
	{
		Polyline polyline = new Polyline(segmentIndex + 2);
		for (int i = 0; i <= segmentIndex; i++)
		{
			polyline.Add(original[i]);
		}
		polyline.Add(splitPoint);
		Polyline polyline2 = new Polyline(original.Count - segmentIndex - 1 + 1);
		polyline2.Add(splitPoint);
		for (int j = segmentIndex + 1; j < original.Count; j++)
		{
			polyline2.Add(original[j]);
		}
		return (first: polyline, second: polyline2);
	}

	public static (Polyline first, Polyline second) CreateSplitPolylinesAtVertex(Polyline original, int vertexIndex)
	{
		if (vertexIndex <= 0 || vertexIndex >= original.Count - 1)
		{
			throw new ArgumentException("Cannot split at first or last vertex", "vertexIndex");
		}
		Polyline polyline = new Polyline(vertexIndex + 1);
		for (int i = 0; i <= vertexIndex; i++)
		{
			polyline.Add(original[i]);
		}
		Polyline polyline2 = new Polyline(original.Count - vertexIndex);
		for (int j = vertexIndex; j < original.Count; j++)
		{
			polyline2.Add(original[j]);
		}
		return (first: polyline, second: polyline2);
	}
}
