using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Utility.DataStructures;
using Utility.Files;

namespace Visual.Observables;

public class ObservableExternalConnectionCollection : ObservableValidator, IEnumerable<ObservableExternalConnection>, IEnumerable
{
	private readonly ExternalConnectionCollection _collection;

	private readonly AdvancedCollection<ObservableExternalConnection> _connections;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<IReadOnlyList<string>>? addFileConnectionCommand;

	public IReadOnlyAdvancedCollection<ObservableExternalConnection> Connections => _connections;

	public ObservableExternalConnection this[int index] => Connections[index];

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<IReadOnlyList<string>> AddFileConnectionCommand => addFileConnectionCommand ?? (addFileConnectionCommand = new RelayCommand<IReadOnlyList<string>>(AddFileConnection));

	public ObservableExternalConnectionCollection(ExternalConnectionCollection collection)
	{
		_collection = collection;
		_connections = new AdvancedCollection<ObservableExternalConnection>(collection.Connections.Select((ExternalConnectionBase c) => ObservableExternalConnection.From(c)).ToList().ToList());
	}

	public ObservableExternalConnection FindConnection(ExternalConnectionBase connection)
	{
		return Connections.FirstOrDefault((ObservableExternalConnection c) => c.Connection.Equals(connection));
	}

	public bool ContainsName(string connectionName)
	{
		ExternalConnectionBase connection;
		return _collection.TryFindConnection(connectionName, out connection);
	}

	public ObservableFileExternalConnection AddFileConnection(string fileName, string name)
	{
		ObservableFileExternalConnection observableFileExternalConnection = new ObservableFileExternalConnection(_collection.AddFileConnection(name, fileName));
		_connections.Add(observableFileExternalConnection);
		return observableFileExternalConnection;
	}

	public void RemoveConnection(ObservableExternalConnection connection)
	{
		_connections.Remove(connection);
		_collection.Remove(connection.Connection);
	}

	public void AddFileConnection(IReadOnlyList<string> fileNames)
	{
		foreach (string fileName in fileNames)
		{
			AddFileConnection(fileName, Path.GetFileName(fileName));
		}
	}

	public IEnumerator<ObservableExternalConnection> GetEnumerator()
	{
		return Connections.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable)Connections).GetEnumerator();
	}
}
