using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RasMapperLib.Mapping;

namespace RasMapperLib.Render;

public class VelocityComputer1D
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__20_002D0
	{
		public float[] clipDepths;

		public _Closure_0024__20_002D0(_Closure_0024__20_002D0 other)
		{
			if (other != null)
			{
				clipDepths = other.clipDepths;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(XSISMapPoint px)
		{
			return clipDepths[px.Index] > 0f;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__21_002D0
	{
		public float[][] clipDepths;

		public int timeIndex;

		public _Closure_0024__21_002D0(_Closure_0024__21_002D0 other)
		{
			if (other != null)
			{
				clipDepths = other.clipDepths;
				timeIndex = other.timeIndex;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(XSISMapPoint px)
		{
			return clipDepths[px.Index][timeIndex] > 0f;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__22_002D0
	{
		public float[] clipDepths;

		public _Closure_0024__22_002D0(_Closure_0024__22_002D0 other)
		{
			if (other != null)
			{
				clipDepths = other.clipDepths;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(XSISMapPoint px)
		{
			return clipDepths[px.Index] > 0f;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D0
	{
		public float[] clipDepths;

		public _Closure_0024__24_002D0(_Closure_0024__24_002D0 other)
		{
			if (other != null)
			{
				clipDepths = other.clipDepths;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(XSISMapPoint px)
		{
			return clipDepths[px.Index] > 0f;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__27_002D0
	{
		public float[] clipDepths;

		public _Closure_0024__27_002D0(_Closure_0024__27_002D0 other)
		{
			if (other != null)
			{
				clipDepths = other.clipDepths;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(XSISMapPoint px)
		{
			return clipDepths[px.Index] > 0f;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__29_002D0
	{
		public float[] clipDepths;

		public _Closure_0024__29_002D0(_Closure_0024__29_002D0 other)
		{
			if (other != null)
			{
				clipDepths = other.clipDepths;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(XSISMapPoint px)
		{
			return clipDepths[px.Index] > 0f;
		}
	}

	protected FlowDistributionData _flowDist;

	protected int _USXS_ID;

	protected int _DSXS_ID;

	protected int XSIS_ID;

	protected float _usFlow;

	protected float _dsFlow;

	protected float _usFlowSqrd;

	protected float _dsFlowSqrd;

	protected FlowDistributionData.WSInterpData _usWSInterpData;

	protected FlowDistributionData.WSInterpData _dsWSInterpData;

	protected Func<float, float> _usStationComputer;

	protected Func<float, float> _dsStationComputer;

	protected RASXS _XS;

	protected RASXSInterpolationSurface _XSIS;

	public VelocityComputer1D(FlowDistributionData flowDist, RASXS XS, RASXSInterpolationSurface XSIS)
	{
		_USXS_ID = int.MinValue;
		_DSXS_ID = int.MinValue;
		XSIS_ID = int.MinValue;
		_flowDist = flowDist;
		_XS = XS;
		_XSIS = XSIS;
	}

	public void UpdateXSIS(int xsisID, float[] xsWaterSurfaces, float[] xsFlows)
	{
		UpdateIDIfNecessary(xsisID);
		UpdateData(xsFlows[_USXS_ID], xsFlows[_DSXS_ID], xsWaterSurfaces[_USXS_ID], xsWaterSurfaces[_DSXS_ID]);
	}

	public void UpdateXSIS(int xsisID, float usWaterSurface, float dsWaterSurface, float usFlow, float dsFlow)
	{
		UpdateIDIfNecessary(xsisID);
		UpdateData(usFlow, dsFlow, usWaterSurface, dsWaterSurface);
	}

	private void UpdateIDIfNecessary(int xsisID)
	{
		if (xsisID != XSIS_ID)
		{
			XSIS_ID = xsisID;
			_USXS_ID = _XSIS.GetUpstreamXSID(XSIS_ID);
			_DSXS_ID = _XSIS.GetDownstreamXSID(XSIS_ID);
			_usStationComputer = _XS.GetStationComputer(_USXS_ID);
			_dsStationComputer = _XS.GetStationComputer(_DSXS_ID);
		}
	}

	private void UpdateData(float usFlow, float dsFlow, float usWs, float dsWs)
	{
		_usFlow = usFlow;
		_dsFlow = dsFlow;
		_usFlowSqrd = usFlow * usFlow;
		_dsFlowSqrd = dsFlow * dsFlow;
		_usWSInterpData = _flowDist.ComputeWSInterpData(_USXS_ID, usWs);
		_dsWSInterpData = _flowDist.ComputeWSInterpData(_DSXS_ID, dsWs);
	}

	public float ComputeVelocity(XSISMapPoint pixel)
	{
		float station = _usStationComputer(pixel.M);
		float station2 = _dsStationComputer(pixel.M);
		float velocity = _flowDist.Velocity(_USXS_ID, _usWSInterpData, station, _usFlow);
		float velocity2 = _flowDist.Velocity(_DSXS_ID, _dsWSInterpData, station2, _dsFlow);
		return velocity * pixel.Z + velocity2 * (1f - pixel.Z);
	}

	public void ComputeVelocity(List<XSISMapPoint> points, float[] clipDepths, float[] outVelMag, float[] outVelX, float[] outVelY)
	{
		_Closure_0024__20_002D0 capturedLocals = default;
		_Closure_0024__20_002D0 capturedLocals2 = new _Closure_0024__20_002D0(capturedLocals);
		capturedLocals2.clipDepths = clipDepths;
		foreach (XSISMapPoint xsisMapPoint in points.Where([SpecialName] (XSISMapPoint px) => capturedLocals2.clipDepths[px.Index] > 0f))
		{
			float velocity = ComputeVelocity(xsisMapPoint);
			outVelMag[xsisMapPoint.Index] = Math.Abs(velocity);
			outVelX[xsisMapPoint.Index] = xsisMapPoint.StreamNX * velocity;
			outVelY[xsisMapPoint.Index] = xsisMapPoint.StreamNY * velocity;
		}
	}

	public void ComputeVelocity(List<XSISMapPoint> points, float[][] clipDepths, float[][] outVelMag, float[][] outVelX, float[][] outVelY, int timeIndex)
	{
		_Closure_0024__21_002D0 capturedLocals = default;
		_Closure_0024__21_002D0 capturedLocals2 = new _Closure_0024__21_002D0(capturedLocals);
		capturedLocals2.clipDepths = clipDepths;
		capturedLocals2.timeIndex = timeIndex;
		foreach (XSISMapPoint xsisMapPoint in points.Where([SpecialName] (XSISMapPoint px) => capturedLocals2.clipDepths[px.Index][capturedLocals2.timeIndex] > 0f))
		{
			float velocity = ComputeVelocity(xsisMapPoint);
			outVelMag[xsisMapPoint.Index][capturedLocals2.timeIndex] = Math.Abs(velocity);
			outVelX[xsisMapPoint.Index][capturedLocals2.timeIndex] = xsisMapPoint.StreamNX * velocity;
			outVelY[xsisMapPoint.Index][capturedLocals2.timeIndex] = xsisMapPoint.StreamNY * velocity;
		}
	}

	public void ComputeVelocityUpdateIfMax(List<XSISMapPoint> points, float[] clipDepths, float[] velMag, float[] velX, float[] velY)
	{
		_Closure_0024__22_002D0 capturedLocals = default;
		_Closure_0024__22_002D0 capturedLocals2 = new _Closure_0024__22_002D0(capturedLocals);
		capturedLocals2.clipDepths = clipDepths;
		foreach (XSISMapPoint xsisMapPoint in points.Where([SpecialName] (XSISMapPoint px) => capturedLocals2.clipDepths[px.Index] > 0f))
		{
			float velocity = ComputeVelocity(xsisMapPoint);
			float velMag2 = Math.Abs(velocity);
			if (velMag2 > velMag[xsisMapPoint.Index])
			{
				velMag[xsisMapPoint.Index] = velMag2;
				velX[xsisMapPoint.Index] = xsisMapPoint.StreamNX * velocity;
				velY[xsisMapPoint.Index] = xsisMapPoint.StreamNY * velocity;
			}
		}
	}

	private float ComputeShear(XSISMapPoint pixel)
	{
		float station = _usStationComputer(pixel.M);
		float station2 = _dsStationComputer(pixel.M);
		float shear = _flowDist.Shear(_USXS_ID, _usWSInterpData, station, _usFlowSqrd);
		float shear2 = _flowDist.Shear(_DSXS_ID, _dsWSInterpData, station2, _dsFlowSqrd);
		return shear * pixel.Z + shear2 * (1f - pixel.Z);
	}

	public void ComputeShear(List<XSISMapPoint> points, float[] clipDepths, float[] shear)
	{
		_Closure_0024__24_002D0 capturedLocals = default;
		_Closure_0024__24_002D0 capturedLocals2 = new _Closure_0024__24_002D0(capturedLocals);
		capturedLocals2.clipDepths = clipDepths;
		foreach (XSISMapPoint xsisMapPoint in points.Where([SpecialName] (XSISMapPoint px) => capturedLocals2.clipDepths[px.Index] > 0f))
		{
			float shear2 = ComputeShear(xsisMapPoint);
			if (float.IsNaN(shear2))
			{
				shear2 = -9999f;
			}
			shear[xsisMapPoint.Index] = shear2;
		}
	}

	public void ComputeShearAlongSubsections(ref float[] usShears, ref float[] dsShears)
	{
		usShears = _flowDist.ComputeShearAlongSubsections(_USXS_ID, _usWSInterpData, _usFlowSqrd);
		dsShears = _flowDist.ComputeShearAlongSubsections(_DSXS_ID, _dsWSInterpData, _dsFlowSqrd);
	}

	public float ComputeDV(XSISMapPoint pixel)
	{
		float station = _usStationComputer(pixel.M);
		float station2 = _dsStationComputer(pixel.M);
		float depthTimesVelocity = _flowDist.DepthTimesVelocity(_USXS_ID, _usWSInterpData, station, _usFlow);
		float depthTimesVelocity2 = _flowDist.DepthTimesVelocity(_DSXS_ID, _dsWSInterpData, station2, _dsFlow);
		return Math.Abs(depthTimesVelocity * pixel.Z + depthTimesVelocity2 * (1f - pixel.Z));
	}

	public void ComputeDV(List<XSISMapPoint> points, float[] clipDepths, float[] outDV)
	{
		_Closure_0024__27_002D0 capturedLocals = default;
		_Closure_0024__27_002D0 capturedLocals2 = new _Closure_0024__27_002D0(capturedLocals);
		capturedLocals2.clipDepths = clipDepths;
		foreach (XSISMapPoint xsisMapPoint in points.Where([SpecialName] (XSISMapPoint px) => capturedLocals2.clipDepths[px.Index] > 0f))
		{
			outDV[xsisMapPoint.Index] = ComputeDV(xsisMapPoint);
		}
	}

	public float ComputeDVSquared(XSISMapPoint pixel)
	{
		float station = _usStationComputer(pixel.M);
		float station2 = _dsStationComputer(pixel.M);
		float depthTimesVelocitySquared = _flowDist.DepthTimesVelocitySquared(_USXS_ID, _usWSInterpData, station, _usFlow);
		float depthTimesVelocitySquared2 = _flowDist.DepthTimesVelocitySquared(_DSXS_ID, _dsWSInterpData, station2, _dsFlow);
		return depthTimesVelocitySquared * pixel.Z + depthTimesVelocitySquared2 * (1f - pixel.Z);
	}

	public void ComputeDVSquared(List<XSISMapPoint> points, float[] clipDepths, float[] outDVsq)
	{
		_Closure_0024__29_002D0 capturedLocals = default;
		_Closure_0024__29_002D0 capturedLocals2 = new _Closure_0024__29_002D0(capturedLocals);
		capturedLocals2.clipDepths = clipDepths;
		foreach (XSISMapPoint xsisMapPoint in points.Where([SpecialName] (XSISMapPoint px) => capturedLocals2.clipDepths[px.Index] > 0f))
		{
			outDVsq[xsisMapPoint.Index] = ComputeDVSquared(xsisMapPoint);
		}
	}
}
