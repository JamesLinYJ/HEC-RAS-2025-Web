using System;
using Geospatial.Rendering;
using Ras.Layers.BoundaryConditions;
using Utility.Units;
using Visual.Mapping.Observables;

namespace Ras.Observables;

public class ObservableAirDensityLayer : ObservableSpatialScalarBoundaryConditionBase<AirDensityLayer>
{
	private static Type[] _validTypes = new Type[1] { typeof(AirDensity) };

	public static double[] Pressures = new double[4] { 950.0, 987.5, 1025.0, 1100.0 };

	public static InterpolatedSurfaceFill SymbologyFill = new InterpolatedSurfaceFill(ColorPalettes.AirPressure, Pressures);

	public static RasterSymbology Symbology = new RasterSymbology(SymbologyFill);

	public static ObservableRasterSymbology StaticObservableSymbology = new ObservableRasterSymbology(Symbology);

	public override Type[] ValidUnitTypes => _validTypes;

	public ObservableRasterSymbology ObservableSymbology => StaticObservableSymbology;

	public ObservableAirDensityLayer(ObservableBoundaryCondition bc)
		: base(bc, bc.BoundaryCondition.AirDensity)
	{
	}
}
