using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Utility.Progress;

namespace Utility.Logging;

[DebuggerDisplay("{Result}")]
public class OperationResult
{
	public bool Result { get; set; }

	public List<Message> Messages { get; }

	public OperationResult()
	{
		Messages = new List<Message>();
		Result = true;
	}

	public OperationResult(bool success)
	{
		Messages = new List<Message>();
		Result = success;
	}

	internal OperationResult(OperationResult cloneFrom)
	{
		Messages = new List<Message>(cloneFrom.Messages);
		Result = cloneFrom.Result;
	}

	public static OperationResult Success()
	{
		return new OperationResult(success: true);
	}

	public static OperationResult Fail(Message error)
	{
		OperationResult operationResult = new OperationResult(success: false);
		operationResult.AddMessage(error);
		return operationResult;
	}

	public static OperationResult Fail(string msg)
	{
		OperationResult operationResult = new OperationResult(success: false);
		operationResult.AddMessage(new Message(msg));
		return operationResult;
	}

	public static OperationResult Fail(string task, Exception ex)
	{
		OperationResult operationResult = new OperationResult(success: false);
		operationResult.AddMessage(GeneralMessageCodes.GetExceptionOccurred(task, ex));
		return operationResult;
	}

	public static OperationResult Fail(IList<Message> errors)
	{
		OperationResult operationResult = new OperationResult(success: false);
		operationResult.AddMessages(errors);
		return operationResult;
	}

	public void AddFailure(Message msg)
	{
		Result = false;
		AddMessage(msg);
	}

	public void AddFailureAndReport(Message msg, ProgressReporter pr)
	{
		Result = false;
		AddMessage(msg);
		pr?.ReportMessage(msg);
	}

	public void AddFailure(string msg)
	{
		Result = false;
		AddMessage(Message.GenericError(msg));
	}

	public void AddFailureAndReport(string msg, ProgressReporter pr)
	{
		Result = false;
		AddMessage(msg);
		pr?.ReportMessage(msg);
	}

	public void AddDedupedFailure(Message msg)
	{
		Result = false;
		if (Messages.Count == 0)
		{
			AddMessage(msg);
		}
		else if (Messages.Last().MessageType != msg.MessageType)
		{
			AddMessage(msg);
		}
	}

	public void AddMessage(Message msg)
	{
		Messages.Add(msg);
	}

	public void AddMessage(string msg)
	{
		AddMessage(new Message(msg));
	}

	public void AddMessages(IEnumerable<Message> messages)
	{
		Messages.AddRange(messages);
	}

	public void AddFailures(IEnumerable<Message> failures)
	{
		Result = false;
		AddMessages(failures);
	}

	public void AddFailure(OperationResult other)
	{
		Result = false;
		AddMessages(other.Messages);
	}

	public void AddFailureAndReport(OperationResult msgs, ProgressReporter pr)
	{
		AddFailure(msgs);
		foreach (Message message in msgs.Messages)
		{
			pr?.ReportMessage(message);
		}
	}

	public void AddMessages(OperationResult other)
	{
		Messages.AddRange(other.Messages);
	}

	public void RemoveMessage(Message msg)
	{
		Messages.Remove(msg);
	}

	public void CopyMessagesTo(OperationResult other)
	{
		other.Messages.AddRange(Messages);
	}

	public bool ContainsMessage(MessageClass msgClass)
	{
		return Messages.Any((Message m) => m.MessageType == msgClass);
	}

	public bool ContainsMessage(MessageClass msgClass, out Message firstInst)
	{
		foreach (Message message in Messages)
		{
			if (message.MessageType == msgClass)
			{
				firstInst = message;
				return true;
			}
		}
		firstInst = default;
		return false;
	}

	public void MergeIn(OperationResult other)
	{
		other.CopyMessagesTo(this);
		if (!other.Result)
		{
			Result = false;
		}
	}

	public string GetConcatenatedMessages(string delimeter = null)
	{
		if (Messages.Count == 0)
		{
			return string.Empty;
		}
		if (string.IsNullOrEmpty(delimeter))
		{
			delimeter = Environment.NewLine;
		}
		return string.Join(delimeter, Messages.Select((Message msg) => msg.EvaluatedMessage));
	}

	public IEnumerable<AggregatedMessage> EnumerateAggregatedMessages()
	{
		Dictionary<string, int> extraCounts = new Dictionary<string, int>();
		List<Message> messages = new List<Message>();
		foreach (Message message in Messages)
		{
			string code = message.MessageType.Code;
			if (extraCounts.ContainsKey(code))
			{
				extraCounts[code]++;
				continue;
			}
			extraCounts[code] = 0;
			messages.Add(message);
		}
		foreach (Message message2 in messages)
		{
			yield return new AggregatedMessage(message2, extraCounts[message2.MessageType.Code]);
		}
	}

	public OperationResult Clone()
	{
		return new OperationResult(this);
	}

	public string GetDiagnosticString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Result: " + Result);
		if (Messages != null)
		{
			foreach (Message message in Messages)
			{
				stringBuilder.AppendLine(message.EvaluatedMessage);
			}
		}
		return stringBuilder.ToString();
	}

	public static implicit operator OperationResult(bool result)
	{
		return new OperationResult(result);
	}

	public static implicit operator bool(OperationResult result)
	{
		return result.Result;
	}
}
