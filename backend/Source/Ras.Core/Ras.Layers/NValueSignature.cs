using System;
using System.Diagnostics;
using Geospatial.IO.ORM;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Files;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers;

[DebuggerDisplay("{Name,nq}: ({Mode,nq})")]
public class NValueSignature
{
	[Metadata("Name")]
	public string Name { get; set; }

	[Metadata("Mode")]
	public NValueSource Mode { get; set; }

	[Metadata("Constant Value")]
	public float ConstantValue { get; set; }

	[Metadata("Classification Layer Name")]
	public string ClassificationLayerName { get; set; }

	[Metadata("Interpretation Table Hash")]
	public int InterpretationTableHash { get; set; }

	[ComplexMetadata(Optional = true)]
	public FileSignature FileSignature { get; set; }

	public ClassificationSignature ClassificationSignature { get; set; }

	public NValueSignature()
	{
		FileSignature = new FileSignature();
	}

	public OperationResult Compare(NValueSignature other, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		OperationResult operationResult = true;
		if (Mode != other.Mode)
		{
			operationResult.AddFailure(new Message("N-Value mode changed: {ThisMode} -> {ThatMode}", Mode, other.Mode));
			return operationResult;
		}
		pr.ReportMessage("N-Value mode matches: {ThisMode}", LogLevel.Debug, Mode);
		switch (Mode)
		{
		case NValueSource.Constant:
			if (ConstantValue != other.ConstantValue)
			{
				operationResult.AddFailure(new Message("Constant N-Value changed: {ThisValue} -> {ThatValue}", ConstantValue, other.ConstantValue));
			}
			break;
		case NValueSource.ClassificationLayer:
		{
			if (ClassificationLayerName != other.ClassificationLayerName)
			{
				operationResult.AddFailure(new Message("Classification layer association changed: {ThisLayer} -> {ThatLayer}", ClassificationLayerName ?? "(none)", other.ClassificationLayerName ?? "(none)"));
			}
			if (InterpretationTableHash != other.InterpretationTableHash)
			{
				operationResult.AddFailure(new Message("N-Value interpretation table changed (hash mismatch: {ThisHash} vs {ThatHash})", InterpretationTableHash, other.InterpretationTableHash));
			}
			else
			{
				pr.ReportMessage("N-Value interpretation table hashes match ({ThisHash}).", LogLevel.Debug, InterpretationTableHash);
			}
			if (ClassificationSignature == null && other.ClassificationSignature == null)
			{
				pr.ReportMessage("Classification Signature null for source and target.", LogLevel.Debug);
				break;
			}
			if (ClassificationSignature == null)
			{
				operationResult.AddFailure("Source Classification Signature is null.");
				break;
			}
			if (other.ClassificationSignature == null)
			{
				operationResult.AddFailure("Target Classification Signature is null.");
				break;
			}
			OperationResult operationResult3 = ClassificationSignature.Compare(other.ClassificationSignature, pr);
			if (!operationResult3)
			{
				operationResult.AddFailure(operationResult3);
			}
			break;
		}
		case NValueSource.ImportedRaster:
			if (FileSignature != null && other.FileSignature != null)
			{
				OperationResult operationResult2 = FileSignature.Compare(other.FileSignature, FileCompareOptions.Hash, pr);
				if (!operationResult2)
				{
					operationResult.AddFailure(operationResult2);
				}
			}
			else if (FileSignature == null && other.FileSignature != null)
			{
				operationResult.AddFailure(new Message("N-Value raster file was added."));
			}
			else if (FileSignature != null && other.FileSignature == null)
			{
				operationResult.AddFailure(new Message("N-Value raster file was removed."));
			}
			break;
		default:
			throw new NotImplementedException($"NValueSource.{Mode} comparison not implemented");
		}
		return operationResult;
	}

	public static bool TryLoad(H5Reader hr, string group, out NValueSignature signature)
	{
		signature = new NValueSignature();
		if (!GeospatialH5Metadata.Read(signature, hr, group))
		{
			signature = null;
			return false;
		}
		ClassificationSignature.TryLoad(hr, group + "/Classifications", out var signature2);
		signature.ClassificationSignature = signature2;
		return true;
	}

	public void Save(H5Writer hw, string group)
	{
		hw.Delete(group);
		GeospatialH5Metadata.Write(this, hw, group);
		ClassificationSignature?.Save(hw, group + "/Classifications");
	}
}
