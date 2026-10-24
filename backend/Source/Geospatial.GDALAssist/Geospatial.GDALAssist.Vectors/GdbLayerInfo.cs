namespace Geospatial.GDALAssist.Vectors;

public readonly record struct GdbLayerInfo(string Name, VectorType GeometryType, long FeatureCount, bool IsRaster, string ConnectionString);
