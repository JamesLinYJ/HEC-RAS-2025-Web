using System;
using Utility;

namespace Geospatial.Vectors.Meshes;

public class WaterSurfaceMeshInputs
{
	public Mesh Mesh { get; set; }

	public MeshPropertyTables PropertyTables { get; set; }

	public Memory<float> CellWaterSurfaces { get; set; }

	public Memory<FaceValues> FaceWaterSurfaces { get; set; }

	public Memory<float>[] FacePointWaterSurfaces { get; set; }

	public WaterSurfaceMeshInputs(Mesh mesh, MeshPropertyTables props)
	{
		Mesh = mesh;
		PropertyTables = props;
		if (props == null || props.CellMinimumElevations == null || props.FaceMinimumElevations == null)
		{
			throw new ArgumentNullException("props or some necessary internal buffer.");
		}
		CellWaterSurfaces = new float[mesh.CellCount];
		FaceWaterSurfaces = new FaceValues[mesh.FaceCount];
		FacePointWaterSurfaces = new Memory<float>[mesh.FacePointCount];
		int faceCountSum = 0;
		for (int i = 0; i < mesh.FacePointCount; i++)
		{
			FacePoint facePoint = mesh.FacePoints[i];
			faceCountSum += facePoint.Faces.Count;
		}
		float[] floatValues = new float[faceCountSum];
		int countSum = 0;
		for (int j = 0; j < mesh.FacePointCount; j++)
		{
			int count = mesh.FacePoints[j].Faces.Count;
			FacePointWaterSurfaces[j] = floatValues.AsMemory(countSum, count);
			countSum += count;
		}
	}

	public void Build()
	{
		Parallel.SmartFor(Mesh.FaceCount, (int start, int finish) =>
		{
			Span<FaceValues> faceWaterSurfaces = FaceWaterSurfaces.Span;
			Span<float> cellWaterSurfaces = CellWaterSurfaces.Span;
			HydraulicMeshComputer metadataCache = Mesh.MetadataCache;
			for (int i = start; i <= finish; i++)
			{
				faceWaterSurfaces[i] = metadataCache.FaceComputes.WSEL_Adjusted(i, cellWaterSurfaces, PropertyTables);
			}
		}, 256);
		Parallel.SmartFor(Mesh.FacePointCount, (int start, int finish) =>
		{
			Span<FaceValues> faceWaterSurfacesSpan = FaceWaterSurfaces.Span;
			HydraulicMeshComputer metadataCache = Mesh.MetadataCache;
			for (int i = start; i <= finish; i++)
			{
				metadataCache.NodeComputes.WSEL_Global(i, faceWaterSurfacesSpan, FacePointWaterSurfaces[i].Span);
			}
		}, 256);
	}
}
