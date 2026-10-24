using System;
using System.Collections.Generic;

namespace Utility.Communication;

public class Session
{
	public string SessionName { get; set; }

	public Connection LocalConnection { get; set; }

	public Connection RemoteConnection { get; set; }

	public bool CanPull { get; set; } = true;

	public bool CanPush { get; set; } = true;

	public bool CanDeleteRemote { get; set; } = true;

	private Session()
	{
	}

	public Session(string sessionName, Connection localConnection, Connection remoteConnection)
	{
		SessionName = sessionName;
		LocalConnection = localConnection;
		RemoteConnection = remoteConnection;
	}

	public Session(string sessionName, Connection localConnection, Connection remoteConnection, bool canPull, bool canPush, bool canDeleteRemote)
	{
		SessionName = sessionName;
		LocalConnection = localConnection;
		RemoteConnection = remoteConnection;
		CanPull = canPull;
		CanPush = canPush;
		CanDeleteRemote = canDeleteRemote;
	}

	public string Serialize()
	{
		return SanitizeSaving(SessionName) + "\n" + SanitizeSaving(LocalConnection.FriendlyName) + "\n" + SanitizeSaving(RemoteConnection.FriendlyName) + "\n" + CanPull + "\n" + CanPush + "\n" + CanDeleteRemote;
	}

	public static Session TryDeserialize(string sl, List<Connection> connections)
	{
		string[] parts = sl.Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length >= 3)
		{
			string sessionName = parts[0];
			string part = parts[1];
			string part2 = parts[2];
			bool result = true;
			bool result2 = true;
			bool result3 = true;
			if (parts.Length >= 6)
			{
				if (!bool.TryParse(parts[3], out result))
				{
					result = true;
				}
				if (!bool.TryParse(parts[4], out result2))
				{
					result2 = true;
				}
				if (!bool.TryParse(parts[5], out result3))
				{
					result3 = true;
				}
			}
			Connection connection = null;
			Connection connection2 = null;
			foreach (Connection connection3 in connections)
			{
				if (connection3.FriendlyName.Equals(part))
				{
					connection = connection3;
				}
				if (connection3.FriendlyName.Equals(part2))
				{
					connection2 = connection3;
				}
			}
			if (connection == null || connection2 == null)
			{
				return null;
			}
			return new Session(sessionName, connection, connection2, result, result2, result3);
		}
		return null;
	}

	private static string SanitizeSaving(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "_";
		}
		return s;
	}

	private static string SanitizeLoading(string s)
	{
		if (s == "_")
		{
			return "";
		}
		return s;
	}

	public Session Clone()
	{
		return new Session(SessionName, LocalConnection, RemoteConnection, CanPull, CanPush, CanDeleteRemote);
	}
}
