using System;
using Ras.Layers.Payloads;

namespace Ras.Observables;

public class ObservableFunctionalPayload : ObservablePayload
{
	public static Type Type => typeof(ObservableFunctionalPayload);

	public override FunctionalPayload Payload => _payload as FunctionalPayload;

	public override string FriendlyName => "Functional";

	public ObservableFunctionalPayload(FunctionalPayload payload)
		: base(payload)
	{
	}
}
