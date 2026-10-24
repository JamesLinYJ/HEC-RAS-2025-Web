using System;
using System.Collections.Generic;
using System.IO;
using Utility.Extensions;
using Utility.Logging;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

public class TopologyRecipe : IStreamableObject
{
	public static class MessageCodes
	{
		public static readonly MessageClass ErrorParsingRecipe = new MessageClass("MeshGen_ParsingRecipe", MessageSeverity.Error, "Recipe could not be parsed: {parseError}.");

		public static Message GetErrorParsing(string parseError)
		{
			return ErrorParsingRecipe.Create(parseError);
		}
	}

	private ref struct ParseState
	{
		public ReadOnlyMemory<char> buff = default;

		public TopologyRecipe recipe = null;

		public string Source = null;

		public bool global = false;

		public OperationResult opRes = null;

		public Dictionary<ReadOnlyMemory<char>, Type> methods = null;

		public int currentPosition => Source.Length - buff.Length;

		public ParseState()
		{
		}

		public void LogError(string message, int location = -1, int length = 8)
		{
			if (recipe != null)
			{
				recipe.Valid = false;
			}
			if (opRes != null)
			{
				if (location < 0)
				{
					location = currentPosition;
				}
				string parseError = FormattedError(message, location, length);
				opRes.AddFailure(MessageCodes.GetErrorParsing(parseError));
			}
		}

		public string FormattedError(string message, int errorLocation, int length = 8)
		{
			length = Math.Min(length, Source.Length - errorLocation);
			return message + " near " + Source.AsMemory().Slice(errorLocation, length);
		}
	}

	private enum ValueState
	{
		START,
		UNSIGNED,
		FRACTION,
		ALPHANUM,
		SUCCESS,
		ERROR
	}

	private List<TopologyStep> _steps;

	private Dictionary<ReadOnlyMemory<char>, Type> _methods;

	private readonly bool _global;

	private static string commentString = "#";

	private static Dictionary<ReadOnlyMemory<char>, Type> _regionMethods = InitRegionMethods();

	private static Dictionary<ReadOnlyMemory<char>, Type> _globalMethods = InitGlobalMethods();

	public string Source { get; private set; }

	public bool Valid { get; private set; }

	public IReadOnlyList<TopologyStep> Steps => _steps;

	public void SetSource(string source)
	{
		Source = source;
		Valid = false;
	}

	public TopologyRecipe(bool global = false)
	{
		_steps = new List<TopologyStep>();
		Valid = false;
		Source = "";
		_global = global;
		if (_global)
		{
			_methods = _globalMethods;
		}
		else
		{
			_methods = _regionMethods;
		}
	}

	public void StreamIn(BinaryReader br)
	{
		Source = br.ReadStringA();
		ParseState parseState = new ParseState();
		parseState.recipe = this;
		parseState.global = false;
		parseState.buff = Source.AsMemory();
		parseState.Source = Source;
		ParseState parseState2 = parseState;
		parseState2.recipe.Valid = TryParseTop(ref parseState2);
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.WriteStringA(Source);
	}

	private static Dictionary<ReadOnlyMemory<char>, Type> InitRegionMethods()
	{
		MemStringEqualityComparer comparer = new MemStringEqualityComparer();
		return new Dictionary<ReadOnlyMemory<char>, Type>(10, comparer)
		{
			["SwapEdges".AsMemory()] = typeof(SwapEdgesStep),
			["Smooth".AsMemory()] = typeof(SmoothStep),
			["ForceDisplacement".AsMemory()] = typeof(ForceDisplacementStep),
			["Orthogonalize".AsMemory()] = typeof(OrthogonalizeStep),
			["SmoothAngles".AsMemory()] = typeof(SmoothAnglesStep),
			["Distort".AsMemory()] = typeof(DistortStep),
			["RemoveFaces".AsMemory()] = typeof(RemoveFacesStep),
			["Merge".AsMemory()] = typeof(RemoveFacesStep),
			["RemoveNodes".AsMemory()] = typeof(RemoveNodesStep),
			["MergeFaces".AsMemory()] = typeof(RemoveNodesStep),
			["OptTopology".AsMemory()] = typeof(OptTopologyStep),
			["OptSkew".AsMemory()] = typeof(OptSkewStep),
			["Split".AsMemory()] = typeof(SplitStep),
			["Default".AsMemory()] = typeof(DefaultStep)
		};
	}

	private static Dictionary<ReadOnlyMemory<char>, Type> InitGlobalMethods()
	{
		return InitRegionMethods();
	}

	public static bool TryParse(string input, out TopologyRecipe recipe, bool global, OperationResult res = null)
	{
		ParseState parseState = new ParseState();
		parseState.recipe = new TopologyRecipe(global)
		{
			Source = input
		};
		parseState.global = global;
		parseState.buff = input.AsMemory();
		parseState.Source = input;
		parseState.opRes = res;
		ParseState parseState2 = parseState;
		parseState2.recipe.Valid = TryParseTop(ref parseState2);
		recipe = parseState2.recipe;
		return parseState2.recipe.Valid;
	}

	public static bool TryParse(string input, out string errorMessage, bool global)
	{
		OperationResult operationResult = new OperationResult();
		ParseState parseState = new ParseState();
		parseState.recipe = null;
		parseState.global = global;
		parseState.buff = input.AsMemory();
		parseState.Source = input;
		parseState.opRes = operationResult;
		ParseState parseState2 = parseState;
		bool result = TryParseTop(ref parseState2);
		if (operationResult.Result)
		{
			errorMessage = "";
			return result;
		}
		errorMessage = operationResult.GetConcatenatedMessages();
		return result;
	}

	private static bool TryParseTop(ref ParseState parseState)
	{
		if (parseState.global)
		{
			parseState.methods = _globalMethods;
		}
		else
		{
			parseState.methods = _regionMethods;
		}
		while (true)
		{
			SkipSpace(ref parseState);
			if (parseState.buff.Length == 0)
			{
				return true;
			}
			if (!TryParseStep(ref parseState))
			{
				break;
			}
			TryParseCharSet(ref parseState, ";,", required: false);
		}
		return false;
	}

	private static bool TryParseStep(ref ParseState parseState)
	{
		int currentPosition = parseState.currentPosition;
		ReadOnlyMemory<char> buff = parseState.buff;
		if (!TryParseIdentifier(ref parseState, out var identifier, required: false))
		{
			return false;
		}
		if (!parseState.methods.TryGetValue(identifier, out var localValue))
		{
			parseState.LogError("Unrecognized step name", currentPosition, parseState.currentPosition - currentPosition);
			parseState.buff = buff;
			return false;
		}
		if (!TryParseOpenParen(ref parseState))
		{
			parseState.buff = buff;
			return false;
		}
		TopologyStep topologyStep = null;
		if (parseState.recipe != null)
		{
			topologyStep = (TopologyStep)Activator.CreateInstance(localValue);
		}
		if (!TryParseParameterList(ref parseState, localValue, topologyStep))
		{
			parseState.buff = buff;
			return false;
		}
		if (!TryParseCloseParen(ref parseState))
		{
			return false;
		}
		parseState.recipe?._steps.Add(topologyStep);
		return true;
	}

	private static void SkipSpace(ref ParseState parseState)
	{
		parseState.buff = parseState.buff.TrimStart();
		while (parseState.buff.Span.StartsWith(commentString))
		{
			int indexOf = parseState.buff.Span.IndexOf('\r');
			if (indexOf < 0)
			{
				indexOf = parseState.buff.Length - 1;
			}
			parseState.buff = parseState.buff.Slice(indexOf + 1);
			parseState.buff = parseState.buff.TrimStart();
		}
	}

	private static bool TryParseIdentifier(ref ParseState parseState, out ReadOnlyMemory<char> identifier, bool required)
	{
		SkipSpace(ref parseState);
		int currentPosition = parseState.currentPosition;
		int intervalIndex = 0;
		for (intervalIndex = 0; intervalIndex < parseState.buff.Length; intervalIndex++)
		{
			char c = parseState.buff.Span[intervalIndex];
			if ((intervalIndex == 0 && !char.IsLetter(c)) || !char.IsLetterOrDigit(c))
			{
				break;
			}
		}
		identifier = parseState.buff.Slice(0, intervalIndex);
		if (intervalIndex > 0)
		{
			parseState.buff = parseState.buff.Slice(intervalIndex);
		}
		if (intervalIndex > 0)
		{
			return true;
		}
		if (required)
		{
			parseState.LogError("Failed to parse identifier", currentPosition);
		}
		return false;
	}

	private static bool TryParseOpenParen(ref ParseState parseState)
	{
		return TryParseChar(ref parseState, '(');
	}

	private static bool TryParseCloseParen(ref ParseState parseState)
	{
		return TryParseChar(ref parseState, ')');
	}

	private static bool TryParseChar(ref ParseState parseState, char c, bool required = true)
	{
		SkipSpace(ref parseState);
		if (parseState.buff.Length > 0 && parseState.buff.Span[0] == c)
		{
			parseState.buff = parseState.buff.Slice(1);
			return true;
		}
		if (required)
		{
			parseState.LogError($"Expected '{c}'");
		}
		return false;
	}

	private static bool TryParseCharSet(ref ParseState parseState, string charSet, bool required = true)
	{
		SkipSpace(ref parseState);
		if (parseState.buff.Length > 0 && charSet.Contains(parseState.buff.Span[0]))
		{
			parseState.buff = parseState.buff.Slice(1);
			return true;
		}
		if (required)
		{
			parseState.LogError("Expected one of '" + charSet + "'");
		}
		return false;
	}

	private static bool TryParseParameterList(ref ParseState parseState, Type stepType, TopologyStep step)
	{
		do
		{
			ReadOnlyMemory<char> buff = parseState.buff;
			if (!TryParseParameter(ref parseState, out var paramName, out var paramValue))
			{
				break;
			}
			if (!TopologyStep.TrySetParameter(stepType, paramName, paramValue, step))
			{
				parseState.buff = buff;
				parseState.LogError("Unrecognized parameter name", parseState.currentPosition, paramName.Length);
				return false;
			}
		}
		while (TryParseChar(ref parseState, ',', required: false));
		return true;
	}

	private static bool TryParseParameter(ref ParseState parseState, out ReadOnlyMemory<char> paramName, out ReadOnlyMemory<char> paramValue)
	{
		paramName = null;
		paramValue = null;
		if (!TryParseIdentifier(ref parseState, out paramName, required: false))
		{
			return false;
		}
		if (!TryParseChar(ref parseState, '='))
		{
			return false;
		}
		if (!TryParseValue(ref parseState, out paramValue))
		{
			return false;
		}
		return true;
	}

	private static bool TryParseValue(ref ParseState parseState, out ReadOnlyMemory<char> paramValue)
	{
		SkipSpace(ref parseState);
		ReadOnlyMemory<char> buff = parseState.buff;
		int i = 0;
		ValueState valueState = ValueState.START;
		int ordinal = 0;
		while (i < buff.Length)
		{
			char c = buff.Span[i];
			switch (valueState)
			{
			case ValueState.START:
				if (c == '-' || c == '+')
				{
					i++;
					valueState = ValueState.UNSIGNED;
				}
				else if (char.IsAsciiDigit(c))
				{
					valueState = ValueState.UNSIGNED;
				}
				else if (c == '.')
				{
					valueState = ValueState.FRACTION;
				}
				else if (char.IsAsciiLetter(c))
				{
					i++;
					valueState = ValueState.ALPHANUM;
				}
				else
				{
					valueState = ValueState.ERROR;
				}
				break;
			case ValueState.UNSIGNED:
				for (; i < buff.Length && char.IsAsciiDigit(buff.Span[i]); i++)
				{
					ordinal++;
				}
				valueState = ValueState.FRACTION;
				break;
			case ValueState.FRACTION:
				if (c == '.')
				{
					for (i++; i < buff.Length && char.IsAsciiDigit(buff.Span[i]); i++)
					{
						ordinal++;
					}
				}
				valueState = ((ordinal <= 0) ? ValueState.ERROR : ValueState.SUCCESS);
				break;
			case ValueState.ALPHANUM:
				for (; i < buff.Length && char.IsAsciiLetterOrDigit(buff.Span[i]); i++)
				{
				}
				valueState = ValueState.SUCCESS;
				break;
			case ValueState.SUCCESS:
				paramValue = buff.Slice(0, i);
				parseState.buff = buff.Slice(i);
				return true;
			case ValueState.ERROR:
				paramValue = "".AsMemory();
				parseState.LogError("Invalid value");
				return false;
			}
		}
		parseState.LogError("Expected a value");
		paramValue = "".AsMemory();
		return false;
	}
}
