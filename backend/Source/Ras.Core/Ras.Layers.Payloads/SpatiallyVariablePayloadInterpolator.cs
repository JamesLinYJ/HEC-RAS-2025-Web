using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Layers.Payloads;

public static class SpatiallyVariablePayloadInterpolator
{
	public static FaceStationSpan[] FaceStationsAlongLine(Mesh mesh, Polyline bcLine, List<ProfileFaceRange> perimFaces)
	{
		if (perimFaces.Count == 0)
		{
			return Array.Empty<FaceStationSpan>();
		}
		FaceStationSpan[] faceStationSpans = new FaceStationSpan[perimFaces.Count];
		for (int i = 0; i < perimFaces.Count; i++)
		{
			int face = perimFaces[i].Face;
			Face face2 = mesh.Faces[face];
			double stationOf = StationOf(bcLine, mesh.FacePoints[face2.fpA].Point);
			double stationOf2 = StationOf(bcLine, mesh.FacePoints[face2.fpB].Point);
			double mid = StationOf(bcLine, mesh.FaceMidPoint(face));
			faceStationSpans[i] = ((stationOf <= stationOf2) ? new FaceStationSpan(stationOf, mid, stationOf2) : new FaceStationSpan(stationOf2, mid, stationOf));
		}
		return faceStationSpans;
	}

	private static double StationOf(Polyline bcLine, Point pt)
	{
		PointOnPolyline ptOnPoly = bcLine.NearestPointOnPolyline(pt);
		return bcLine.DistanceAlong(ptOnPoly);
	}

	public static double[] InterpolateBlockToFaces(double[] stations, double[] payloadValues, FaceStationSpan[] faceSpans, int stationOffset = 0)
	{
		if (faceSpans.Length == 0)
		{
			return Array.Empty<double>();
		}
		if (stations.Length == 0)
		{
			throw new ArgumentException("Spatially variable payload has no stations.", "stations");
		}
		if (payloadValues.Length < stationOffset + stations.Length)
		{
			throw new ArgumentException($"Spatially variable payload row has {payloadValues.Length} ordinates; block at offset {stationOffset} needs {stationOffset + stations.Length}.", "payloadValues");
		}
		double[] doubleValues = new double[faceSpans.Length];
		for (int i = 0; i < faceSpans.Length; i++)
		{
			doubleValues[i] = ValueAtStation(stations, payloadValues, faceSpans[i].Mid, stationOffset);
		}
		return doubleValues;
	}

	private static double ValueAtStation(double[] stations, double[] payloadValues, double station, int stationOffset)
	{
		if (station <= stations[0])
		{
			return payloadValues[stationOffset];
		}
		if (station >= stations[^1])
		{
			return payloadValues[stationOffset + stations.Length - 1];
		}
		int binarySearch = Array.BinarySearch(stations, station);
		if (binarySearch >= 0)
		{
			return payloadValues[stationOffset + binarySearch];
		}
		binarySearch = ~binarySearch;
		int binarySearchOffset = binarySearch - 1;
		double station2 = stations[binarySearchOffset];
		double station3 = stations[binarySearch];
		if (station3 <= station2)
		{
			return payloadValues[stationOffset + binarySearchOffset];
		}
		double submergenceRatio = (station - station2) / (station3 - station2);
		double payloadValue = payloadValues[stationOffset + binarySearchOffset];
		double payloadValue2 = payloadValues[stationOffset + binarySearch];
		return payloadValue + (payloadValue2 - payloadValue) * submergenceRatio;
	}

	public static double[,] BuildFaceValues(SpatiallyVariableTimeSeriesPayload payload, double[] timesteps, FaceStationSpan[] faceSpans)
	{
		double[] stations = payload.Stations.ToArray();
		double[,] faceValues = new double[timesteps.Length, faceSpans.Length];
		for (int i = 0; i < timesteps.Length; i++)
		{
			double[] interpolateBlockToFaces = InterpolateBlockToFaces(stations, payload.Values[i].ToArray(), faceSpans);
			for (int j = 0; j < faceSpans.Length; j++)
			{
				faceValues[i, j] = interpolateBlockToFaces[j];
			}
		}
		return faceValues;
	}
}
