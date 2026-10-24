using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics.Classic;

namespace Ras.Observables.Classic;

public class ObservableStructure : ObservableObject, IObservableFeature<Structure>
{
	private Structure _structure;

	public Structure BaseFeature => _structure;

	public Polyline Polyline
	{
		get
		{
			return _structure.Polyline;
		}
		set
		{
			SetProperty(_structure.Polyline, value, _structure, (Structure r, Polyline v) =>
			{
				r.Polyline = v;
			}, "Polyline");
		}
	}

	public StructureIdentifier ID
	{
		get
		{
			return _structure.ID;
		}
		set
		{
			SetProperty(_structure.ID, value, _structure, (Structure r, StructureIdentifier v) =>
			{
				r.ID = v;
			}, "ID");
		}
	}

	public ObservableStructure(Structure structure)
	{
		_structure = structure;
	}
}
