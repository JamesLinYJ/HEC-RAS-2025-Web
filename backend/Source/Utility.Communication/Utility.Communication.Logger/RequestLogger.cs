using System;
using System.Collections.Specialized;
using System.IO;

namespace Utility.Communication.Logger;

public class RequestLogger
{
	private StringCollection _logTo;

	public RequestLogger(StringCollection logTo)
	{
		_logTo = logTo;
	}

	private void AppendLog(string line)
	{
		_logTo.Add(line);
	}

	public void LogPush(Session session, TransferRequest req)
	{
		string nowDateTimeTerm = DateTime.Now.ToString() + " PUSH ";
		nowDateTimeTerm = nowDateTimeTerm + " (" + session.LocalConnection.URL + " -> " + session.RemoteConnection.BucketSubdomain + "." + session.RemoteConnection.URL + ") ";
		nowDateTimeTerm = nowDateTimeTerm + req.LocalFile.Filename + " -> " + req.RemoteFile.Filename + " ";
		AppendLog(nowDateTimeTerm);
	}

	public void LogPull(Session session, TransferRequest req)
	{
		string nowDateTimeTerm = DateTime.Now.ToString() + " PULL ";
		nowDateTimeTerm = nowDateTimeTerm + " (" + session.LocalConnection.URL + " <- " + session.RemoteConnection.BucketSubdomain + "." + session.RemoteConnection.URL + ") ";
		nowDateTimeTerm = nowDateTimeTerm + req.LocalFile.Filename + " <- " + req.RemoteFile.Filename + " ";
		AppendLog(nowDateTimeTerm);
	}

	public void LogDelete(Session session, TransferRequest req, bool isLocalDelete)
	{
		string urlLocalConnectionTerm = DateTime.Now.ToString() + " DELETE ";
		urlLocalConnectionTerm = urlLocalConnectionTerm + " (" + session.LocalConnection.URL + " - " + session.RemoteConnection.BucketSubdomain + "." + session.RemoteConnection.URL + ") ";
		urlLocalConnectionTerm += (isLocalDelete ? req.LocalFile.Filename : req.RemoteFile.Filename);
		AppendLog(urlLocalConnectionTerm);
	}

	public void WriteToFile(string filePath)
	{
		string[] logTo = new string[_logTo.Count];
		_logTo.CopyTo(logTo, 0);
		File.WriteAllLines(filePath, logTo);
	}
}
