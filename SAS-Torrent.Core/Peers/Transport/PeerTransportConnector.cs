namespace SAS_Torrent.Core.Peers.Transport;

public static class PeerTransportConnector
{
    public static async Task<PeerTransportConnection?> ConnectAnyAsync(
        IReadOnlyList<IPeerTransport> transports,
        string ip,
        int port,
        TimeSpan timeout,
        TimeSpan secondaryDelay,
        CancellationToken ct)
    {
        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = new List<Task<PeerTransportConnection?>>();
        var next = 0;

        try
        {
            while (true)
            {
                if (pending.Count == 0)
                {
                    if (next >= transports.Count)
                        return null;

                    pending.Add(transports[next++].ConnectAsync(ip, port, timeout, raceCts.Token));
                    continue;
                }

                Task? delay = null;
                if (next < transports.Count)
                    delay = Task.Delay(secondaryDelay, raceCts.Token);

                var waiting = delay == null
                    ? pending.Cast<Task>().ToList()
                    : pending.Cast<Task>().Append(delay).ToList();

                var finished = await Task.WhenAny(waiting);

                if (finished == delay)
                {
                    pending.Add(transports[next++].ConnectAsync(ip, port, timeout, raceCts.Token));
                    continue;
                }

                var completed = (Task<PeerTransportConnection?>)finished;
                pending.Remove(completed);

                var connection = await completed;
                if (connection != null)
                {
                    raceCts.Cancel();
                    return connection;
                }
            }
        }
        finally
        {
            raceCts.Cancel();
            await DisposeLosersAsync(pending);
        }
    }

    private static async Task DisposeLosersAsync(IEnumerable<Task<PeerTransportConnection?>> attempts)
    {
        foreach (var attempt in attempts)
        {
            try
            {
                var connection = await attempt;
                connection?.Dispose();
            }
            catch { }
        }
    }
}
