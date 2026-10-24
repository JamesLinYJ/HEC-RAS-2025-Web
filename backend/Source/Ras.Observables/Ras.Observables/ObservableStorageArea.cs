using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Observables;

public class ObservableStorageArea : ObservableObject, IObservableFeature<StorageArea>
{
	private readonly StorageArea _storageArea;

	public StorageArea StorageArea => _storageArea;

	public StorageArea BaseFeature => _storageArea;

	public string Name
	{
		get
		{
			return _storageArea.Name;
		}
		set
		{
			SetProperty(_storageArea.Name, value, _storageArea, (StorageArea sa, string v) =>
			{
				sa.Name = v;
			}, "Name");
		}
	}

	public Polygon Polygon
	{
		get
		{
			return _storageArea.Polygon;
		}
		set
		{
			SetProperty(_storageArea.Polygon, value, _storageArea, (StorageArea sa, Polygon v) =>
			{
				sa.Polygon = v;
			}, "Polygon");
		}
	}

	public double Area => _storageArea.Area;

	public ObservableStorageArea(StorageArea sa)
	{
		_storageArea = sa;
	}
}
