using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public static class EditFunctions
{
	public static void RenderPointMoveOperation(IVectorManager vectorManager, ref VectorID segmentsVectorID, ref VectorID pointsVectorID, IPointCollection pl, Vector offsetDelta, List<int> pointIndices, bool openEnded = true)
	{
		pointIndices.Sort();
		List<Segment> segments = new List<Segment>();
		Segment nullSegment = Segment.NullSegment;
		for (int i = 0; i < pointIndices.Count; i++)
		{
			int pointIndex = pointIndices[i];
			int pointIndex2 = ((i == 0) ? (-1) : pointIndices[i - 1]);
			if (pointIndex == 0)
			{
				if (!openEnded)
				{
					Segment segment5 = default;
					IPointCollection pointCollection = pl;
					segment5.A = pointCollection[pointCollection.Count - 1];
					segment5.B = pl[0].Translate(offsetDelta);
					segments.Add(segment5);
				}
				Segment segment = new Segment
				{
					A = pl[0].Translate(offsetDelta),
					B = pl[1]
				};
				segments.Add(segment);
				nullSegment = segment;
			}
			else if (pointIndex == pl.Count - 1)
			{
				if (pointIndex2 == pointIndex - 1)
				{
					IPointCollection pointCollection2 = pl;
					nullSegment.B = pointCollection2[pointCollection2.Count - 1].Translate(offsetDelta);
					segments[segments.Count - 1] = nullSegment;
				}
				else
				{
					Segment segment2 = default;
					IPointCollection pointCollection3 = pl;
					segment2.A = pointCollection3[pointCollection3.Count - 2];
					IPointCollection pointCollection4 = pl;
					segment2.B = pointCollection4[pointCollection4.Count - 1].Translate(offsetDelta);
					segments.Add(segment2);
					nullSegment = segment2;
				}
				if (!openEnded)
				{
					if (pointIndices[0] == 0)
					{
						Segment segment6 = segments[0];
						IPointCollection pointCollection5 = pl;
						segment6.A = pointCollection5[pointCollection5.Count - 1].Translate(offsetDelta);
						segments[0] = segment6;
					}
					else
					{
						Segment segment3 = new Segment
						{
							A = pl[pointIndex].Translate(offsetDelta),
							B = pl[0]
						};
						segments.Add(segment3);
						nullSegment = segment3;
					}
				}
			}
			else
			{
				if (pointIndex2 == pointIndex - 1)
				{
					nullSegment.B = nullSegment.B.Translate(offsetDelta);
					segments[segments.Count - 1] = nullSegment;
				}
				else
				{
					segments.Add(new Segment
					{
						A = pl[pointIndex - 1],
						B = pl[pointIndex].Translate(offsetDelta)
					});
				}
				Segment segment4 = new Segment
				{
					A = pl[pointIndex].Translate(offsetDelta),
					B = pl[pointIndex + 1]
				};
				segments.Add(segment4);
				nullSegment = segment4;
			}
		}
		vectorManager.AddOrUpdateVector(ref segmentsVectorID, segments, DefaultEditSymbology.EditLineSymbology);
		vectorManager.AddOrUpdateVector(ref pointsVectorID, pointIndices.Select((int idx) => pl[idx].Translate(offsetDelta)).ToList(), DefaultEditSymbology.EditPointSymbology);
	}

	public static void RenderPointWithAttachedSegmentsMoveOperation(IVectorManager vectorManager, ref VectorID segmentsVectorID, ref VectorID pointsVectorID, IReadOnlyList<Segment> segments, IEnumerable<Point> pts, Vector offsetDelta, VectorSymbology segmentSymbology, VectorSymbology pointSymbology)
	{
		vectorManager.AddOrUpdateVector(ref segmentsVectorID, segments.Select((Segment seg) => new Segment(seg.A.Translate(offsetDelta), seg.B)).ToList(), segmentSymbology);
		vectorManager.AddOrUpdateVector(ref pointsVectorID, pts.Select((Point pt) => pt.Translate(offsetDelta)).ToList(), pointSymbology);
		vectorManager.SetVectorZIndex(pointsVectorID, 10);
	}

	public static void RenderPointAttachOperation(IVectorManager vectorManager, ref VectorID pointVectorID, ref VectorID segmentForegroundVectorID, ref VectorID segmentBackgroundVectorID, Point endPoint, Point mousePt)
	{
		vectorManager.AddOrUpdateVector(ref pointVectorID, mousePt, DefaultEditSymbology.AppendPointSymbology);
		vectorManager.AddOrUpdateVector(ref segmentForegroundVectorID, new Segment(endPoint, mousePt), DefaultEditSymbology.AppendSegmentSymbology);
		vectorManager.AddOrUpdateVector(ref segmentBackgroundVectorID, new Segment(endPoint, mousePt), DefaultEditSymbology.AppendSegmentBackgroundSymbology);
		vectorManager.SetVectorZIndex(segmentForegroundVectorID, 2);
		vectorManager.SetVectorZIndex(pointVectorID, 3);
	}

	public static void RenderSplitPreview(IVectorManager vectorManager, OverlayChannels overlay, SplitLineResult splitResult, Func<int, Polyline> getLine, SplitPreviewSymbology symbology, string channelPrefix)
	{
		RenderSplitsPreview(vectorManager, overlay, new CompactList<SplitLineResult>(splitResult), getLine, splitResult.Point.Point, symbology, channelPrefix);
	}

	public static void RenderSplitsPreview(IVectorManager vectorManager, OverlayChannels overlay, IList<SplitLineResult> splitResults, Func<int, Polyline> getLine, Point splitPoint, SplitPreviewSymbology symbology, string channelPrefix)
	{
		if (splitResults.Count != 0)
		{
			CompactList<Polyline> compactList = default;
			CompactList<Polyline> compactList2 = default;
			for (int i = 0; i < splitResults.Count; i++)
			{
				SplitLineResult splitLineResult = splitResults[i];
				Polyline polyline = getLine(splitLineResult.FID);
				compactList.Add(polyline.SubsectionBefore(splitLineResult.Point));
				compactList2.Add(polyline.SubsectionAfter(splitLineResult.Point));
			}
			string channel = channelPrefix + "_first_bg";
			string channel2 = channelPrefix + "_first_fg";
			string channel3 = channelPrefix + "_second_bg";
			string channel4 = channelPrefix + "_second_fg";
			string channel5 = channelPrefix + "_point";
			overlay.TryGetVector(channel, out var id);
			vectorManager.AddOrUpdateVector(ref id, compactList, symbology.FirstHalfBackground);
			vectorManager.SetVectorZIndex(id, 7);
			overlay.RegisterVector(channel, id);
			overlay.TryGetVector(channel2, out var id2);
			vectorManager.AddOrUpdateVector(ref id2, compactList, symbology.FirstHalfForeground);
			vectorManager.SetVectorZIndex(id2, 8);
			overlay.RegisterVector(channel2, id2);
			overlay.TryGetVector(channel3, out var id3);
			vectorManager.AddOrUpdateVector(ref id3, compactList2, symbology.SecondHalfBackground);
			vectorManager.SetVectorZIndex(id3, 7);
			overlay.RegisterVector(channel3, id3);
			overlay.TryGetVector(channel4, out var id4);
			vectorManager.AddOrUpdateVector(ref id4, compactList2, symbology.SecondHalfForeground);
			vectorManager.SetVectorZIndex(id4, 8);
			overlay.RegisterVector(channel4, id4);
			overlay.TryGetVector(channel5, out var id5);
			vectorManager.AddOrUpdateVector(ref id5, splitPoint, symbology.SplitPoint);
			vectorManager.SetVectorZIndex(id5, 9);
			overlay.RegisterVector(channel5, id5);
		}
	}
}
