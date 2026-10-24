using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Files;

public class ExternalConnectionCollection
{
	protected static readonly ILogger _logger = LogHelper.GetLogger("ExternalConnectionCollection");

	public int Count => Connections.Count;

	public List<ExternalConnectionBase> Connections { get; private set; }

	public ExternalConnectionBase this[int index]
	{
		get
		{
			return Connections[index];
		}
		set
		{
			Connections[index] = value;
		}
	}

	public string PathRelativeBase { get; private set; }

	public ExternalConnectionCollection(string pathRelativeBase = "")
	{
		Connections = new List<ExternalConnectionBase>();
		SetPathRelativeBase(pathRelativeBase);
	}

	public void SetPathRelativeBase(string pathRelativeBase)
	{
		if (string.IsNullOrEmpty(pathRelativeBase))
		{
			PathRelativeBase = "";
		}
		else if (File.Exists(pathRelativeBase))
		{
			PathRelativeBase = Path.GetDirectoryName(pathRelativeBase);
		}
		else
		{
			PathRelativeBase = pathRelativeBase;
		}
	}

	public void Remove(ExternalConnectionBase con)
	{
		Connections.Remove(con);
	}

	public bool TryFindConnection(string connectionName, out ExternalConnectionBase connection)
	{
		connection = Connections.FirstOrDefault((ExternalConnectionBase c) => c.Name == connectionName);
		return connection != null;
	}

	public bool TryRenameConnection(string initialName, string newName)
	{
		if (!TryFindConnection(initialName, out var connection))
		{
			return false;
		}
		connection.Name = newName;
		return true;
	}

	public FileExternalConnection AddFileConnection(string name, string filename)
	{
		name = UniqueFileConnectionName(name);
		if (string.IsNullOrEmpty(PathRelativeBase))
		{
			FileExternalConnection fileExternalConnection = FileExternalConnection.CreateAbsoluteFileConnection(name, filename, PathRelativeBase);
			Connections.Add(fileExternalConnection);
			return fileExternalConnection;
		}
		FileExternalConnection fileExternalConnection2 = FileExternalConnection.CreateAbsoluteFileConnection(name, filename, PathRelativeBase);
		Connections.Add(fileExternalConnection2);
		return fileExternalConnection2;
	}

	public string UniqueFileConnectionName(string desiredName)
	{
		int ordinal = 0;
		string desiredName2 = desiredName;
		while (ordinal < 1000)
		{
			bool condition = true;
			for (int i = 0; i < Count; i++)
			{
				if (desiredName2 == Connections[i].Name)
				{
					ordinal++;
					desiredName2 = desiredName + " (" + ordinal + ")";
					condition = false;
					break;
				}
			}
			if (condition)
			{
				return desiredName2;
			}
		}
		throw new Exception("Exceeded reasonable connection count.");
	}

	public void TryReconcileAll()
	{
		foreach (ExternalConnectionBase connection in Connections)
		{
			if (connection is PathExternalConnection pathExternalConnection)
			{
				try
				{
					pathExternalConnection.TryReconcilePaths();
				}
				catch (Exception exception)
				{
					_logger.LogError(exception, "Failed to reconcile paths for external connection \"{Name}\".", connection.Name);
				}
			}
		}
	}

	public void SaveXML(XElement root)
	{
		foreach (ExternalConnectionBase connection in Connections)
		{
			root.Add(connection.SaveXML());
		}
	}

	protected static IEnumerable<XElement> GetChildren(XElement x, string key)
	{
		return from n in x.Descendants()
			where n.Name.ToString() == key
			select n;
	}

	public void LoadXML(XElement root)
	{
		foreach (XElement child in GetChildren(root, ExternalConnection.XmlElementName))
		{
			if ((bool)ExternalConnectionBase.TryLoadFromXML(PathRelativeBase, child, out var connection))
			{
				Connections.Add(connection);
			}
		}
	}
}
