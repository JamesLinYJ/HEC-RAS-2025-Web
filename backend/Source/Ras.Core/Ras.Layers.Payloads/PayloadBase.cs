using System;
using Geospatial.Vectors;
using Utility;
using Utility.Files;
using Utility.Hashing;
using Utility.Logging;

namespace Ras.Layers.Payloads;

public abstract class PayloadBase : IDataHash
{
	[HashContributor]
	public string Name { get; set; }

	[HashContributor]
	public string Description { get; set; }

	[HashContributor]
	public string Variable { get; set; }

	[HashContributor]
	public bool IsEnabled { get; set; }

	protected PayloadBase(string variable)
	{
		Variable = variable;
		IsEnabled = true;
	}

	public void Switch(Action<ConstantPayload> ifConstant, Action<TimeSeriesPayload> ifTimeSeries, Action<RatingCurvePayload> ifRatingCurve, Action<FunctionalPayload> ifFunctional, Action<ExternalPayload> ifExternal, Action<SpatiallyVariableTimeSeriesPayload> ifSpatiallyVariable)
	{
		if (this is ConstantPayload constantPayload)
		{
			ifConstant(constantPayload);
			return;
		}
		if (this is TimeSeriesPayload timeSeriesPayload)
		{
			ifTimeSeries(timeSeriesPayload);
			return;
		}
		if (this is RatingCurvePayload ratingCurvePayload)
		{
			ifRatingCurve(ratingCurvePayload);
			return;
		}
		if (this is FunctionalPayload functionalPayload)
		{
			ifFunctional(functionalPayload);
			return;
		}
		if (this is ExternalPayload externalPayload)
		{
			ifExternal(externalPayload);
			return;
		}
		if (this is SpatiallyVariableTimeSeriesPayload spatiallyVariableTimeSeriesPayload)
		{
			ifSpatiallyVariable(spatiallyVariableTimeSeriesPayload);
			return;
		}
		throw new NotImplementedException();
	}

	public T Switch<T>(Func<ConstantPayload, T> ifConstant, Func<TimeSeriesPayload, T> ifTimeSeries, Func<RatingCurvePayload, T> ifRatingCurve, Func<FunctionalPayload, T> ifFunctional, Func<ExternalPayload, T> ifExternal, Func<SpatiallyVariableTimeSeriesPayload, T> ifSpatiallyVariable)
	{
		if (this is ConstantPayload constantPayload)
		{
			return ifConstant(constantPayload);
		}
		if (this is TimeSeriesPayload timeSeriesPayload)
		{
			return ifTimeSeries(timeSeriesPayload);
		}
		if (this is RatingCurvePayload ratingCurvePayload)
		{
			return ifRatingCurve(ratingCurvePayload);
		}
		if (this is FunctionalPayload functionalPayload)
		{
			return ifFunctional(functionalPayload);
		}
		if (this is ExternalPayload externalPayload)
		{
			return ifExternal(externalPayload);
		}
		if (this is SpatiallyVariableTimeSeriesPayload spatiallyVariableTimeSeriesPayload)
		{
			return ifSpatiallyVariable(spatiallyVariableTimeSeriesPayload);
		}
		throw new NotImplementedException();
	}

	public OperationResult CoversTimeWindow(ExternalConnectionCollection collectionSource, PointCollection spatialContext, DateTime startTime, TimeSpan duration, DateTime injectedStartTime)
	{
		if (this is ConstantPayload || this is RatingCurvePayload)
		{
			return true;
		}
		if (Variable == "Normal Depth")
		{
			return true;
		}
		if (this is TimeSeriesPayload timeSeriesPayload)
		{
			return timeSeriesPayload.CoversTimeWindow(startTime, duration, injectedStartTime);
		}
		if (this is FunctionalPayload)
		{
			throw new NotImplementedException();
		}
		if (this is ExternalPayload externalPayload)
		{
			OperationResult operationResult = externalPayload.TryEvaluate(collectionSource, spatialContext, out var payload);
			if (!operationResult)
			{
				return operationResult;
			}
			return payload.CoversTimeWindow(collectionSource, spatialContext, startTime, duration, injectedStartTime);
		}
		if (this is SpatiallyVariableTimeSeriesPayload spatiallyVariableTimeSeriesPayload)
		{
			return spatiallyVariableTimeSeriesPayload.CoversTimeWindow(startTime, duration, injectedStartTime);
		}
		throw new NotSupportedException("Payload type not supported: " + GetType().Name);
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public OperationResult DataEquals(PayloadBase other)
	{
		OperationResult or = true;
		if (GetDataHash() != other.GetDataHash())
		{
			or.AddFailure("PayloadBase hash mismatch.");
		}
		if (!StringHelpers.StringEquals(Name, other.Name))
		{
			or.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("PayloadBase", "Name", Name, other.Name));
		}
		if (!StringHelpers.StringEquals(Description, other.Description))
		{
			or.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("PayloadBase", "Description", Description, other.Description));
		}
		if (!StringHelpers.StringEquals(Variable, other.Variable))
		{
			or.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("PayloadBase", "Variable", Variable, other.Variable));
		}
		if (IsEnabled != other.IsEnabled)
		{
			or.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("PayloadBase", "IsEnabled", IsEnabled, other.IsEnabled));
		}
		if (GetType() != other.GetType())
		{
			or.AddFailure("Payload type mismatch.");
		}
		else
		{
			Switch((ConstantPayload cPl) =>
			{
				ConstantPayload constantPayload = other as ConstantPayload;
				if (!MathHelpers.DoubleEquals(cPl.Value, constantPayload.Value))
				{
					or.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("ConstantPayload", "Value", cPl.Value, constantPayload.Value));
				}
			}, (TimeSeriesPayload tsPl) =>
			{
				TimeSeriesPayload timeSeriesPayload = other as TimeSeriesPayload;
				OperationResult operationResult = tsPl.TimeSeriesDefinition.DataEquals(timeSeriesPayload.TimeSeriesDefinition);
				if (!operationResult)
				{
					or.AddFailure(operationResult);
				}
				Memory<double> values = tsPl.Values;
				ReadOnlySpan<double> a = values.Span;
				values = timeSeriesPayload.Values;
				if (!MathHelpers.SpanDoubleEquals(a, values.Span))
				{
					or.AddFailure("TimeSeriesPayload Values do not match.");
				}
			}, (RatingCurvePayload rcPl) =>
			{
				RatingCurvePayload ratingCurvePayload = other as RatingCurvePayload;
				if (!StringHelpers.StringEquals(rcPl.IndependentVariable, ratingCurvePayload.IndependentVariable))
				{
					or.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("RatingCurvePayload", "IndependentVariable", rcPl.IndependentVariable, ratingCurvePayload.IndependentVariable));
				}
				Memory<double> inputValues = rcPl.InputValues;
				ReadOnlySpan<double> a = inputValues.Span;
				inputValues = ratingCurvePayload.InputValues;
				if (!MathHelpers.SpanDoubleEquals(a, inputValues.Span))
				{
					or.AddFailure("RatingCurvePayload InputValues do not match.");
				}
				inputValues = rcPl.OutputValues;
				ReadOnlySpan<double> a2 = inputValues.Span;
				inputValues = ratingCurvePayload.OutputValues;
				if (!MathHelpers.SpanDoubleEquals(a2, inputValues.Span))
				{
					or.AddFailure("RatingCurvePayload OutputValues do not match.");
				}
			}, (FunctionalPayload fxnPl) =>
			{
			}, (ExternalPayload extPl) =>
			{
				ExternalPayload externalPayload = other as ExternalPayload;
				if (!StringHelpers.StringEquals(extPl.ExternalConnection, externalPayload.ExternalConnection))
				{
					or.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("ExternalPayload", "ExternalConnection", extPl.ExternalConnection, externalPayload.ExternalConnection));
				}
				if (!StringHelpers.StringEquals(extPl.ExternalPath, externalPayload.ExternalPath))
				{
					or.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("ExternalPayload", "ExternalPath", extPl.ExternalPath, externalPayload.ExternalPath));
				}
			}, (SpatiallyVariableTimeSeriesPayload spTsPl) =>
			{
				SpatiallyVariableTimeSeriesPayload other2 = other as SpatiallyVariableTimeSeriesPayload;
				OperationResult operationResult = spTsPl.DataEquals(other2);
				if (!operationResult)
				{
					or.AddFailure(operationResult);
				}
			});
		}
		return or;
	}
}
