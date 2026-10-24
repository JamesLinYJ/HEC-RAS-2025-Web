using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class PostFaceFlip(SlidingMesh mesh) : MeshPostprocess(mesh)
{
	public int degreeThreshold = 14;

	public bool useFaceLength;

	public bool useDelaunay;

	public bool TestShouldFlip(int fIdx)
	{
		int a = mesh.FaceCells[fIdx].A;
		if (a < 0)
		{
			return false;
		}
		int b = mesh.FaceCells[fIdx].B;
		if (b < 0)
		{
			return false;
		}
		if (mesh.CellFaces[a].Count != 3)
		{
			return false;
		}
		if (mesh.CellFaces[b].Count != 3)
		{
			return false;
		}
		HalfEdge he = new HalfEdge(fIdx, 0);
		int faceNode = mesh.FaceNodes[fIdx][0];
		int faceNode2 = mesh.FaceNodes[fIdx][1];
		HalfEdge he2 = mesh.HalfEdgeNextByCell(he);
		HalfEdge he3 = mesh.HalfEdgeNextByCell(mesh.HalfEdgeFlip(he));
		int halfEdgeNodeForward = mesh.HalfEdgeNodeForward(he2);
		int halfEdgeNodeForward2 = mesh.HalfEdgeNodeForward(he3);
		if (degreeThreshold > 0)
		{
			int nodeDegree = mesh.NodeDegree(faceNode);
			int nodeDegree2 = mesh.NodeDegree(faceNode2);
			int nodeDegreeNodeDegree2Term = nodeDegree * nodeDegree + nodeDegree2 * nodeDegree2;
			int nodeDegreeMeshTerm = mesh.NodeDegree(halfEdgeNodeForward) + 1;
			int nodeDegreeMeshTerm2 = mesh.NodeDegree(halfEdgeNodeForward2) + 1;
			int ordinal = nodeDegreeMeshTerm * nodeDegreeMeshTerm + nodeDegreeMeshTerm2 * nodeDegreeMeshTerm2;
			double degreeThresholdTerm = (double)(degreeThreshold * degreeThreshold) / 2.0;
			if ((double)nodeDegreeNodeDegree2Term >= degreeThresholdTerm)
			{
				return ordinal < nodeDegreeNodeDegree2Term;
			}
			return false;
		}
		if (useDelaunay)
		{
			return NativePredicates.InCircle(mesh.NodePoints[halfEdgeNodeForward], mesh.NodePoints[faceNode], mesh.NodePoints[faceNode2], mesh.NodePoints[halfEdgeNodeForward2]) > 0.0;
		}
		if (useFaceLength)
		{
			double distanceSquaredTo = mesh.NodePoints[halfEdgeNodeForward].DistanceSquaredTo(mesh.NodePoints[halfEdgeNodeForward2]);
			double distanceSquaredTo2 = mesh.NodePoints[faceNode].DistanceSquaredTo(mesh.NodePoints[faceNode2]);
			return distanceSquaredTo < distanceSquaredTo2;
		}
		return false;
	}

	public override void Apply(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		int ordinal = 0;
		int count = 5;
		double scaledCount = 1.0 / (double)count;
		for (int i = 0; i < count; i++)
		{
			int ordinal2 = 0;
			ordinal = 0;
			foreach (int faceIterator in mesh.FaceIterator())
			{
				if (TestShouldFlip(faceIterator))
				{
					ordinal2++;
					if (mesh.FlipFace(faceIterator))
					{
						ordinal++;
					}
				}
				if (reporter.CancelRequested)
				{
					return;
				}
			}
			reporter.ReportProgressFraction((float)(((double)i + 1.0) * scaledCount));
			if (ordinal == 0)
			{
				break;
			}
		}
	}
}
