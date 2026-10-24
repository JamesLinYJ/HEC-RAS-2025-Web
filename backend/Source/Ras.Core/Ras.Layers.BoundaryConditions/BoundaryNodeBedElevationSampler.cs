using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Ras.Layers.BoundaryConditions;

internal static class BoundaryNodeBedElevationSampler
{
	internal static bool TrySample(Mesh mesh, IReadOnlyList<ProfileFaceRange> faceRanges, IResample<float> terrain, out float[] elevations)
	{
		elevations = null;
		if (mesh == null || faceRanges == null || faceRanges.Count == 0 || terrain == null)
		{
			return false;
		}
		Point[] points = new Point[faceRanges.Count + 1];
		int fpEndIdxProfileFaceRangeTerm = -1;
		for (int i = 0; i < faceRanges.Count; i++)
		{
			ProfileFaceRange profileFaceRange = faceRanges[i];
			if (profileFaceRange.Face < 0 || profileFaceRange.Face >= mesh.FaceCount || profileFaceRange.FPStartIdx < 0 || profileFaceRange.FPStartIdx >= mesh.FacePointCount || profileFaceRange.FPEndIdx < 0 || profileFaceRange.FPEndIdx >= mesh.FacePointCount)
			{
				return false;
			}
			Face face = mesh.Faces[profileFaceRange.Face];
			if (((face.fpA != profileFaceRange.FPStartIdx || face.fpB != profileFaceRange.FPEndIdx) && (face.fpB != profileFaceRange.FPStartIdx || face.fpA != profileFaceRange.FPEndIdx)) || (i > 0 && profileFaceRange.FPStartIdx != fpEndIdxProfileFaceRangeTerm))
			{
				return false;
			}
			if (i == 0)
			{
				points[0] = mesh.FacePoints[profileFaceRange.FPStartIdx].Point;
			}
			points[i + 1] = mesh.FacePoints[profileFaceRange.FPEndIdx].Point;
			fpEndIdxProfileFaceRangeTerm = profileFaceRange.FPEndIdx;
		}
		float[] elevationsArray = terrain.SamplePoints(points);
		float? noDataValue = terrain.NoDataValue;
		for (int j = 0; j < elevationsArray.Length; j++)
		{
			float samplePoint = elevationsArray[j];
			if (!float.IsFinite(samplePoint) || samplePoint == -9999f || (noDataValue.HasValue && samplePoint.Equals(noDataValue.Value)))
			{
				return false;
			}
		}
		elevations = elevationsArray;
		return true;
	}
}
