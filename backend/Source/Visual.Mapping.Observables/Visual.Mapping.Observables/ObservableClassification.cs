using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rasters.Classifications;

namespace Visual.Mapping.Observables;

public class ObservableClassification : ObservableObject
{
	public delegate void ClassificationValueChangedEvent(ObservableClassification sender);

	private Classification _classification;

	private ObservableCollection<ObservableClassificationMapRow> _rows;

	public Classification Classification => _classification;

	public ObservableCollection<ObservableClassificationMapRow> Rows
	{
		get
		{
			if (_rows == null)
			{
				BuildRows();
			}
			return _rows;
		}
	}

	public event ClassificationValueChangedEvent ClassificationValueChanged;

	public ObservableClassification(Classification classification)
	{
		_classification = classification;
		BuildRows();
	}

	private void BuildRows()
	{
		ObservableCollection<ObservableClassificationMapRow> observableCollection = new ObservableCollection<ObservableClassificationMapRow>();
		foreach (KeyValuePair<int, string> allClassification in Classification.GetAllClassifications())
		{
			ObservableClassificationMapRow observableClassificationMapRow = new ObservableClassificationMapRow(allClassification.Key, allClassification.Value);
			observableCollection.Add(observableClassificationMapRow);
			observableClassificationMapRow.ValueChanged += RowValueChanged;
		}
		_rows = observableCollection;
		_rows.CollectionChanged += RowsCollectionChanged;
	}

	private void RowValueChanged(ObservableClassificationMapRow sender, string oldValue, string newValue)
	{
		Classification.Set(sender.Key, newValue);
		ClassificationValueChanged?.Invoke(this);
	}

	private void RowsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		throw new NotImplementedException("TODO: Implement handling changes for if an external user adds/deletes rows from the table.");
	}
}
