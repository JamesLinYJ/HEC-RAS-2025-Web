using System;
using Geospatial.IO.ORM;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Files;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers;

public class MeshPropertyTableSignature
{
	public static readonly string Version = "0.1.0 (Nov 2025)";

	[Metadata("Version")]
	public string SignatureVersion { get; set; }

	[Metadata("Cell Count")]
	public int MeshCellCount { get; set; }

	[Metadata("Face Count")]
	public int MeshFaceCount { get; set; }

	[Metadata("Mesh Hash")]
	public int MeshHash { get; set; }

	[Metadata("Date Last Modified")]
	public DateTime DateLastModified { get; set; } = DateTime.MinValue;

	[Metadata("Software Version")]
	public string SoftwareVersion { get; set; } = string.Empty;

	[Metadata("Compute Duration")]
	public double ComputeDurationSeconds { get; set; } = double.NaN;

	public TerrainSignature TerrainSignature { get; set; }

	public NValueSignature NValueSignature { get; set; }

	public MeshPropertyTableSignature()
	{
		SignatureVersion = Version;
	}

	public bool IsLoaded()
	{
		if (MeshCellCount > 0)
		{
			return MeshFaceCount > 0;
		}
		return false;
	}

	public OperationResult Compare(MeshPropertyTableSignature other, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		OperationResult operationResult = true;
		if (SignatureVersion != other.SignatureVersion)
		{
			operationResult.AddFailureAndReport($"Signature version mismatch (source={SignatureVersion}, target={other.SignatureVersion}).", pr);
		}
		else
		{
			pr.ReportMessage("Signature versions match ({Version}).", LogLevel.Debug, SignatureVersion);
		}
		if (MeshCellCount != other.MeshCellCount || MeshFaceCount != other.MeshFaceCount || MeshHash != other.MeshHash)
		{
			operationResult.AddFailureAndReport("Mesh changed.", pr);
		}
		else
		{
			pr.ReportMessage("Mesh properties match (CellCount={CellCount}, FaceCount={FaceCount}, Hash={Hash}).", LogLevel.Debug, MeshCellCount, MeshFaceCount, MeshHash);
		}
		if (TerrainSignature == null)
		{
			operationResult.AddFailureAndReport("Source missing terrain layer signature.", pr);
		}
		else if (other.TerrainSignature == null)
		{
			operationResult.AddFailureAndReport("Target missing terrain layer signature.", pr);
		}
		else
		{
			OperationResult operationResult2 = TerrainSignature.Compare(other.TerrainSignature, pr);
			if (!operationResult2)
			{
				pr.ReportMessage("Terrain Signature mismatch.");
				operationResult.AddFailureAndReport(operationResult2, pr);
				if (operationResult2.ContainsMessage(FileSignature.MessageCodes.NotLoaded))
				{
					pr.ReportMessage("File Signatures not loaded - this can indicate you're using an old or corrupted Terrain file. Rebuilding your Terrain may fix this.");
				}
			}
			else
			{
				pr.ReportMessage("Terrain Signatures match.");
			}
		}
		if (NValueSignature == null)
		{
			operationResult.AddFailureAndReport("Source missing n-value layer signature.", pr);
		}
		else if (other.NValueSignature == null)
		{
			operationResult.AddFailureAndReport("Target missing n-value layer signature.", pr);
		}
		else
		{
			OperationResult operationResult3 = NValueSignature.Compare(other.NValueSignature, pr);
			if (!operationResult3)
			{
				pr.ReportMessage("NValue Signature mismatch.");
				operationResult.AddFailureAndReport(operationResult3, pr);
			}
			else
			{
				pr.ReportMessage("NValue Signatures match.");
			}
		}
		return operationResult;
	}

	public OperationResult CompareFundamental(MeshPropertyTableSignature other, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		OperationResult operationResult = true;
		if (MeshCellCount != other.MeshCellCount)
		{
			operationResult.AddFailureAndReport($"Cell count mismatch (stored={MeshCellCount}, current={other.MeshCellCount}).", pr);
		}
		if (MeshFaceCount != other.MeshFaceCount)
		{
			operationResult.AddFailureAndReport($"Face count mismatch (stored={MeshFaceCount}, current={other.MeshFaceCount}).", pr);
		}
		if ((bool)operationResult)
		{
			pr.ReportMessage("Pinned tables validated (CellCount={CellCount}, FaceCount={FaceCount}).", LogLevel.Debug, MeshCellCount, MeshFaceCount);
		}
		return operationResult;
	}

	public void Save(H5Writer hw, string group)
	{
		hw.Delete(group);
		GeospatialH5Metadata.Write(this, hw, group);
		TerrainSignature?.Save(hw, group + "/Terrain");
		NValueSignature?.Save(hw, group + "/NValues");
	}

	public static bool TryLoad(H5Reader hr, string group, out MeshPropertyTableSignature signature)
	{
		signature = new MeshPropertyTableSignature();
		if (!GeospatialH5Metadata.Read(signature, hr, group))
		{
			signature = null;
			return false;
		}
		TerrainSignature.TryLoad(hr, group + "/Terrain", out var signature2);
		signature.TerrainSignature = signature2;
		NValueSignature.TryLoad(hr, group + "/NValues", out var signature3);
		signature.NValueSignature = signature3;
		return true;
	}
}
