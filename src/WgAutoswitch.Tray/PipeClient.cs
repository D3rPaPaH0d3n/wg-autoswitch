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
            // Längerer Timeout damit kurze Engpässe (Service bedient gerade einen anderen
            // Request, Pipe-Server zwischen zwei Connections) nicht sofort als Fehler
            // hochkommen.
            await pipe.ConnectAsync(5000, ct);

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
