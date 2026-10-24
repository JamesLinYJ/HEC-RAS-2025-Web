using System;
using Utility.Files;

namespace Visual.Observables;

public class ObservableDirectoryExternalConnection : ObservableExternalConnection
{
	private readonly DirectoryExternalConnection _connection;

	public new static Type Type => typeof(ObservableDirectoryExternalConnection);

	public DirectoryExternalConnection DirectoryConnection => _connection;

	public string PathRelativeSourceDirectory => _connection.PathRelativeSourceDirectory;

	public string RelativePath => _connection.RelativePath;

	public string AbsolutePath => _connection.AbsolutePath;

	public ObservableDirectoryExternalConnection(DirectoryExternalConnection connection)
		: base(connection)
	{
		_connection = connection;
	}
}
