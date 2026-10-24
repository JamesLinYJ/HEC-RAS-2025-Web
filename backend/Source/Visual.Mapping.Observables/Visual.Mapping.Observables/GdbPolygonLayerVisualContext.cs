using System.Collections.Generic;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Visual.Mapping.Observables.Map;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class GdbPolygonLayerVisualContext : GdbVectorLayerVisualContext, IPolygonVectorContext, IVectorContext<Polygon>, IVectorFeatureContext
{
	private PolygonCollection _outerRings;

	public IPolygonCollection Polygons
	{
		get
		{
			if (!IsDataLoaded)
			{
				return null;
			}
			return _outerRings;
		}
	}

	bool IVectorFeatureContext.IsFeatureDataLoaded => IsDataLoaded;

	ObservableTable IVectorFeatureContext.FeatureAttributeTable => AttributeTable;

	public GdbPolygonLayerVisualContext(string gdbPath, string layerName, VectorType vectorType, Projection projection, MapBase map)
		: base(gdbPath, layerName, vectorType, projection, map)
	{
	}

	protected override IRenderLayer BuildRenderLayer(VectorLayer layer, Projection srcProj)
	{
		List<ComplexPolygonCollection> featuresAsPolygonCollections = layer.ReadFeaturesAsPolygonCollections(Extent.EffectivelyInfinite);
		if (featuresAsPolygonCollections == null || featuresAsPolygonCollections.Count == 0)
		{
			return null;
		}
		ComplexPolygonCollection complexPolygonCollection = new ComplexPolygonCollection();
		foreach (ComplexPolygonCollection featuresAsPolygonCollection in featuresAsPolygonCollections)
		{
			if (featuresAsPolygonCollection == null)
			{
				continue;
			}
			foreach (ComplexPolygon polygon in featuresAsPolygonCollection.Polygons)
			{
				if (polygon != null)
				{
					complexPolygonCollection.Add(polygon);
				}
			}
		}
		if (complexPolygonCollection.Count == 0)
		{
			return null;
		}
		if (NeedsReprojection(srcProj))
		{
			using ReprojectionTransformer reprojectionTransformer = new ReprojectionTransformer(srcProj, TargetProjection);
			complexPolygonCollection = new ComplexPolygonCollection(reprojectionTransformer.Transform(complexPolygonCollection.Polygons));
		}
		_outerRings = new PolygonCollection(complexPolygonCollection.Polygons.Select((ComplexPolygon cp) => cp.OuterRing.Polygon));
		return new ComplexPolygonRenderLayer(complexPolygonCollection);
	}

	protected override VectorSymbologyDefinition[] GetSymbologyDefinitions()
	{
		return new VectorSymbologyDefinition[1] { GdbVectorLayerVisualContext._defaultPolygonSymbDef };
	}
}
