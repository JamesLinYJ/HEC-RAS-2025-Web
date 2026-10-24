using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Utility.Files;
using Utility.Logging;

namespace Ras.Layers;

internal static class ProjectConnectionMigration
{
	internal static OperationResult Migrate(Project source, Project destination, IEnumerable<string> required,
		Project.MigrateExternalConnectionBehavior behavior)
	{
		int ordinal = 0;
		foreach (string name in required.Distinct(StringComparer.Ordinal))
		{
			if (!source.ConnectionSource.TryFindConnection(name, out ExternalConnectionBase connection))
				return OperationResult.Fail("Required external connection was not found: " + name);
			OperationResult loaded = ExternalConnectionBase.TryLoadFromXML(destination.ProjectDirectory, connection.SaveXML(), out ExternalConnectionBase clone);
			if (!loaded) return loaded;
			string root = Path.Combine(destination.ProjectDirectory, "External Connections", (ordinal++).ToString("D4", CultureInfo.InvariantCulture));
			if (connection is FileExternalConnection file && clone is FileExternalConnection copiedFile)
			{
				OperationResult resolved = file.TryGetFile(out string filename);
				if (!resolved) return resolved;
				if (behavior == Project.MigrateExternalConnectionBehavior.CopyToProject)
				{
					Directory.CreateDirectory(root);
					string target = Path.Combine(root, Path.GetFileName(filename));
					File.Copy(filename, target, overwrite: false);
					copiedFile.SetConnectedPathname(target); copiedFile.AbsolutePath = "";
				}
				else { copiedFile.AbsolutePath = Path.GetFullPath(filename); copiedFile.RelativePath = ""; }
			}
			else if (connection is DirectoryExternalConnection directory && clone is DirectoryExternalConnection copiedDirectory)
			{
				OperationResult resolved = directory.TryGetDirectory(out string directoryPath);
				if (!resolved) return resolved;
				if (behavior == Project.MigrateExternalConnectionBehavior.CopyToProject)
				{
					OperationResult listed = directory.TryGetFiles(out string[] files);
					if (!listed) return listed;
					Directory.CreateDirectory(root);
					foreach (string filename in files)
					{
						string target = Path.Combine(root, Path.GetRelativePath(directoryPath, filename));
						Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(filename, target, overwrite: false);
					}
					copiedDirectory.SetConnectedPathname(root); copiedDirectory.AbsolutePath = "";
				}
				else { copiedDirectory.AbsolutePath = directoryPath; copiedDirectory.RelativePath = ""; }
			}
			else return OperationResult.Fail("Unsupported external connection type: " + name);
			destination.ConnectionSource.Connections.Add(clone);
		}
		return true;
	}
}
