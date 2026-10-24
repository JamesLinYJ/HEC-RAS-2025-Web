using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Files;

namespace Visual.Observables;

public abstract class ObservableExternalConnection : ObservableValidator
{
	private ExternalConnectionBase _connection;

	public static Type Type => typeof(ObservableExternalConnection);

	public ExternalConnectionBase Connection => _connection;

	public string Name
	{
		get
		{
			return _connection.Name;
		}
		set
		{
			SetProperty(_connection.Name, value, _connection, (ExternalConnectionBase u, string n) =>
			{
				u.Name = n;
			}, "Name");
		}
	}

	public string Description
	{
		get
		{
			return _connection.Description;
		}
		set
		{
			SetProperty(_connection.Description, value, _connection, (ExternalConnectionBase u, string n) =>
			{
				u.Description = n;
			}, "Description");
		}
	}

	protected ObservableExternalConnection(ExternalConnectionBase connection)
	{
		_connection = connection;
	}

	public void SetConnectedPathname(string pathname)
	{
		if (_connection is PathExternalConnection pathExternalConnection)
		{
			pathExternalConnection.SetConnectedPathname(pathname);
			OnPropertyChanged("AbsolutePath");
			OnPropertyChanged("RelativePath");
		}
	}

	public static ObservableExternalConnection From(ExternalConnectionBase connection)
	{
		if (connection is FileExternalConnection connection2)
		{
			return new ObservableFileExternalConnection(connection2);
		}
		if (connection is DirectoryExternalConnection connection3)
		{
			return new ObservableDirectoryExternalConnection(connection3);
		}
		throw new NotImplementedException();
	}
}
