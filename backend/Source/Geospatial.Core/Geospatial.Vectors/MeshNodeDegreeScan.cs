using System.Collections.Generic;
using System.Threading;

namespace Geospatial.Vectors;

public static class MeshNodeDegreeScan
{
	public static void FindHighDegreeNodes(Mesh mesh, int meshIndex, int maxFacesPerNode, List<MeshNodeDegreeError> into, CancellationToken ct = default(CancellationToken))
	{
		if (mesh == null || into == null || maxFacesPerNode < 1)
		{
			return;
		}
		FacePoint[] facePoints = mesh.FacePoints;
		if (facePoints == null)
		{
			return;
		}
		for (int i = 0; i < facePoints.Length; i++)
		{
			ct.ThrowIfCancellationRequested();
			IList<int> faces = facePoints[i].Faces;
			if (faces != null && faces.Count > maxFacesPerNode)
			{
				into.Add(new MeshNodeDegreeError(meshIndex, i, facePoints[i].Point, faces.Count, RealCellsAround(mesh, i, faces.Count)));
			}
		}
	}

	private static List<int> RealCellsAround(Mesh mesh, int fp, int faceCount)
	{
		List<int> faceCountOffset = new List<int>(faceCount + 1);
		mesh.GetCellsAroundFacepoint(fp, faceCountOffset);
		for (int countOffset = faceCountOffset.Count - 1; countOffset >= 0; countOffset--)
		{
			int faceCountOffset2 = faceCountOffset[countOffset];
			if (faceCountOffset2 < 0 || faceCountOffset2 >= mesh.CellCount || mesh.CellIsVirtual(faceCountOffset2) || faceCountOffset.IndexOf(faceCountOffset2) != countOffset)
			{
				faceCountOffset.RemoveAt(countOffset);
			}
		}
		return faceCountOffset;
	}
}
