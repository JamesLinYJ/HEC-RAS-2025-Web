namespace Geospatial.Rendering;

public static class USCustomarySurfaceFills
{
	public static InterpolatedSurfaceFill DepthsValues = new InterpolatedSurfaceFill(ColorPalettes.Depths, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 15.0 }), isStretched: true, regenForScreen: false, "Depths");

	public static InterpolatedSurfaceFill VelocitiesValues = new InterpolatedSurfaceFill(ColorPalettes.Velocities, new _003C_003Ez__ReadOnlyArray<double>(new double[7] { 0.0, 2.0, 4.0, 6.0, 8.0, 10.0, 15.0 }), isStretched: true, regenForScreen: false, "Velocities");

	public static InterpolatedSurfaceFill WSErrorValues = new InterpolatedSurfaceFill(ColorPalettes.WSError, new _003C_003Ez__ReadOnlyArray<double>(new double[6] { 0.0, 0.02, 0.1, 0.5, 0.7, 1.0 }), isStretched: true, regenForScreen: false, "Water Surface Error");

	public static InterpolatedSurfaceFill WSElevationDefaultValues = new InterpolatedSurfaceFill(ColorPalettes.WSError, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 100.0 }), isStretched: true, regenForScreen: false, "Water Surface Elevation");

	public static InterpolatedSurfaceFill GenericDefaultValues = new InterpolatedSurfaceFill(ColorPalettes.WSError, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 20.0 }), isStretched: true, regenForScreen: false, "Generic Default");

	public static InterpolatedSurfaceFill ShearValues = new InterpolatedSurfaceFill(ColorPalettes.Shear, new _003C_003Ez__ReadOnlyArray<double>(new double[6] { 0.0, 0.02, 0.04, 0.06, 0.08, 0.1 }), isStretched: true, regenForScreen: false, "Shear");

	public static InterpolatedSurfaceFill IceValues = new InterpolatedSurfaceFill(ColorPalettes.Ice, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 10.0 }), isStretched: true, regenForScreen: false, "Ice");

	public static InterpolatedSurfaceFill DepthForecastValues = new InterpolatedSurfaceFill(ColorPalettes.DepthForecast, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 15.0 }), isStretched: true, regenForScreen: false, "Depth Forecast");

	public static InterpolatedSurfaceFill PrecipitationValues = new InterpolatedSurfaceFill(ColorPalettes.Precipitation, new _003C_003Ez__ReadOnlyArray<double>(new double[17]
	{
		0.01, 0.1, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0,
		7.5, 10.0, 12.5, 15.0, 20.0, 25.0, 30.0
	}), isStretched: true, regenForScreen: false, "Precipitation");

	public static InterpolatedSurfaceFill PrecipitationCumulativeValues = new InterpolatedSurfaceFill(ColorPalettes.PrecipitationCumulative, new _003C_003Ez__ReadOnlyArray<double>(new double[17]
	{
		0.01, 0.1, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0,
		7.5, 10.0, 12.5, 15.0, 20.0, 25.0, 30.0
	}), isStretched: true, regenForScreen: false, "Precipitation Cumulative");

	public static InterpolatedSurfaceFill PrecipitationIncrementalValues = new InterpolatedSurfaceFill(ColorPalettes.PrecipitationIncremental, new _003C_003Ez__ReadOnlyArray<double>(new double[15]
	{
		0.001, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.2, 0.25, 0.3,
		0.4, 0.5, 0.6, 0.8, 1.0
	}), isStretched: true, regenForScreen: false, "Precipitation Incremental");

	public static InterpolatedSurfaceFill MudValues = new InterpolatedSurfaceFill(ColorPalettes.Mud, new _003C_003Ez__ReadOnlyArray<double>(new double[6] { 0.1, 1.0, 2.0, 3.0, 5.0, 10.0 }), isStretched: true, regenForScreen: false, "Mud");

	public static InterpolatedSurfaceFill WindSpeedValues = new InterpolatedSurfaceFill(ColorPalettes.WindSpeed, new _003C_003Ez__ReadOnlyArray<double>(new double[25]
	{
		0.0, 5.0, 10.0, 15.0, 20.0, 25.0, 30.0, 35.0, 40.0, 45.0,
		50.0, 55.0, 60.0, 65.0, 70.0, 75.0, 80.0, 85.0, 90.0, 95.0,
		100.0, 105.0, 110.0, 115.0, 120.0
	}), isStretched: true, regenForScreen: false, "Wind Speed");

	public static InterpolatedSurfaceFill AirTemperatureValues = new InterpolatedSurfaceFill(ColorPalettes.AirTemperature, new _003C_003Ez__ReadOnlyArray<double>(new double[13]
	{
		-20.0, 0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0,
		90.0, 100.0, 120.0
	}), isStretched: true, regenForScreen: false, "Air Temperature");
}
