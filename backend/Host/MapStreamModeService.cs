using System.IO.Pipes;
using System.Net.Sockets;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.Systems;

namespace Ras.Backend;

/// <summary>
/// Portable host for the retained RasMainWindow stream loop. It reads the
/// original binary streamable-object records and publishes only types accepted
/// by RasMapLayerImporter.TryAddToMap.
/// </summary>
public static class MapStreamModeService
{
    private const int ConnectTimeoutMilliseconds = 15_000;
    private const int MaximumCoordinatesPerObject = 1_000_000;
    private const int MaximumTrianglesPerObject = 2_000_000;
    private const int MaximumTinProfilesPerSession = 512;

    private static readonly IReadOnlyDictionary<string, Type> SupportedStreamTypes =
        new[] { typeof(Polygon), typeof(Polyline), typeof(Topology), typeof(Tin), typeof(Segment) }
            .ToDictionary(type => type.AssemblyQualifiedName
                ?? throw new InvalidOperationException("A supported stream type has no assembly-qualified name."));

    internal static async Task<StreamModeConnectResult> RunAsync(
        StreamModeConnectRequest request,
        Action<StreamModeMapFrame>? onFrame,
        BackendDuplexControlReader? controls,
        CancellationToken cancellationToken = default)
    {
        UnixDomainSocketEndPoint? unixEndpoint = ValidateRequest(request);
        using CancellationTokenSource operationStop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task controlPump = controls is null
            ? Task.CompletedTask
            : PumpControlsAsync(controls, operationStop);

        try
        {
            return await ConnectAndReadAsync(request.PipeName, unixEndpoint, request.SessionID, onFrame, operationStop.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            operationStop.Cancel();
            await controlPump.ConfigureAwait(false);
        }
    }

    private static async Task<StreamModeConnectResult> ConnectAndReadAsync(
        string pipeName,
        UnixDomainSocketEndPoint? unixEndpoint,
        Guid sessionID,
        Action<StreamModeMapFrame>? onFrame,
        CancellationToken cancellationToken)
    {
        using Stream client = await ConnectAsync(pipeName, unixEndpoint, cancellationToken).ConfigureAwait(false);

        long sequence = 0;
        int tinProfileCount = 0;
        while (true)
        {
            using var capture = new StreamModeTinRecordCaptureStream(client);
            IStreamableObject? source = await StreamableObjectHelper.ReadAsync(
                capture, SupportedStreamTypes, cancellationToken).ConfigureAwait(false);
            if (source is null)
            {
                return new StreamModeConnectResult(pipeName, sequence);
            }

            if (source is Tin && tinProfileCount == MaximumTinProfilesPerSession)
                throw new InvalidDataException("The stream contains more TIN profiles than the retained BandedTin renderer can represent.");

            StreamModeMapFrame frame = ConvertSourceObject(sequence, source);
            StreamModeTinAssetReference? tinAsset = null;
            if (source is Tin)
            {
                tinAsset = StreamModeTinAssetStore.Store(sessionID, sequence, capture.CapturedBytes);
                frame = frame with { TinAsset = tinAsset };
                tinProfileCount++;
            }
            try
            {
                if (onFrame is null)
                {
                    if (tinAsset is not null)
                        _ = StreamModeTinAssetStore.Release(tinAsset);
                }
                else
                {
                    onFrame(frame);
                }
            }
            catch
            {
                if (tinAsset is not null)
                    _ = StreamModeTinAssetStore.Release(tinAsset);
                throw;
            }
            sequence = checked(sequence + 1);
        }
    }

    private static async Task<Stream> ConnectAsync(string pipeName, UnixDomainSocketEndPoint? unixEndpoint,
        CancellationToken cancellationToken)
    {
        if (unixEndpoint is not null)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(ConnectTimeoutMilliseconds);
            try
            {
                await socket.ConnectAsync(unixEndpoint, connectTimeout.Token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                throw new TimeoutException("Timed out connecting to the Unix-domain socket.");
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        var namedPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await namedPipe.ConnectAsync(ConnectTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
            return namedPipe;
        }
        catch
        {
            namedPipe.Dispose();
            throw;
        }
    }

    private static async Task PumpControlsAsync(
        BackendDuplexControlReader controls,
        CancellationTokenSource operationStop)
    {
        try
        {
            await foreach (BackendDuplexControlFrame frame in controls.ReadAllAsync(operationStop.Token)
                .ConfigureAwait(false))
            {
                if (frame.Control.TryGetProperty("type", out System.Text.Json.JsonElement type) &&
                    type.ValueKind == System.Text.Json.JsonValueKind.String && type.GetString() == "cancel")
                {
                    operationStop.Cancel();
                    return;
                }

                throw new InvalidDataException("The stream-mode host accepts only a cancel control.");
            }
        }
        catch (OperationCanceledException) when (operationStop.IsCancellationRequested)
        {
        }
        catch
        {
            operationStop.Cancel();
            throw;
        }
    }

    private static UnixDomainSocketEndPoint? ValidateRequest(StreamModeConnectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PipeName))
        {
            throw new ArgumentException("A stream endpoint is required.", nameof(request.PipeName));
        }
        if (request.SessionID == Guid.Empty)
        {
            throw new ArgumentException("A stream session identity is required.", nameof(request.SessionID));
        }

        if (!OperatingSystem.IsWindows())
        {
            if (!Path.IsPathFullyQualified(request.PipeName))
            {
                throw new ArgumentException(
                    "On Unix, stream mode requires an absolute Unix-domain socket path.", nameof(request.PipeName));
            }

            try
            {
                // Let the platform runtime validate its real sockaddr_un path limit.
                return new UnixDomainSocketEndPoint(request.PipeName);
            }
            catch (ArgumentOutOfRangeException error)
            {
                throw new ArgumentException(
                    "The Unix-domain socket path exceeds this platform's endpoint length. Choose a shorter absolute path.",
                    nameof(request.PipeName), error);
            }
            catch (ArgumentException error)
            {
                throw new ArgumentException("The Unix-domain socket path is invalid.", nameof(request.PipeName), error);
            }
        }

        if (request.PipeName.Length > 256 || request.PipeName.IndexOfAny(['\0', '/', '\\']) >= 0)
        {
            throw new ArgumentException("A valid named-pipe name is required.", nameof(request.PipeName));
        }
        return null;
    }

    private static StreamModeMapFrame ConvertSourceObject(long sequence, IStreamableObject source)
    {
        if (source is Polygon polygon)
        {
            StreamModeCoordinate[] coordinates = ReadCoordinates(polygon, includeMeasures: true);
            return new StreamModeMapFrame(sequence, nameof(Polygon), coordinates.Length, coordinates,
                coordinates.Length == 0 ? [] : [Enumerable.Range(0, coordinates.Length).ToArray()], [], true,
                polygon.HasZ, polygon.HasM, FiniteOrNull(polygon.ZFillValue), FiniteOrNull(polygon.MFillValue));
        }

        if (source is Polyline polyline)
        {
            StreamModeCoordinate[] coordinates = ReadCoordinates(polyline, includeMeasures: true);
            return new StreamModeMapFrame(sequence, nameof(Polyline), coordinates.Length, coordinates,
                coordinates.Length == 0 ? [] : [Enumerable.Range(0, coordinates.Length).ToArray()], [], false,
                polyline.HasZ, polyline.HasM, FiniteOrNull(polyline.ZFillValue), FiniteOrNull(polyline.MFillValue));
        }

        if (source is Segment segment)
        {
            StreamModeCoordinate[] coordinates = [ReadCoordinate(segment.A), ReadCoordinate(segment.B)];
            return new StreamModeMapFrame(sequence, nameof(Segment), coordinates.Length, coordinates,
                [[0, 1]], [], false);
        }

        if (source is Topology topology)
        {
            return ConvertTopology(sequence, topology);
        }

        if (source is Tin tin)
        {
            return ConvertTin(sequence, tin);
        }

        throw new NotSupportedException("The source map importer does not accept this streamable object type.");
    }

    private static StreamModeMapFrame ConvertTopology(long sequence, Topology topology)
    {
        var coordinates = new List<StreamModeCoordinate>();
        var paths = new List<int[]>(topology.Arcs.Count);
        foreach (Arc arc in topology.Arcs)
        {
            if (arc.StartNode < 0 || arc.StartNode >= topology.Nodes.Count ||
                arc.EndNode < 0 || arc.EndNode >= topology.Nodes.Count)
            {
                throw new InvalidDataException("A streamed topology arc references a missing endpoint node.");
            }

            int pathStart = coordinates.Count;
            coordinates.Add(ReadCoordinate(topology.Nodes[arc.StartNode].Point));
            if (arc.InternalPoints is not null)
            {
                for (int index = 0; index < arc.InternalPoints.Count; index++)
                {
                    coordinates.Add(ReadCoordinate(arc.InternalPoints[index]));
                }
            }
            coordinates.Add(ReadCoordinate(topology.Nodes[arc.EndNode].Point));
            EnsureCoordinateLimit(coordinates.Count);
            paths.Add(Enumerable.Range(pathStart, coordinates.Count - pathStart).ToArray());
        }

        return new StreamModeMapFrame(sequence, nameof(Topology), coordinates.Count,
            coordinates.ToArray(), paths.ToArray(), [], false);
    }

    private static StreamModeMapFrame ConvertTin(long sequence, Tin tin)
    {
        StreamModeCoordinate[] coordinates = ReadCoordinates(tin, includeMeasures: true);
        var triangles = new List<int[]>(Math.Min(tin.TriangleDefinition.Count, MaximumTrianglesPerObject));
        foreach (TriangleDefinition triangle in tin.TriangleDefinition)
        {
            if (triangle.IsDeleted)
            {
                continue;
            }
            if (triangle.p0 < 0 || triangle.p0 >= coordinates.Length ||
                triangle.p1 < 0 || triangle.p1 >= coordinates.Length ||
                triangle.p2 < 0 || triangle.p2 >= coordinates.Length)
            {
                throw new InvalidDataException("A streamed TIN triangle references a missing coordinate.");
            }
            if (triangles.Count == MaximumTrianglesPerObject)
            {
                throw new InvalidDataException("The streamed TIN contains too many triangles for the native map.");
            }
            triangles.Add([triangle.p0, triangle.p1, triangle.p2]);
        }

        return new StreamModeMapFrame(sequence, nameof(Tin), coordinates.Length,
            coordinates, [], triangles.ToArray(), false, tin.HasZ, tin.HasM,
            FiniteOrNull(tin.ZFillValue), FiniteOrNull(tin.MFillValue));
    }

    private static StreamModeCoordinate[] ReadCoordinates(PointCollection points, bool includeMeasures = false)
    {
        EnsureCoordinateLimit(points.Count);
        var coordinates = new StreamModeCoordinate[points.Count];
        for (int index = 0; index < points.Count; index++)
        {
            Point point = points[index];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                throw new InvalidDataException("The streamed map object contains a non-finite coordinate.");
            }
            double? z = includeMeasures && points.HasZ ? FiniteOrNull(points.Z[index]) : null;
            double? m = includeMeasures && points.HasM ? FiniteOrNull(points.M[index]) : null;
            coordinates[index] = new StreamModeCoordinate(point.X, point.Y, z, m);
        }
        return coordinates;
    }

    private static double? FiniteOrNull(double value) => double.IsFinite(value) ? value : null;

    private static StreamModeCoordinate ReadCoordinate(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new InvalidDataException("The streamed map object contains a non-finite coordinate.");
        }
        return new StreamModeCoordinate(point.X, point.Y);
    }

    private static void EnsureCoordinateLimit(int coordinateCount)
    {
        if (coordinateCount > MaximumCoordinatesPerObject)
        {
            throw new InvalidDataException("The streamed map object contains too many coordinates.");
        }
    }
}
