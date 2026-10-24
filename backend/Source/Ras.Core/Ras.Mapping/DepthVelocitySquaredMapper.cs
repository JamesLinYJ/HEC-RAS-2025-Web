using System;
using Geospatial.Vectors.Meshes;
using Ras.Output;
using Utility.Memory;

namespace Ras.Mapping;

/// <summary>Computes the source Depth * Velocity^2 hazard field from each selected Result profile.</summary>
public sealed class DepthVelocitySquaredMapper : Mapper
{
    private readonly VelocityMapper _velocities;

    public override MapTypes MapType => MapTypes.DepthTimesVelocitySquared;

    public DepthVelocitySquaredMapper(IHorizontalOutputBlock block, MeshPropertyTables[] tables, MapperCache cache = null)
        : base(block, tables, cache)
    {
        _velocities = new VelocityMapper(block, tables, MapperCache);
    }

    public override void Compute(int profileIndex, GeometryMapPoints mapPoints, Memory<float> terrain, Memory<float> output)
    {
        if (mapPoints.InputCount < 0)
            throw new ArgumentException("Input points <= 0");
        if (output.Length != mapPoints.InputCount)
            throw new ArgumentException("Output buffer size does not match the number of input points.");

        Memory<float> velocity;
        using (SharedMemoryPool<float>.RentNoFill(mapPoints.InputCount, out velocity))
        {
            // VelocityMapper returns magnitude and the matching depth from the same source profile.
            _velocities.Compute(profileIndex, mapPoints, terrain, velocity, output);
            Span<float> velocityValues = velocity.Span;
            Span<float> depthValues = output.Span;
            for (int index = 0; index < mapPoints.InputCount; index++)
            {
                float depth = depthValues[index];
                float speed = velocityValues[index];
                if (depth == -9999f || speed == -9999f)
                {
                    depthValues[index] = -9999f;
                    continue;
                }
                depthValues[index] = depth * speed * speed;
            }
        }
    }
}
