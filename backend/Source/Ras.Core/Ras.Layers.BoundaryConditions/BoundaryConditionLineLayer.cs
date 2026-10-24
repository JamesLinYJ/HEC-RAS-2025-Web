using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Geospatial.IO.ORM;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Layers.Payloads;
using Utility;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

[DebuggerDisplay("{Features}")]
public class BoundaryConditionLineLayer : BoundaryConditionFeatureLayerBase<BoundaryConditionLine>
{
	public const string HDFGroupName = "/Boundary Conditions/BC Lines";

	private const string StageFlowHydroDataset = "/Boundary Conditions/BC Lines/Stage-Flow Hydrograph";

	private const int LatestDiskVersion = 1;

	private IPolylineCollection _plWrapper;

	protected override string HDF5Group => "/Boundary Conditions/BC Lines";

	[Metadata(KeyName = "Disk Version", Optional = true, DefaultValue = -1, SetValueToDefaultIfKeyMissing = true)]
	public int DiskVersion { get; set; } = -1;

	internal BoundaryConditionLineLayer(BoundaryCondition bc)
		: base(bc)
	{
	}

	protected override List<BoundaryConditionLine> LoadFeaturesInternal()
	{
		if (IsDataLoaded)
		{
			BoundaryConditionFeatureLayerBase<BoundaryConditionLine>._logger.LogWarning("Why is the load event being called twice on BC lines?");
			return _loadedFeatures;
		}
		if (BoundaryCondition.InstantiationMode == InstantiationMode.InMemory || BoundaryCondition.InstantiationMode == InstantiationMode.CreateNewFile)
		{
			_loadedFeatures = new List<BoundaryConditionLine>();
			return _loadedFeatures;
		}
		switch (TryGetFileVersion())
		{
		case 0:
			DiskVersion = 0;
			BoundaryCondition.CheckInitializeData(ref _loadedFeatures, (H5Reader hr) => (from e in FeatureIO.Read<BCLEntity_V0>(hr, "/Boundary Conditions/BC Lines")
				select e.ToBCLine()).ToList());
			break;
		case 1:
			BoundaryCondition.CheckInitializeData(ref _loadedFeatures, (H5Reader hr) => BoundaryConditionLineSerializer.Load(hr, "/Boundary Conditions/BC Lines"));
			break;
		default:
			BoundaryConditionFeatureLayerBase<BoundaryConditionLine>._logger.LogError("Error loading BCL features. Could not determine version.");
			_loadedFeatures = new List<BoundaryConditionLine>();
			return _loadedFeatures;
		}
		if (_loadedFeatures != null)
		{
			for (int intervalIndex = 0; intervalIndex < _loadedFeatures.Count; intervalIndex++)
			{
				_loadedFeatures[intervalIndex].InitializeBoundaryCondition(BoundaryCondition);
			}
		}
		return _loadedFeatures;
	}

	protected override void PreSaveInternal(H5Writer hw)
	{
		DiskVersion = 1;
	}

	protected override void SaveFeaturesInternal(H5Writer hw, string hdfGroup, List<BoundaryConditionLine> features)
	{
		ClearOldDatasets(hw);
		BoundaryConditionLineSerializer.From(Features).Save(hw, hdfGroup);
	}

	public int TryGetFileVersion()
	{
		try
		{
			if (BoundaryCondition == null)
			{
				return -1;
			}
			if (!BoundaryCondition.IsInMemory && !File.Exists(BoundaryCondition.Filename))
			{
				return -1;
			}
			return BoundaryCondition.UseReader((H5Reader hr) =>
			{
				if (hr.ExistsAsGroup(HDF5Group + "/Payloads"))
				{
					return 1;
				}
				return (!hr.ExistsAsDataset("/Boundary Conditions/BC Lines/Stage-Flow Hydrograph")) ? 1 : 0;
			});
		}
		catch (Exception exception)
		{
			BoundaryConditionFeatureLayerBase<BoundaryConditionLine>._logger.LogWarning(exception, "Could not open/read BC file to determine file version.");
			return -1;
		}
	}

	private void ClearOldDatasets(H5Writer hw)
	{
		hw.Delete("/Boundary Conditions/BC Lines/Stage-Flow Hydrograph");
	}

	public BoundaryConditionLine Add(Polyline pl)
	{
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(BoundaryCondition, pl);
		string name = StringHelpers.NextUniqueName(Features, (BoundaryConditionLine ft) => ft.Name, "Line");
		boundaryConditionLine.Name = name;
		Add(boundaryConditionLine);
		return boundaryConditionLine;
	}

	public BoundaryConditionLine Add(Segment seg)
	{
		return Add(seg.AsPolyline());
	}

	public BoundaryConditionLine Add(Polyline pl, PayloadBase payload)
	{
		BoundaryConditionLine boundaryConditionLine = Add(pl);
		boundaryConditionLine.Add(payload);
		return boundaryConditionLine;
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineFtLayerWrapped<BoundaryConditionLine>(this, (BoundaryConditionLine riv) => riv.Polyline, (Polyline pl) => new BoundaryConditionLine
			{
				Polyline = pl,
				Name = "Boundary Condition Line"
			});
		}
		return _plWrapper;
	}

	public OperationResult DataEquals(BoundaryConditionLineLayer other)
	{
		OperationResult operationResult = true;
		if (Features.Count != other.Features.Count)
		{
			operationResult.AddFailure("Different number of features.");
			return operationResult;
		}
		for (int i = 0; i < Features.Count; i++)
		{
			OperationResult operationResult2 = Features[i].DataEquals(other.Features[i]);
			if (!operationResult2)
			{
				operationResult.AddFailure("BC Line[" + i + "] did not match expected value.");
				operationResult.AddFailure(operationResult2);
			}
		}
		return operationResult;
	}
}
