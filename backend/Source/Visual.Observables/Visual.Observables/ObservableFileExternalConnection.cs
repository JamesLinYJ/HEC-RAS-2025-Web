using System;
using Utility.Files;

namespace Visual.Observables;

public class ObservableFileExternalConnection : ObservableExternalConnection
{
	private readonly FileExternalConnection _connection;

	public new static Type Type => typeof(ObservableFileExternalConnection);

	public static Type TType => typeof(ObservableFileExternalConnection);

	public FileExternalConnection FileConnection => _connection;

	public string PathRelativeSourceDirectory => _connection.PathRelativeSourceDirectory;

	public string RelativePath => _connection.RelativePath;

	public string AbsolutePath => _connection.AbsolutePath;

	public ObservableFileExternalConnection(FileExternalConnection connection)
		: base(connection)
	{
		_connection = connection;
	}
}
