using System.IO.Pipes;
using System.Text.Json;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Tray;

public class PipeClient
{
    public async Task<CommandResponse> SendAsync(Command cmd, CancellationToken ct)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Paths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(2000, ct);

            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(cmd));
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line))
                return new CommandResponse(false, "Leere Antwort vom Service", null);

            return JsonSerializer.Deserialize<CommandResponse>(line)
                   ?? new CommandResponse(false, "Antwort nicht parsebar", null);
        }
        catch (TimeoutException)
        {
            return new CommandResponse(false, "Service läuft nicht oder ist nicht erreichbar", null);
        }
        catch (Exception ex)
        {
            return new CommandResponse(false, ex.Message, null);
        }
    }
}
