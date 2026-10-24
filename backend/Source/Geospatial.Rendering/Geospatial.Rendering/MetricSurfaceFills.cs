namespace Geospatial.Rendering;

public static class MetricSurfaceFills
{
	public static InterpolatedSurfaceFill DepthsValues = new InterpolatedSurfaceFill(ColorPalettes.Depths, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 5.0 }), isStretched: true, regenForScreen: false, "Depths");

	public static InterpolatedSurfaceFill VelocitiesValues = new InterpolatedSurfaceFill(ColorPalettes.Velocities, new _003C_003Ez__ReadOnlyArray<double>(new double[7] { 0.0, 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 }), isStretched: true, regenForScreen: false, "Velocities");

	public static InterpolatedSurfaceFill WSErrorValues = new InterpolatedSurfaceFill(ColorPalettes.WSError, new _003C_003Ez__ReadOnlyArray<double>(new double[6] { 0.0, 0.006, 0.3, 0.15, 0.23, 0.3 }), isStretched: true, regenForScreen: false, "Water Surface Error");

	public static InterpolatedSurfaceFill WSElevationDefaultValues = new InterpolatedSurfaceFill(ColorPalettes.WSError, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 30.0 }), isStretched: true, regenForScreen: false, "Water Surface Elevation");

	public static InterpolatedSurfaceFill GenericDefaultValues = new InterpolatedSurfaceFill(ColorPalettes.WSError, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 7.0 }), isStretched: true, regenForScreen: false, "Generic Default");

	public static InterpolatedSurfaceFill ShearValues = new InterpolatedSurfaceFill(ColorPalettes.Shear, new _003C_003Ez__ReadOnlyArray<double>(new double[6] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0 }), isStretched: true, regenForScreen: false, "Shear");

	public static InterpolatedSurfaceFill IceValues = new InterpolatedSurfaceFill(ColorPalettes.Ice, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 3.0 }), isStretched: true, regenForScreen: false, "Ice");

	public static InterpolatedSurfaceFill DepthForecastValues = new InterpolatedSurfaceFill(ColorPalettes.DepthForecast, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 5.0 }), isStretched: true, regenForScreen: false, "Depth Forecast");

	public static InterpolatedSurfaceFill PrecipitationValues = new InterpolatedSurfaceFill(ColorPalettes.Precipitation, new _003C_003Ez__ReadOnlyArray<double>(new double[17]
	{
		0.25, 2.5, 12.7, 25.4, 38.0, 51.0, 64.0, 76.0, 102.0, 127.0,
		191.0, 254.0, 318.0, 381.0, 508.0, 635.0, 762.0
	}), isStretched: true, regenForScreen: false, "Precipitation");

	public static InterpolatedSurfaceFill PrecipitationCumulativeValues = new InterpolatedSurfaceFill(ColorPalettes.PrecipitationCumulative, new _003C_003Ez__ReadOnlyArray<double>(new double[17]
	{
		0.25, 2.5, 12.7, 25.4, 38.0, 51.0, 64.0, 76.0, 102.0, 127.0,
		191.0, 254.0, 318.0, 381.0, 508.0, 635.0, 762.0
	}), isStretched: true, regenForScreen: false, "Precipitation Cumulative");

	public static InterpolatedSurfaceFill PrecipitationIncrementalValues = new InterpolatedSurfaceFill(ColorPalettes.PrecipitationIncremental, new _003C_003Ez__ReadOnlyArray<double>(new double[15]
	{
		0.003, 0.03, 0.6, 1.3, 1.9, 2.5, 4.0, 5.0, 6.5, 8.0,
		10.0, 12.5, 15.0, 20.0, 25.0
	}), isStretched: true, regenForScreen: false, "Precipitation Incremental");

	public static InterpolatedSurfaceFill MudValues = new InterpolatedSurfaceFill(ColorPalettes.Mud, new _003C_003Ez__ReadOnlyArray<double>(new double[6] { 0.05, 0.3, 0.5, 1.0, 1.5, 3.0 }), isStretched: true, regenForScreen: false, "Mud");

	public static InterpolatedSurfaceFill WindSpeedValues = new InterpolatedSurfaceFill(ColorPalettes.WindSpeed, new _003C_003Ez__ReadOnlyArray<double>(new double[25]
	{
		0.0, 8.0, 16.0, 24.0, 32.0, 40.0, 48.0, 56.0, 64.0, 72.0,
		80.0, 88.0, 97.0, 105.0, 113.0, 120.0, 129.0, 137.0, 145.0, 153.0,
		161.0, 169.0, 177.0, 185.0, 193.0
	}), isStretched: true, regenForScreen: false, "Wind Speed");

	public static InterpolatedSurfaceFill AirTemperatureValues = new InterpolatedSurfaceFill(ColorPalettes.AirTemperature, new _003C_003Ez__ReadOnlyArray<double>(new double[13]
	{
		-30.0, -15.0, -10.0, -5.0, 0.0, 5.0, 10.0, 15.0, 20.0, 25.0,
		30.0, 35.0, 50.0
	}), isStretched: true, regenForScreen: false, "Air Temperature");
}
