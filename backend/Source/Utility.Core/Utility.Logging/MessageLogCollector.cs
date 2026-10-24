using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Utility.Progress;

namespace Utility.Logging;

public sealed class MessageLogCollector : IDisposable
{
	private readonly ProgressReporter _reporter;

	private readonly StringBuilder _text = new StringBuilder();

	private readonly List<MessageLogEntry> _entries = new List<MessageLogEntry>();

	private readonly object _sync = new object();

	public string Text
	{
		get
		{
			lock (_sync)
			{
				return _text.ToString();
			}
		}
	}

	public IReadOnlyList<MessageLogEntry> Entries
	{
		get
		{
			lock (_sync)
			{
				return _entries.ToArray();
			}
		}
	}

	public MessageLogCollector(ProgressReporter reporter)
	{
		if (reporter == null)
		{
			throw new ArgumentNullException("reporter");
		}
		_reporter = reporter;
		_reporter.MessageReported += OnMessageReported;
	}

	private void OnMessageReported(ProgressReporter sender, Message msg)
	{
		MessageClass messageType = msg.MessageType;
		string[] data = msg.LogData.Select((KeyValuePair<string, object> kvp) => kvp.Value?.ToString() ?? "").ToArray();
		string evaluatedMessage = msg.EvaluatedMessage;
		MessageLogEntry messageLogEntry = new MessageLogEntry(DateTime.Now, messageType?.DefaultSeverity ?? MessageSeverity.Information, messageType?.Code, data);
		lock (_sync)
		{
			_text.AppendLine(evaluatedMessage);
			_entries.Add(messageLogEntry);
		}
	}

	public void Dispose()
	{
		_reporter.MessageReported -= OnMessageReported;
	}
}
