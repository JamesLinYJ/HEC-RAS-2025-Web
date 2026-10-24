using System;
using System.Collections.Generic;
using System.IO;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;

namespace Ras.Layers.BoundaryConditions;

public static class InitialConditionSource
{
	public static OperationResult TryResolveConnectionFile(Project project, string connectionName, out string filename)
	{
		filename = "";
		ExternalConnectionCollection externalConnectionCollection = project?.ConnectionSource;
		if (externalConnectionCollection == null)
		{
			return OperationResult.Fail(new Message("No project available to resolve external connection {Name}.", connectionName));
		}
		if (!externalConnectionCollection.TryFindConnection(connectionName, out var connection))
		{
			return OperationResult.Fail(new Message("Could not identify External Connection {Name}.", connectionName));
		}
		if (!(connection is IFileExternalConnection fileExternalConnection))
		{
			return OperationResult.Fail(new Message("External Connection {Name} is not a file-based Connection.", connectionName));
		}
		return fileExternalConnection.TryGetFile(out filename);
	}

	public static OperationResult TryOpenSourceResult(Project project, string filename, out Result source, out bool owned)
	{
		owned = false;
		source = null;
		string fullPath = Path.GetFullPath(filename);
		if (project != null)
		{
			foreach (Result result in project.Results)
			{
				if (!string.IsNullOrEmpty(result.Filename) && Path.GetFullPath(result.Filename).RoughlyEquals(fullPath))
				{
					source = result;
					return true;
				}
			}
		}
		OperationResult operationResult = Result.TryLoad(filename, out var res);
		if (!operationResult)
		{
			return operationResult;
		}
		res.Project = project;
		source = res;
		owned = true;
		return true;
	}

	public static bool TryResolveProfileIndex(IReadOnlyList<DateTime> times, string timestep, out int profileIndex)
	{
		profileIndex = -1;
		if (times == null || times.Count == 0)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(timestep) || !DateTime.TryParse(timestep, out var result))
		{
			profileIndex = times.Count - 1;
			return true;
		}
		for (int i = 0; i < times.Count; i++)
		{
			if (times[i] == result)
			{
				profileIndex = i;
				return true;
			}
		}
		long maxValue = long.MaxValue;
		for (int j = 0; j < times.Count; j++)
		{
			long absoluteTicks = Math.Abs((times[j] - result).Ticks);
			if (absoluteTicks < maxValue)
			{
				maxValue = absoluteTicks;
				profileIndex = j;
			}
		}
		return true;
	}
}
