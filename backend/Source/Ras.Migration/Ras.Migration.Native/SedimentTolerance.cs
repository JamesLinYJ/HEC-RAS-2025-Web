using System.Collections.Generic;

namespace Ras.Migration.Native;

public class SedimentTolerance
{
	public int SortingAndArmoringIterations;

	public string XSUpdateThreshold;

	public string BedRoughnessPredictor;

	public List<string> RoughnessReach;

	public string HydraulicsUpdateThreshold;

	public string EnergySlopeMethod;

	public string VolumeChangeMethod;

	public string SedimentRetentionMethod;

	public string TsMultiplier;

	public string WarmupMethod;

	public float WarmupDuration;

	public float WarmupDurationConc;

	public float WarmupDurationGrad;

	public float WarmupDurationBath;

	public string XSWeightingMethod;

	public int NumUsWeightedXs;

	public int NumDsWeightedXs;

	public float UpstreamXsWeight;

	public float MainXsWeight;

	public float DownstreamXsWeight;

	public int NumDsXsWeightedWIthUsBd;

	public string UsBdWeight;

	public string WeightXsAssociatedWithUsBd;

	public string PercentileMethod;

	public string SedOutputLevel;

	public List<string> SedOutputVariables = new List<string>();

	public string MassOrVolumeOutput;

	public string OutputIncrementType;

	public string ProfileANdTsOutputIncr;

	public string TransportOutputIncr;

	public string XsOutputFlag;

	public string XsOutputIncr;

	public string ReadHDF5SedHotstart;

	public string SedimentHotstartType;

	public string SedimentHotstartFile;

	public string SedimentHotstartDate;

	public string SedimentHotstartTime;

	public string WriteGradationFile;

	public string ReadGradationHotstart;

	public string GradationFilename;

	public string WriteHDF5File;

	public string WriteBinaryOutput;

	public string WriteDssSedimentFile;

	public string DssSedimentOutputType;

	public List<string> DssLocations = new List<string>();

	public List<string> SummaryReach = new List<string>();

	public string SvCurve;

	public string SvUpstreamXs;

	public string SvDownstreamXs;

	public List<string> SvDate = new List<string>();

	public List<string> SvElevation = new List<string>();

	public string SpecificGageFlag;

	public string SpecificGageFile;

	public List<string> SpecificGageDates = new List<string>();

	public List<string> SpecificGageTimes = new List<string>();

	public string SubcellErosionMethods;

	public string SubcellDepositionMethods;

	public string AdvectionScheme;

	public string MatrixSolver;

	public string ImplicitWeightingFactor;

	public string MaxOuterLoopConvIters;

	public string ConvMaxAbs;

	public string ConvRMSE;

	public string GrainFracMaxAbsErr;

	public string MaxSubgridRgns;

	public string MaxSubgridLengthScale;

	public string INitialLayerThickness;

	public string MinLayerThickness;

	public string MaxLayerThickness;

	public int NumLayers;
}
