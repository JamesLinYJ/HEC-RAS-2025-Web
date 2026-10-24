using System;
using Geospatial.Vectors;
using Ras.Layers.Payloads;
using Utility.Files;
using Utility.Logging;
using Visual.Observables;

namespace Ras.Observables;

public class ObservableExternalPayload : ObservablePayload
{
	private readonly ObservableProject _obsProj;

	private ObservableExternalConnection _dataConnection;

	public static Type Type => typeof(ObservableExternalPayload);

	public override ExternalPayload Payload => _payload as ExternalPayload;

	public string ExternalConnection
	{
		get
		{
			return Payload.ExternalConnection;
		}
		set
		{
			SetProperty(Payload.ExternalConnection, value, Payload, (ExternalPayload r, string v) =>
			{
				r.ExternalConnection = v;
			}, "ExternalConnection");
		}
	}

	public string ExternalPath
	{
		get
		{
			return Payload.ExternalPath;
		}
		set
		{
			SetProperty(Payload.ExternalPath, value, Payload, (ExternalPayload r, string v) =>
			{
				r.ExternalPath = v;
			}, "ExternalPath");
		}
	}

	public ObservableExternalConnection DataConnection
	{
		get
		{
			if (!_obsProj.Project.ConnectionSource.TryFindConnection(ExternalConnection, out var connection))
			{
				_dataConnection = null;
				return null;
			}
			if (_dataConnection == null && connection != null)
			{
				_dataConnection = _obsProj.Connections.FindConnection(connection);
			}
			return _dataConnection;
		}
		set
		{
			if (value == null)
			{
				Payload.SetDataConnection(null);
				SetProperty(ref _dataConnection, null, "DataConnection");
			}
			else
			{
				Payload.SetDataConnection(value.Connection);
				SetProperty(ref _dataConnection, value, "DataConnection");
			}
		}
	}

	public override string FriendlyName => "External Connection";

	public ObservableExternalPayload(ObservableProject obsProj, ExternalPayload payload)
		: base(payload)
	{
		_obsProj = obsProj;
	}

	public OperationResult TryEvaluate(PointCollection spatialContext, out ObservablePayload payload)
	{
		ExternalConnectionCollection connectionSource = _obsProj.Project.ConnectionSource;
		OperationResult operationResult = Payload.TryEvaluate(connectionSource, spatialContext, out var payload2);
		if (!operationResult.Result)
		{
			payload = null;
			return operationResult;
		}
		payload = ObservablePayload.From(_obsProj, payload2);
		return operationResult;
	}
}
