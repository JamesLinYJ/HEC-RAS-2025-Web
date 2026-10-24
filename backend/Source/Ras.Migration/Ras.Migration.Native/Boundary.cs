using System.Collections.Generic;
using System.Diagnostics;

namespace Ras.Migration.Native;

[DebuggerDisplay("{Location.ToString()}")]
public class Boundary
{
	public BoundaryLocation Location;

	public string Description;

	public string Name;

	public string TimeInterval;

	public HydrographType HydrographType;

	public float[] Stage;

	public float[] Flow;

	public bool StageHydrographUseInitialStage;

	public bool StageHydrographTailwaterCheck;

	public float FlowHydroInitialWS;

	public float FlowHydroQMin;

	public float FlowHydroQMult;

	public float FlowHydroSlope;

	public string HydroDssFile;

	public string HydroDssPath;

	public string HydroDssPath2;

	public bool HydroUseDss;

	public bool HydroUseAbsTime;

	public string HydroFixedStartDt;

	public bool HydroIsCritBoundary;

	public float HydroCritBoundaryFlow;

	public float FrictionSlope;

	public bool SingleWsOnBCLine;

	public List<BoundaryGate> Gates;

	public List<ElevControlledGate> ElevControlledGates;

	public NavigationDam NavigationDam;
}
