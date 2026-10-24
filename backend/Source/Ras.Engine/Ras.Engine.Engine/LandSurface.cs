using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;

namespace Ras.Engine.Engine;

public class LandSurface
{
	private readonly Mesh _mesh;

	public double[] CellExcessRate;

	public double[] CellExcessFlow;

	public LandSurface(Mesh mesh)
	{
		_mesh = mesh;
		CellExcessFlow = null;
		CellExcessRate = null;
	}

	public void Prepare()
	{
		CellExcessRate = new double[_mesh.CellCount];
		CellExcessFlow = new double[_mesh.CellCount];
	}

	public void Update(double[] cellPrecipRate, double[] cellEvapotransRate)
	{
		HydraulicMeshComputer metadataCache = _mesh.MetadataCache;
		bool hasCellPrecipRate = cellPrecipRate != null;
		bool hasCellEvapotransRate = cellEvapotransRate != null;
		if (hasCellPrecipRate & hasCellEvapotransRate)
		{
			for (int i = 0; i < _mesh.CellCount; i++)
			{
				CellExcessRate[i] = cellPrecipRate[i] - cellEvapotransRate[i];
				CellExcessFlow[i] = CellExcessRate[i] * metadataCache.CellPolygonAreas[i];
			}
		}
		else if (hasCellPrecipRate)
		{
			for (int j = 0; j < _mesh.CellCount; j++)
			{
				CellExcessRate[j] = cellPrecipRate[j];
				CellExcessFlow[j] = CellExcessRate[j] * metadataCache.CellPolygonAreas[j];
			}
		}
		else if (hasCellEvapotransRate)
		{
			for (int k = 0; k < _mesh.CellCount; k++)
			{
				CellExcessRate[k] = 0.0 - cellEvapotransRate[k];
				CellExcessFlow[k] = CellExcessRate[k] * metadataCache.CellPolygonAreas[k];
			}
		}
	}
}
