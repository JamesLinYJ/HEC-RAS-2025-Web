using Geospatial.IO.Vectors;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Hydraulics;
using Ras.Hydraulics.CrossSections;
using RasMapperLib;
using Utility.Logging;

namespace Ras.Migration;

public static class RasFeatureConverter
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ModelConverter");

	public static CrossSection Convert(RASXS xsl, int idx)
	{
		CrossSection crossSection = new CrossSection(GeoConverter.Convert(xsl.Polyline(idx)));
		crossSection.NodeName = xsl.GetNodeName(idx);
		if (string.IsNullOrEmpty(crossSection.NodeName))
		{
			crossSection.NodeName = IHasRRRSLayerExtensions.GetRiverStationName(xsl, idx);
		}
		crossSection.Description = xsl.GetNodeDescription(idx);
		xsl.GetFrictionMode(idx);
		crossSection.RiverName = IHasRRRSLayerExtensions.GetRiverName(xsl, idx);
		crossSection.ReachName = IHasRRRSLayerExtensions.GetReachName(xsl, idx);
		crossSection.RiverStation = IHasRRRSLayerExtensions.GetRiverStationName(xsl, idx);
		crossSection.MinorLoss = xsl.GetMinorLossCoef(idx);
		crossSection.Skew = xsl.GetSkewCoef(idx);
		RasMapperLib.LeveeProperties leveeProperties = xsl.GetLeveeProperties(idx);
		crossSection.LeveeProperties.LeftStation = leveeProperties.LeftStation;
		crossSection.LeveeProperties.LeftElevation = leveeProperties.LeftElevation;
		crossSection.LeveeProperties.RightStation = leveeProperties.RightStation;
		crossSection.LeveeProperties.RightElevation = leveeProperties.RightElevation;
		ContractionExpansionCoefficients contractionExpansionCoefficients = crossSection.ContractionExpansionCoefficients;
		contractionExpansionCoefficients.ContractionCoefficient = xsl.GetContractionCoef(idx);
		contractionExpansionCoefficients.ExpansionCoefficient = xsl.GetExpansionCoef(idx);
		contractionExpansionCoefficients.USFContractionCoefficient = xsl.GetUnsteadyContractionCoef(idx);
		contractionExpansionCoefficients.USFExpansionCoefficient = xsl.GetUnsteadyExpansionCoef(idx);
		_ = crossSection.IceProperties;
		Profile profile = xsl.GetProfile(idx);
		crossSection.OverrideStationElevation = GeoConverter.Convert(profile);
		crossSection.OverrideStationElevation.AdjustToZeroStationing();
		_logger.LogTrace("Migrated cross section {xsName}", crossSection.NodeName);
		return crossSection;
	}

	public static River Convert(RASRiver rivL, int idx)
	{
		Geospatial.Vectors.Polyline polyline = GeoConverter.Convert(rivL.Polyline(idx));
		River river = new River();
		river.RiverName = rivL.GetRiverName(idx);
		river.ReachName = rivL.GetReachName(idx);
		river.Polyline = polyline;
		_logger.LogTrace("Migrated river {riverName}", river.ToString());
		return river;
	}

	public static StorageArea Convert(RASStorageArea saL, int idx)
	{
		Geospatial.Vectors.Polygon polygon = GeoConverter.Convert(saL.Polygon(idx));
		StorageArea storageArea = new StorageArea();
		storageArea.Name = saL.GetFeatureName(idx);
		storageArea.Polygon = polygon;
		_logger.LogTrace("Migrated Storage Area {saName}", storageArea.Name);
		return storageArea;
	}

	public static Bankline Convert(RASBankLines bankL, int idx)
	{
		Geospatial.Vectors.Polyline polyline = GeoConverter.Convert(bankL.Polyline(idx));
		Bankline bankline = new Bankline();
		bankline.Polyline = polyline;
		_logger.LogTrace("Migrated bankline {idx}", idx);
		return bankline;
	}

	public static FlowPathLine Convert(FlowPaths fpl, int idx)
	{
		Geospatial.Vectors.Polyline polyline = GeoConverter.Convert(fpl.Polyline(idx));
		FlowPathLine flowPathLine = new FlowPathLine();
		flowPathLine.Polyline = polyline;
		_logger.LogTrace("Migrated flow path line {idx}", idx);
		return flowPathLine;
	}

	public static Edgeline Convert(RASEdgeLines edg, int idx)
	{
		Geospatial.Vectors.Polyline polyline = GeoConverter.Convert(edg.Polyline(idx));
		Edgeline edgeline = new Edgeline();
		edgeline.Polyline = polyline;
		_logger.LogTrace("Migrated edgeline {idx}", idx);
		return edgeline;
	}

	public static FlowArea Convert(RASD2FlowArea fa, int idx, H5Reader hr)
	{
		MeshFV2D meshFV2D = fa.Mesh(idx);
		if (meshFV2D.IsComplete())
		{
			Mesh mesh = GeoConverter.Convert(meshFV2D);
			string featureName = fa.GetFeatureName(idx);
			_logger.LogTrace("Migrated flow area {mesh}", featureName);
			_logger.LogWarning("TODO: Migrate property tables.");
			FlowArea flowArea = new FlowArea(mesh, featureName);
			string meshGroup = "Geometry/2D Flow Areas/" + featureName;
			if (MeshReader.ContainsTables(hr, meshGroup, out var isLegacy) && isLegacy)
			{
				flowArea.PropertyTables = MeshReader.ReadTables(mesh, hr, meshGroup);
			}
			return flowArea;
		}
		_logger.LogInformation("Could not migrate flow area {mesh} (not created)", fa.GetFeatureName(idx));
		return new FlowArea(Mesh.NullMesh(), "NULL");
	}

	public static InterpolationSurface Convert(RASXSInterpolationSurface xsis, int idx)
	{
		return new InterpolationSurface(GeoConverter.Convert(xsis.Tin(idx)), xsis.GetUpstreamXSID(idx), xsis.GetDownstreamXSID(idx));
	}
}
