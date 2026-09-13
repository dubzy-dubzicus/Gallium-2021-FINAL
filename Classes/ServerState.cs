using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Gallium2021.Classes;

public class ServerState
{
    public DateTime? MaintenanceDeadline;
    public readonly ConcurrentDictionary<int, DateTime> LastImageUpload = new();
    public readonly List<Channel<string>> LogSubscribers = new();
    public readonly object LogLock = new();

    public int MaintenanceMinutesRemaining()
    {
        if (MaintenanceDeadline == null) return 0;
        var remaining = (MaintenanceDeadline.Value - DateTime.UtcNow).TotalMinutes;
        return Math.Max(0, (int)remaining);
    }

    public void PublishLog(string entry)
    {
        lock (LogLock)
        {
            foreach (var channel in LogSubscribers)
                channel.Writer.TryWrite(entry);
        }
    }
}
