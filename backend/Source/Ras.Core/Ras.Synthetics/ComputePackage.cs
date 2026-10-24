using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;

namespace Ras.Synthetics;

public class ComputePackage
{
	public Mesh Mesh { get; set; }

	public MeshPropertyTables PropertyTables { get; set; }

	public IResample<float> Elevations { get; set; }

	public IResample<float> NValues { get; set; }

	public Solver Solver { get; set; }

	public ComputeDriver Driver { get; set; }

	public ComputePackage(Solver s, Mesh m, MeshPropertyTables props, IResample<float> elevs, IResample<float> nvals)
	{
		Solver = s;
		Driver = null;
		Mesh = m;
		PropertyTables = props;
		Elevations = elevs;
		NValues = nvals;
	}

	public ComputePackage(ComputeDriver d, Mesh m, MeshPropertyTables props, IResample<float> elevs, IResample<float> nvals)
	{
		Solver = null;
		Driver = d;
		Mesh = m;
		PropertyTables = props;
		Elevations = elevs;
		NValues = nvals;
	}
}
