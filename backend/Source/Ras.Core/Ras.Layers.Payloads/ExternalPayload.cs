using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Hec.Dss;
using Utility.Extensions;
using Utility.Files;
using Utility.Hashing;
using Utility.Logging;

namespace Ras.Layers.Payloads;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class ExternalPayload : PayloadBase
{
	private string _externalConnection;

	private string _externalPath;

	private ExternalConnectionBase _dataConnection;

	[HashContributor]
	public string ExternalConnection
	{
		get
		{
			return _externalConnection;
		}
		set
		{
			if (!(_externalConnection == value))
			{
				_externalConnection = value;
				_dataConnection = null;
			}
		}
	}

	[HashContributor]
	public string ExternalPath
	{
		get
		{
			return _externalPath;
		}
		set
		{
			if (!(_externalPath == value))
			{
				_externalPath = value;
				_dataConnection = null;
			}
		}
	}

	public ExternalPayload(string variable, string externalConnection, string externalPath)
		: base(variable)
	{
		ExternalConnection = externalConnection;
		ExternalPath = externalPath;
	}

	public OperationResult TryEvaluate(ExternalConnectionCollection connectionSource, PointCollection spatialContext, out PayloadBase payload)
	{
		try
		{
			OperationResult operationResult = TryGetFileBasedConnection(connectionSource, out var file);
			if (!operationResult)
			{
				payload = null;
				return operationResult;
			}
			string extension = Path.GetExtension(file);
			if (extension.RoughlyEquals(".dss"))
			{
				OperationResult result = TryGetDSSTimeSeries(file, out var payload2);
				payload = payload2;
				return result;
			}
			if (extension.RoughlyEquals(".h5"))
			{
				OperationResult result2 = TryGetResultTimeSeries(file, spatialContext as Polyline, out var payload3);
				payload = payload3;
				return result2;
			}
			payload = null;
			return OperationResult.Fail(FileMessageCodes.GetFileIncorrectExtension(file, "{.dss, .h5}"));
		}
		catch (Exception ex)
		{
			payload = null;
			return OperationResult.Fail("Evaluating External Connection", ex);
		}
	}

	public void SetDataConnection(ExternalConnectionBase conn)
	{
		if (conn == null)
		{
			ExternalConnection = null;
		}
		else
		{
			ExternalConnection = conn.Name;
		}
		_dataConnection = conn;
	}

	private OperationResult TryGetFileBasedConnection(ExternalConnectionCollection collectionSource, out string file)
	{
		if (!collectionSource.TryFindConnection(ExternalConnection, out var connection) || connection == null)
		{
			file = "";
			return OperationResult.Fail(new Message("Could not identify External Connection {Name}.", ExternalConnection));
		}
		if (connection is IFileExternalConnection fileExternalConnection)
		{
			return fileExternalConnection.TryGetFile(out file);
		}
		file = "";
		return OperationResult.Fail(new Message("Error with associated External Connection {Name}: Not a file-based Connection.", ExternalConnection));
	}

	private OperationResult TryGetDSSTimeSeries(string filename, out TimeSeriesPayload payload)
	{
		if (string.IsNullOrEmpty(ExternalPath))
		{
			payload = null;
			return OperationResult.Fail("No ExternalPath provided for the DSS file.");
		}
		using DssReader dssReader = new DssReader(filename);
		Hec.Dss.TimeSeries timeSeries = dssReader.GetTimeSeries(new DssPath(ExternalPath));
		if (timeSeries == null)
		{
			payload = null;
			return OperationResult.Fail(new Message("No TimeSeries found at {ExternalConnection} : {ExternalConnectionPath}", ExternalConnection, ExternalPath));
		}
		IrregularAbsoluteTimeSeriesDefinition tsd = new IrregularAbsoluteTimeSeriesDefinition(timeSeries.Times);
		payload = new TimeSeriesPayload(Variable, tsd, timeSeries.Values);
		return true;
	}

	private OperationResult TryGetResultTimeSeries(string filename, Polyline pline, out TimeSeriesPayload payload)
	{
		if (pline == null)
		{
			payload = null;
			return OperationResult.Fail("Spatial Context expected a polyline to query a Result TimeSeries.");
		}
		using Result result = new Result(filename);
		if (Variable == "Flow")
		{
			result.ComputeFlowTimeSeries(pline, out var flowTimeSeries);
			payload = new TimeSeriesPayload(Variable, new IrregularAbsoluteTimeSeriesDefinition(result.BaseOutputBlock.Times.ToArray()), flowTimeSeries);
			return true;
		}
		_ = Variable == "Stage";
		payload = null;
		return OperationResult.Fail(new Message("Variable {VariableName} not able to be sampled from a RAS Result."));
	}

	private string GetDebuggerDisplay()
	{
		return (Name ?? "(No Name)") + " " + Variable + ": " + ExternalConnection + "::" + ExternalPath;
	}
}
