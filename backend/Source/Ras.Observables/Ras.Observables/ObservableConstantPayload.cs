using System;
using Ras.Layers.Payloads;

namespace Ras.Observables;

public class ObservableConstantPayload : ObservablePayload
{
	public static Type Type => typeof(ObservableConstantPayload);

	public override ConstantPayload Payload => _payload as ConstantPayload;

	public double Value
	{
		get
		{
			return Payload.Value;
		}
		set
		{
			SetProperty(Payload.Value, value, Payload, (ConstantPayload r, double v) =>
			{
				r.Value = v;
			}, "Value");
		}
	}

	public override string FriendlyName => "Constant";

	public ObservableConstantPayload(string variable, double constant)
		: this(new ConstantPayload(variable, constant))
	{
	}

	public ObservableConstantPayload(ConstantPayload payload)
		: base(payload)
	{
	}
}
