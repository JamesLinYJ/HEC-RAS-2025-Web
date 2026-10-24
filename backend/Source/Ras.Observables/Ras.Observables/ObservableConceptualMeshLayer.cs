using Geospatial.Vectors.Topology;
using Visual.Mapping.Observables;

namespace Ras.Observables;

public class ObservableConceptualMeshLayer : ObservableTopologyLayer
{
	private ObservableGeometry _geometry;

	public ConceptualMeshCache Cache;

	public ObservableGeometry Geometry
	{
		get
		{
			return _geometry;
		}
		private set
		{
			SetProperty(ref _geometry, value, "Geometry");
		}
	}

	public ObservableConceptualMeshLayer(ObservableGeometry geometry)
		: base(geometry.Geometry.ConceptualMeshLayer)
	{
		_geometry = geometry;
	}

	protected override ObservableTopology CreateTopology(Topology topology)
	{
		return new ObservableConceptualMeshTopology(topology);
	}
}
