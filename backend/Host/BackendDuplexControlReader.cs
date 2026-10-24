using System.Text.Json;
using System.Threading.Channels;

namespace Ras.Backend;

internal sealed record BackendDuplexControlFrame(string Id, ulong ControlSequence, JsonElement Control);

/// <summary>Owns the child's single stdin reader after the initial request line.</summary>
internal sealed class BackendDuplexControlReader : IAsyncDisposable
{
    private readonly string requestID;
    private readonly TextReader input;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<BackendDuplexControlFrame> controls = Channel.CreateUnbounded<BackendDuplexControlFrame>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly Task pump;
    private ulong lastSequence;

    public BackendDuplexControlReader(string requestID, TextReader input)
    {
        this.requestID = requestID;
        this.input = input;
        pump = Task.Run(ReadControlsAsync);
    }

    public IAsyncEnumerable<BackendDuplexControlFrame> ReadAllAsync(CancellationToken cancellationToken = default) =>
        controls.Reader.ReadAllAsync(cancellationToken);

    private async Task ReadControlsAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                string? line = await input.ReadLineAsync(stop.Token).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("id", out JsonElement idValue) || idValue.GetString() != requestID ||
                    !root.TryGetProperty("controlSequence", out JsonElement sequenceValue) ||
                    !sequenceValue.TryGetUInt64(out ulong sequence) || sequence <= lastSequence ||
                    !root.TryGetProperty("control", out JsonElement controlValue) ||
                    controlValue.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The backend received an invalid or out-of-order duplex control frame.");
                lastSequence = sequence;
                await controls.Writer.WriteAsync(new(requestID, sequence, controlValue.Clone()), stop.Token)
                    .ConfigureAwait(false);
            }
            controls.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            controls.Writer.TryComplete();
        }
        catch (Exception error)
        {
            controls.Writer.TryComplete(error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { await pump.ConfigureAwait(false); }
        catch { }
        stop.Dispose();
    }
}
