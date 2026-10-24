using System.Collections.Generic;
using System.IO;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Extensions;

namespace Ras.Layers.Classic;

public class ClassicGeometry : CoreLayerBase
{
	public const string Extension = ".h5";

	public const string ExtensionSearchPattern = "*.h5";

	public const string HDF5Group = "/Geometry";

	public static readonly string FileVersion = Project.DevFileVersion;

	public static readonly char[] IllegalGeometryNameCharacters = Path.GetInvalidFileNameChars();

	public bool IsResult => false;

	private List<IClassicGeometryLayer> GeometryLayers { get; } = new List<IClassicGeometryLayer>();

	public RiverLayer RiverLayer { get; private set; }

	public CrossSectionLayer CrossSectionLayer { get; private set; }

	public BanklineLayer BanklineLayer { get; private set; }

	public FlowPathLayer FlowPathLayer { get; private set; }

	public RiverEdgelineLayer RiverEdgeLineLayer { get; private set; }

	public StorageAreaLayer StorageAreaLayer { get; private set; }

	public ClassicFlowAreaLayer FlowAreaLayer { get; private set; }

	public BreaklineLayer BreaklineLayer { get; private set; }

	public ClassicStructureLayer StructureLayer { get; private set; }

	public GateGroupLayer GateGroupLayer { get; private set; }

	public GateOpeningLayer GateOpeningLayer { get; private set; }

	public InterpolationSurfaceLayer InterpolationSurfaceLayer { get; private set; }

	protected override string GetHDF5Group()
	{
		return "/Geometry";
	}

	public override string GetCurrentFileFormatVersion()
	{
		return FileVersion;
	}

	public static string MakeFilename(string projectDir, string geomName)
	{
		return Project.MakeFilename<Geometry>(projectDir, geomName, ".h5");
	}

	public static ClassicGeometry CreateInMemory(string name = "")
	{
		return new ClassicGeometry(InstantiationMode.InMemory)
		{
			_name = name
		};
	}

	private ClassicGeometry(InstantiationMode mode)
		: base(mode)
	{
		ConstructHydraulicLayers();
	}

	public ClassicGeometry(string filename, Project project = null)
		: base(filename, project)
	{
	}

	protected override void PreInitializeDataInternal()
	{
		ConstructHydraulicLayers();
	}

	private void ConstructHydraulicLayers()
	{
		List<IClassicGeometryLayer> geometryLayers = GeometryLayers;
		RiverLayer riverLayer = (RiverLayer = new RiverLayer(this));
		geometryLayers.Add(riverLayer);
		List<IClassicGeometryLayer> geometryLayers2 = GeometryLayers;
		CrossSectionLayer crossSectionLayer = (CrossSectionLayer = new CrossSectionLayer(this));
		geometryLayers2.Add(crossSectionLayer);
		List<IClassicGeometryLayer> geometryLayers3 = GeometryLayers;
		InterpolationSurfaceLayer interpolationSurfaceLayer = (InterpolationSurfaceLayer = new InterpolationSurfaceLayer(this));
		geometryLayers3.Add(interpolationSurfaceLayer);
		List<IClassicGeometryLayer> geometryLayers4 = GeometryLayers;
		BanklineLayer banklineLayer = (BanklineLayer = new BanklineLayer(this));
		geometryLayers4.Add(banklineLayer);
		List<IClassicGeometryLayer> geometryLayers5 = GeometryLayers;
		FlowPathLayer flowPathLayer = (FlowPathLayer = new FlowPathLayer(this));
		geometryLayers5.Add(flowPathLayer);
		List<IClassicGeometryLayer> geometryLayers6 = GeometryLayers;
		RiverEdgelineLayer riverEdgelineLayer = (RiverEdgeLineLayer = new RiverEdgelineLayer(this));
		geometryLayers6.Add(riverEdgelineLayer);
		List<IClassicGeometryLayer> geometryLayers7 = GeometryLayers;
		StorageAreaLayer storageAreaLayer = (StorageAreaLayer = new StorageAreaLayer(this));
		geometryLayers7.Add(storageAreaLayer);
		List<IClassicGeometryLayer> geometryLayers8 = GeometryLayers;
		ClassicFlowAreaLayer classicFlowAreaLayer = (FlowAreaLayer = new ClassicFlowAreaLayer(this));
		geometryLayers8.Add(classicFlowAreaLayer);
		List<IClassicGeometryLayer> geometryLayers9 = GeometryLayers;
		BreaklineLayer breaklineLayer = (BreaklineLayer = new BreaklineLayer(this));
		geometryLayers9.Add(breaklineLayer);
		List<IClassicGeometryLayer> geometryLayers10 = GeometryLayers;
		ClassicStructureLayer classicStructureLayer = (StructureLayer = new ClassicStructureLayer(this));
		geometryLayers10.Add(classicStructureLayer);
		List<IClassicGeometryLayer> geometryLayers11 = GeometryLayers;
		GateGroupLayer gateGroupLayer = (GateGroupLayer = new GateGroupLayer(this));
		geometryLayers11.Add(gateGroupLayer);
		List<IClassicGeometryLayer> geometryLayers12 = GeometryLayers;
		GateOpeningLayer gateOpeningLayer = (GateOpeningLayer = new GateOpeningLayer(this));
		geometryLayers12.Add(gateOpeningLayer);
	}

	protected override void LoadInternal(H5Reader hr)
	{
		foreach (IClassicGeometryLayer geometryLayer in GeometryLayers)
		{
			geometryLayer.LoadMetadata(hr);
		}
	}

	protected override bool NeedsSaveInternal()
	{
		foreach (IClassicGeometryLayer geometryLayer in GeometryLayers)
		{
			if (geometryLayer.NeedsSave())
			{
				CoreLayerBase._logger.LogDebug("{GeometryName}: {LayerName} needs to be saved (short-circuiting).", Name, geometryLayer.GetType().Name);
				return true;
			}
		}
		return false;
	}

	protected override void SaveInternal(H5Writer hw)
	{
		bool roughlyEquals = hw.Filename.RoughlyEquals(Filename);
		foreach (IClassicGeometryLayer geometryLayer in GeometryLayers)
		{
			geometryLayer.Save(hw, !roughlyEquals);
		}
	}
}
