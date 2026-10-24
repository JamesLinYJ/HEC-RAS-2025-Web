using System;
using System.IO;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.Vectors;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Logging;
using Visual.Mapping.Observables.Map;
using Visual.Observables;
using Visual.Observables.Notifications;

namespace Visual.Mapping.Observables;

public abstract class VectorFileMapLayerVisualContext<TCollection, TFeature> : AsyncVectorMapLayerVisualContext, IPersistentLayerVisualContext, IDataHash, IVectorFeatureContext where TCollection : class, IVectorCollection<TFeature> where TFeature : ICloneable
{
	protected Projection _projection;

	private const int HashNDV = int.MinValue;

	private int _featureLoadHash = int.MinValue;

	bool IVectorFeatureContext.IsFeatureDataLoaded => IsDataLoaded;

	ObservableTable IVectorFeatureContext.FeatureAttributeTable => AttributeTable;

	int IVectorFeatureContext.FeatureCount => FeatureCollection?.Count ?? 0;

	protected abstract TCollection FeatureCollection { get; }

	protected abstract VectorSymbologyDefinition[] SymbologyDefinitions { get; }

	protected VectorFileMapLayerVisualContext(string filename, Projection projection, MapBase map)
		: base(filename, map)
	{
		Name = Path.GetFileNameWithoutExtension(Filename);
		_projection = projection;
	}

	protected override void LoadInBackground()
	{
		OperationResult or = LoadFromFile(out var features, out var table, out var renderLayer);
		Map.UIThreadSyncContext.Sync(() =>
		{
			Initializing = false;
			if (or.Messages.Count > 0)
			{
				ToastNotifiers.Default.SendAggregatedMessages(or);
				LayerErrors = or.Messages;
			}
			if ((bool)or)
			{
				PublishLoaded(renderLayer, table, SymbologyDefinitions, () =>
				{
					bool rowsMatchFeatures = table.RowCount == features.Count;
					StoreLoadedFeatures(features, table, rowsMatchFeatures);
					foreach (VectorSymbologyOption symbologyOption in SymbologyOptions)
					{
						symbologyOption.AttributeTable = SymbologyAttributeTable(table, rowsMatchFeatures);
					}
					_featureLoadHash = GetDataHash();
				});
			}
		});
	}

	protected abstract OperationResult LoadFromFile(out TCollection features, out ObservableTable table, out IRenderLayer renderLayer);

	protected abstract void StoreLoadedFeatures(TCollection features, ObservableTable table, bool rowsMatchFeatures);

	protected virtual ObservableTable SymbologyAttributeTable(ObservableTable table, bool rowsMatchFeatures)
	{
		return table;
	}

	protected override EditingSession CreateEditLayerCore(ToolOrchestrator editLayer)
	{
		if (!IsDataLoaded)
		{
			Initialize();
			return null;
		}
		return base.CreateEditLayerCore(editLayer);
	}

	public bool NeedsSave()
	{
		if (!IsDataLoaded)
		{
			return false;
		}
		if (GetDataHash() == _featureLoadHash)
		{
			return AttributeTable?.NeedsSave ?? false;
		}
		return true;
	}

	public int GetDataHash()
	{
		if (!IsDataLoaded)
		{
			return 0;
		}
		int combine = 0;
		foreach (TFeature featureCollection in FeatureCollection)
		{
			combine = HashCode.Combine(combine, GetFeatureHash(featureCollection));
		}
		return combine;
	}

	protected abstract int GetFeatureHash(TFeature feature);

	protected abstract OperationResult WriteShapefile();

	protected abstract OperationResult WriteGeoJson();

	public void Save()
	{
		OperationResult operationResult = new OperationResult();
		if (Filename.RoughlyEndsWith(".shp"))
		{
			try
			{
				ShapefileIO.DeleteAllAssociations(Filename);
				if (FeatureCollection.Count > 0)
				{
					operationResult = WriteShapefile();
					if (!operationResult)
					{
						ToastNotifiers.Default.SendAggregatedMessages(operationResult);
					}
				}
				return;
			}
			catch (Exception ex)
			{
				operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Saving " + typeof(TFeature).Name.ToLowerInvariant() + " shapefile " + Filename, ex));
				ToastNotifiers.Default.SendAggregatedMessages(operationResult);
				return;
			}
		}
		if (!GeoJsonReader.HasJsonFileExtension(Filename))
		{
			return;
		}
		try
		{
			operationResult = WriteGeoJson();
			if (!operationResult)
			{
				ToastNotifiers.Default.SendAggregatedMessages(operationResult);
			}
		}
		catch (Exception ex2)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Saving " + typeof(TFeature).Name.ToLowerInvariant() + " GeoJSON " + Filename, ex2));
			ToastNotifiers.Default.SendAggregatedMessages(operationResult);
		}
	}
}
