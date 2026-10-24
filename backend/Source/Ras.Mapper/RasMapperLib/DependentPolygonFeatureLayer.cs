using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Structures;

namespace RasMapperLib;

public abstract class DependentPolygonFeatureLayer : PolygonFeatureLayer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__13_002D0
	{
		public FeatureLayer flyr;

		public DependentPolygonFeatureLayer dependentPolygonFeatureLayer;

		public _Closure_0024__13_002D0(_Closure_0024__13_002D0 other)
		{
			if (other != null)
			{
				flyr = other.flyr;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(object ob, FeatureChangedEventArgs ev)
		{
			dependentPolygonFeatureLayer.InvalidateFeatures(flyr, ev);
		}

		[SpecialName]
		internal void _Lambda_0024__1(object ob, FeatureChangedEventArgs ev)
		{
			dependentPolygonFeatureLayer.OnLayerDependencyChanging(flyr, ev);
		}
	}

	private const string sPolygonValid = "Polygon Valid";

	private List<string> _columnDependencies;

	private List<FeatureLayer> _layerDependencies;

	private HashSet<int> _groupIdxsBeingChangedFromDelete;

	public override bool CanEdit { get; }

	public DependentPolygonFeatureLayer(string lyrName, string filename)
		: base(lyrName, filename)
	{
		CanEdit = false;
		_groupIdxsBeingChangedFromDelete = new HashSet<int>();
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Polygon Valid", typeof(bool), null, DataColumnViewState.Invisible);
	}

	protected abstract Polygon RecomputePolygon(int fid);

	protected abstract List<string> GetColumnDependecies();

	protected abstract List<FeatureLayer> GetLayerDependencies();

	public override Feature Feature(int fId)
	{
		if (!GetCachedPolygonValid(fId))
		{
			Polygon feature = RecomputePolygon(fId);
			SetCachedPolygonValid(fId, isValid: true);
			SetFeature(fId, feature);
		}
		return base.Feature(fId);
	}

	protected override void OnFeatureTableLoaded()
	{
		base.OnFeatureTableLoaded();
		_columnDependencies = GetColumnDependecies();
		_layerDependencies = GetLayerDependencies();
		if (_columnDependencies == null)
		{
			_columnDependencies = new List<string>();
		}
		if (_layerDependencies == null)
		{
			_layerDependencies = new List<FeatureLayer>();
		}
		FeatureTable().ColumnChanging += AttributeChanging;
		using List<FeatureLayer>.Enumerator layerDependencies = _layerDependencies.GetEnumerator();
		_Closure_0024__13_002D0 capturedLocals = default;
		while (layerDependencies.MoveNext())
		{
			capturedLocals = new _Closure_0024__13_002D0(capturedLocals);
			capturedLocals.dependentPolygonFeatureLayer = this;
			capturedLocals.flyr = layerDependencies.Current;
			capturedLocals.flyr.FeatureChanged += capturedLocals._Lambda_0024__0;
			capturedLocals.flyr.FeatureChanging += capturedLocals._Lambda_0024__1;
		}
	}

	protected void OnLayerDependencyChanging(FeatureLayer flyr, FeatureChangedEventArgs args)
	{
		_groupIdxsBeingChangedFromDelete.Clear();
		if (args.ChangeType != FeatureChangedType.FeatureDeleted)
		{
			return;
		}
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			if (flyr is IOpeningLayer)
			{
				_groupIdxsBeingChangedFromDelete.Add(((IOpeningLayer)flyr).TryGetGroupIndex(featureIndicesChanged));
			}
		}
	}

	private void InvalidateFeatures(FeatureLayer flyr, FeatureChangedEventArgs e)
	{
	}

	private void AttributeChanging(object ob, DataColumnChangeEventArgs ev)
	{
		List<int> featureModifiedIds = new List<int>();
		if (!_columnDependencies.Contains(ev.Column.ColumnName) || ev.Row[ev.Column].Equals(RuntimeHelpers.GetObjectValue(ev.ProposedValue)))
		{
			return;
		}
		checked
		{
			foreach (string columnDependency in _columnDependencies)
			{
				if (Operators.CompareString(ev.Column.ColumnName, columnDependency, TextCompare: false) != 0)
				{
					continue;
				}
				ev.Row["Polygon Valid"] = false;
				DataTable dataTable = (DataTable)ob;
				int ordinal = 0;
				int count = dataTable.Rows.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (object.ReferenceEquals(RuntimeHelpers.GetObjectValue(dataTable.Rows[i]["FID"]), RuntimeHelpers.GetObjectValue(ev.Row["FID"])))
					{
						ordinal = i;
						break;
					}
				}
				featureModifiedIds.Add(ordinal);
				break;
			}
			ForceFeatureChange(new FeatureChangedEventArgs(featureModifiedIds, FeatureChangedType.FeatureModified), plotLayers: true);
		}
	}

	public bool GetCachedPolygonValid(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Polygon Valid");
	}

	public void SetCachedPolygonValid(int fID, bool isValid)
	{
		FeatureRow(fID)["Polygon Valid"] = isValid;
	}
}
