using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers;

namespace Ras.Observables;

public class ObservableNValueTable : ObservableObject
{
	private NVTable _table;

	private ObservableCollection<ObservableNValueTableRow> _rows;

	public NVTable Table => _table;

	public ObservableCollection<ObservableNValueTableRow> Rows
	{
		get
		{
			return _rows;
		}
		private set
		{
			SetProperty(ref _rows, value, "Rows");
		}
	}

	public event NotifyCollectionChangedEventHandler RowCollectionChangedEvent;

	public event ObservableNValueTableRow.KeyChangedEvent RowKeyChangedEvent;

	public event ObservableNValueTableRow.ValueChangedEvent RowValueChangedEvent;

	public ObservableNValueTable(NVTable table)
	{
		_table = table;
		Init();
	}

	private void Init()
	{
		ObservableCollection<ObservableNValueTableRow> observableCollection = new ObservableCollection<ObservableNValueTableRow>();
		foreach (KeyValuePair<string, float> rawDatum in _table.RawData)
		{
			ObservableNValueTableRow observableNValueTableRow = new ObservableNValueTableRow(rawDatum.Key, rawDatum.Value);
			observableCollection.Add(observableNValueTableRow);
			observableNValueTableRow.KeyChanged += RowKeyChanged;
			observableNValueTableRow.ValueChanged += RowValueChanged;
		}
		Rows = observableCollection;
		Rows.CollectionChanged += RowsCollectionChanged;
	}

	private void RowValueChanged(ObservableNValueTableRow sender, float oldValue, float newValue)
	{
		_table.Set(sender.Key, newValue);
		RowValueChangedEvent?.Invoke(sender, oldValue, newValue);
	}

	private void RowKeyChanged(ObservableNValueTableRow sender, string oldValue, string newValue)
	{
		_table.UpdateName(oldValue, newValue);
		RowKeyChangedEvent?.Invoke(sender, oldValue, newValue);
	}

	private void RowsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			_table.Clear();
		}
		else
		{
			if (e.NewItems != null)
			{
				foreach (ObservableNValueTableRow newItem in e.NewItems)
				{
					_table.Set(newItem.Key, newItem.Value);
					newItem.KeyChanged += RowKeyChanged;
					newItem.ValueChanged += RowValueChanged;
				}
			}
			if (e.OldItems != null)
			{
				foreach (ObservableNValueTableRow oldItem in e.OldItems)
				{
					_table.Remove(oldItem.Key);
					oldItem.KeyChanged -= RowKeyChanged;
					oldItem.ValueChanged -= RowValueChanged;
				}
			}
		}
		RowCollectionChangedEvent?.Invoke(sender, e);
	}
}
