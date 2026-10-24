using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Ras.Hydraulics.HydraulicDesign;
using Ras.Layers;
using Ras.Migration.Files;
using Utility.Logging;

namespace Ras.Migration;

public class HydraulicDesignConverter
{
	private static readonly ILogger _logger = LogHelper.GetLogger("HydraulicDesignConverter");

	public static string PeekHDTitle(SimpleFileResults results, string v6Filename)
	{
		return results.ReadStringValue("HDes Title", null, allowCache: false);
	}

	public static StableChannelDesign MigrateStableChannelDesign(SimpleFileResults results)
	{
		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Start();
		_logger.LogInformation("Migrating stable channel design data");
		StableChannelDesign stableChannelDesign = StableChannelDesign.CreateInMemory();
		stableChannelDesign.Copeland = new CopelandMethod
		{
			SpecificGravity = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.SpecificGravity),
			D84 = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.D84),
			D50 = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.D50),
			D16 = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.D16),
			Temperature = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.Temperature),
			ValleySlope = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.ValleySlope),
			MedianChannelWidth = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.MedianChannelWidth),
			StableDesign = new CopelandMethodStableDesign
			{
				Discharge = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableDesign.Discharge),
				LeftSideSlope = new CopelandMethodSideSlope
				{
					Value = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableDesign.LeftSideSlope),
					Equation = results.ReadEnumValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableDesign.LeftSideSlopeEquation, BankEquation.Strickler),
					Roughness = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableDesign.LeftSideSlopeRoughnessValue)
				},
				RightSideSlope = new CopelandMethodSideSlope
				{
					Value = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableDesign.RightSideSlope),
					Equation = results.ReadEnumValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableDesign.RightSideSlopeEquation, BankEquation.Strickler),
					Roughness = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableDesign.RightSideSlopeRoughnessValue)
				}
			},
			StableSupply = new CopelandMethodStableSupply
			{
				SedimentConcentration = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.SedimentConcentration),
				ChannelWidth = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.ChannelWidth),
				ChannelBankHeight = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.ChannelBankHeight),
				ChannelSlope = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.ChannelSlope),
				LeftSideSlope = new CopelandMethodSideSlope
				{
					Value = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.LeftSideSlope),
					Equation = results.ReadEnumValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.LeftSideSlopeEquation, BankEquation.Strickler),
					Roughness = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.LeftSideSlopeRoughnessValue)
				},
				RightSideSlope = new CopelandMethodSideSlope
				{
					Value = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.RightSideSlope),
					Equation = results.ReadEnumValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.RightSideSlopeEquation, BankEquation.Strickler),
					Roughness = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.CopelandMethod.StableSupply.RightSideSlopeRoughnessValue)
				}
			}
		};
		stableChannelDesign.Regime = new RegimeMethod
		{
			D50 = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.RegimeMethod.D50),
			Discharge = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.RegimeMethod.Discharge),
			SedimentConcentration = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.RegimeMethod.SedimentConcentration),
			SideFactor = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.RegimeMethod.SideFactor),
			Temperature = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.RegimeMethod.Temperature)
		};
		stableChannelDesign.TractiveForce = new TractiveForceMethod
		{
			Depth = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.Depth),
			Discharge = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.Discharge),
			Temperature = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.Temperature),
			Width = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.Width),
			SolutionMethod = results.ReadEnumValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.SolutionMethod, TractiveForceSolutionMethod.Lane),
			Slope = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.Slope),
			LeftBankSideSlope = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.SideSlopeLeft),
			RightBankSideSlope = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.SideSlopeRight),
			LeftBank = new TractiveForceMethodPart
			{
				D = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.dLeft),
				AngleOfRepose = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.AngleOfReposeLeft),
				Roughness = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.RoughnessLeft),
				SpecificGravity = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.SpecificGravityLeft),
				ShieldsParameter = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.ShieldsParameterLeft),
				Equation = results.ReadEnumValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.LeftSideSlopeEquation, BankEquation.Strickler)
			},
			Bed = new TractiveForceMethodPart
			{
				D = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.dBed),
				AngleOfRepose = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.AngleOfReposeBed),
				Roughness = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.RoughnessBed),
				SpecificGravity = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.SpecificGravityBed),
				ShieldsParameter = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.ShieldsParameterBed),
				Equation = results.ReadEnumValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.BedEquation, BankEquation.Strickler)
			},
			RightBank = new TractiveForceMethodPart
			{
				D = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.dRight),
				AngleOfRepose = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.AngleOfReposeRight),
				Roughness = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.RoughnessRight),
				SpecificGravity = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.SpecificGravityRight),
				ShieldsParameter = results.ReadDoubleValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.ShieldsParameterRight),
				Equation = results.ReadEnumValue(HFileResultsDatasets.StableChannelDesign.TractiveForceMethod.RightSideSlopeEquation, BankEquation.Strickler)
			}
		};
		stopwatch.Stop();
		_logger.LogInformation("Migrated stable channel design data in {time} ms", stopwatch.ElapsedMilliseconds);
		return stableChannelDesign;
	}

	public static void ConvertBridgeScour()
	{
	}

	public static void ConvertRipRap()
	{
	}

	public static void ConvertRatingCurveCalculator()
	{
	}

	public static void ConvertUniformFlow()
	{
	}

	public static void ConvertSIAM()
	{
	}
}
