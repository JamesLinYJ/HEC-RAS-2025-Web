using System;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Visual.Mapping.Observables.Map;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class GdbPolylineLayerVisualContext : GdbVectorLayerVisualContext, IPolylineVectorContext, IVectorContext<Polyline>, IVectorFeatureContext
{
	private PolylineCollection _polylines;

	public IPolylineCollection Polylines
	{
		get
		{
			if (!IsDataLoaded)
			{
				return null;
			}
			return _polylines;
		}
	}

	bool IVectorFeatureContext.IsFeatureDataLoaded => IsDataLoaded;

	ObservableTable IVectorFeatureContext.FeatureAttributeTable => AttributeTable;

	public GdbPolylineLayerVisualContext(string gdbPath, string layerName, VectorType vectorType, Projection projection, MapBase map)
		: base(gdbPath, layerName, vectorType, projection, map)
	{
	}

	protected override IRenderLayer BuildRenderLayer(VectorLayer layer, Projection srcProj)
	{
		PolylineCollection polylineCollection = layer.ReadFeaturesAsPolylines(Extent.EffectivelyInfinite);
		if (polylineCollection == null || polylineCollection.Count == 0)
		{
			return null;
		}
		if (NeedsReprojection(srcProj))
		{
			using ReprojectionTransformer reprojectionTransformer = new ReprojectionTransformer(srcProj, TargetProjection);
			PolylineCollection polylineCollection2 = new PolylineCollection(polylineCollection.Count);
			foreach (Polyline polylineCollection3 in polylineCollection)
			{
				Memory<Point> pointBufferUnsafe = polylineCollection3.GetPointBufferUnsafe();
				Point[] points = new Point[polylineCollection3.Count];
				reprojectionTransformer.Transform(pointBufferUnsafe, points.AsMemory());
				polylineCollection2.Add(new Polyline(points));
			}
			polylineCollection = polylineCollection2;
		}
		_polylines = polylineCollection;
		return new PolylineRenderLayer(polylineCollection);
	}

	protected override VectorSymbologyDefinition[] GetSymbologyDefinitions()
	{
		return new VectorSymbologyDefinition[2]
		{
			GdbVectorLayerVisualContext._defaultLineSymbDef,
			GdbVectorLayerVisualContext._defaultLineVertexSymbDef
		};
	}
}
