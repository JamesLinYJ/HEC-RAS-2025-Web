using System;
using Ras.Layers.Payloads;

namespace Ras.Observables;

public class ObservableRatingCurvePayload : ObservablePayload
{
	public static Type Type => typeof(ObservableRatingCurvePayload);

	public override RatingCurvePayload Payload => _payload as RatingCurvePayload;

	public override string FriendlyName => "Rating Curve";

	public ObservableRatingCurvePayload(RatingCurvePayload payload)
		: base(payload)
	{
	}
}
