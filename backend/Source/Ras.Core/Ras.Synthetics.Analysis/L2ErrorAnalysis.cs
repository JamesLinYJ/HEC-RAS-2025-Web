using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics.Analysis;

public static class L2ErrorAnalysis
{
	public static List<(double Time, double L2U)> TrackL2U(Solver solver, Func<Point, double, Vector> analyticalVelocity)
	{
		return BaseAnalysis.TrackScalar(solver, (MeshTimestep mt) => ExtractL2U(solver.Mesh, mt.WaterState, analyticalVelocity, mt.Time), 1E-10);
	}

	public static double ExtractL2U(Mesh mesh, MeshWaterState ws, Func<Point, double, Vector> analyticalVelocity, double t)
	{
		double computed = 0.0;
		double computed2 = 0.0;
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			Point point = mesh.FacePolyline(i).PointAtFraction(0.5).Point;
			mesh.FaceSegment(i);
			Vector other = mesh.MetadataCache.FaceNormal[i];
			double dot = analyticalVelocity(point, t).Dot(other);
			Face face = mesh.Faces[i];
			if (face.cellA >= 0 || face.cellB >= 0)
			{
				double cellPolygonAreaMeshTerm = 0.5 * (mesh.CellPolygonArea(face.cellA) + mesh.CellPolygonArea(face.cellA));
				double dotFaceVelocityTerm = dot - ws.FaceVelocity[i];
				computed += cellPolygonAreaMeshTerm * dotFaceVelocityTerm * dotFaceVelocityTerm;
				computed2 += cellPolygonAreaMeshTerm;
			}
		}
		return Math.Sqrt(computed / computed2);
	}
}
