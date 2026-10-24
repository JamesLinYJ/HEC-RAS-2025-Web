using System;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Visual.Mapping.Observables.Map;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class GdbPointLayerVisualContext : GdbVectorLayerVisualContext, IPointVectorContext, IVectorContext<Point>, IVectorFeatureContext
{
	private PointCollection _points;

	public IPointCollection Points
	{
		get
		{
			if (!IsDataLoaded)
			{
				return null;
			}
			return _points;
		}
	}

	bool IVectorFeatureContext.IsFeatureDataLoaded => IsDataLoaded;

	ObservableTable IVectorFeatureContext.FeatureAttributeTable => AttributeTable;

	public GdbPointLayerVisualContext(string gdbPath, string layerName, VectorType vectorType, Projection projection, MapBase map)
		: base(gdbPath, layerName, vectorType, projection, map)
	{
	}

	protected override IRenderLayer BuildRenderLayer(VectorLayer layer, Projection srcProj)
	{
		PointCollection pointCollection = layer.ReadFeaturesAsPoints(Extent.EffectivelyInfinite);
		if (pointCollection == null || pointCollection.Count == 0)
		{
			return null;
		}
		if (NeedsReprojection(srcProj))
		{
			using ReprojectionTransformer reprojectionTransformer = new ReprojectionTransformer(srcProj, TargetProjection);
			Memory<Point> pointBufferUnsafe = pointCollection.GetPointBufferUnsafe();
			Point[] points = new Point[pointCollection.Count];
			reprojectionTransformer.Transform(pointBufferUnsafe, points.AsMemory());
			pointCollection = new PointCollection(points);
		}
		_points = pointCollection;
		return new PointRenderLayer(pointCollection);
	}

	protected override VectorSymbologyDefinition[] GetSymbologyDefinitions()
	{
		return new VectorSymbologyDefinition[1] { GdbVectorLayerVisualContext._defaultPointSymbDef };
	}
}
