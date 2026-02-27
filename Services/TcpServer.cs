using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// TCP Server that:
///   - Echoes (loopbacks) any received bytes back to the client
///   - If the client sends a 5-byte trigger starting with {10, 5, 0, ...},
///     the server asynchronously streams a large list of byte arrays back
///     over a configurable duration (default 5 s) without dropping a single byte.
///
/// Byte ordering is guaranteed because all sends go through a single
/// Channel<byte[]> -> dedicated writer loop, so concurrent loopback and
/// burst traffic never interleave or collide.
/// </summary>
public class TcpServer
{
    // ── Trigger config ────────────────────────────────────────────────────────
    private static readonly byte[] TriggerPrefix = { 10, 5, 0 };   // first 3 bytes
    private const int TriggerLength = 5;                             // total trigger size

    // ── Burst-send config ─────────────────────────────────────────────────────
    private const int BurstTargetBytes  = 4200;   // minimum bytes to send in burst
    private const int BurstDurationMs   = 5_000;  // spread over this many ms (0 = as fast as possible)
    private const int BurstChunkSize    = 64;     // size of each generated chunk

    // ─────────────────────────────────────────────────────────────────────────
    public static async Task Main(string[] args)
    {
        int port = 9000;
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"[Server] Listening on port {port}...");

        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            Console.WriteLine($"[Server] Client connected: {client.Client.RemoteEndPoint}");
            // Handle each client on its own task — don't await so we keep accepting.
            _ = HandleClientAsync(client);
        }
    }

    private static async Task HandleClientAsync(TcpClient tcpClient)
    {
        using TcpClient client = tcpClient;
        using NetworkStream stream = client.GetStream();

        // ── Single-writer channel ─────────────────────────────────────────────
        // All byte arrays that need to be sent are queued here.
        // One dedicated writer loop drains the channel in order, so loopback
        // responses and burst data never race each other on the socket.
        var sendChannel = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

        using var cts = new CancellationTokenSource();

        // Start the writer loop first
        Task writerTask = WriterLoopAsync(stream, sendChannel.Reader, cts.Token);

        // Read loop
        try
        {
            byte[] readBuffer = new byte[4096];

            while (true)
            {
                int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, cts.Token);
                if (bytesRead == 0)
                {
                    Console.WriteLine("[Server] Client disconnected.");
                    break;
                }

                // Copy only what was actually read
                byte[] received = new byte[bytesRead];
                Buffer.BlockCopy(readBuffer, 0, received, 0, bytesRead);

                Console.WriteLine($"[Server] Received {bytesRead} byte(s): {BytesToHex(received)}");

                if (IsTrigger(received))
                {
                    Console.WriteLine("[Server] Trigger detected — starting async burst send.");
                    // Fire-and-forget the burst; it queues into the same channel
                    // so ordering with any future loopbacks is still preserved.
                    _ = EnqueueBurstAsync(sendChannel.Writer, BurstTargetBytes, BurstDurationMs);
                }
                else
                {
                    // Loopback: echo the bytes straight back
                    await sendChannel.Writer.WriteAsync(received, cts.Token);
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[Server] Read error: {ex.Message}");
        }
        finally
        {
            // Signal writer to finish and wait for it to drain
            sendChannel.Writer.TryComplete();
            cts.Cancel();
            await writerTask;
            Console.WriteLine("[Server] Client handler exited.");
        }
    }

    // ── Dedicated writer loop ─────────────────────────────────────────────────
    // Single consumer — guarantees in-order, non-interleaved TCP writes.
    private static async Task WriterLoopAsync(
        NetworkStream stream,
        ChannelReader<byte[]> reader,
        CancellationToken ct)
    {
        try
        {
            await foreach (byte[] data in reader.ReadAllAsync(ct))
            {
                await stream.WriteAsync(data, 0, data.Length, ct);
                await stream.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* clean exit */ }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Write error: {ex.Message}");
        }
    }

    // ── Burst generator ───────────────────────────────────────────────────────
    /// <param name="targetBytes">Total bytes to send in this burst.</param>
    /// <param name="spreadMs">Spread the send over this many milliseconds.
    ///   Pass 0 to send as fast as possible.</param>
    private static async Task EnqueueBurstAsync(
        ChannelWriter<byte[]> writer,
        int targetBytes,
        int spreadMs)
    {
        List<byte[]> chunks = GenerateBurstData(targetBytes, BurstChunkSize);

        int totalChunks  = chunks.Count;
        int totalSent    = 0;
        long startMs     = Environment.TickCount64;

        // How long to wait between chunks so we fill spreadMs evenly
        int delayPerChunk = (spreadMs > 0 && totalChunks > 1)
            ? spreadMs / (totalChunks - 1)
            : 0;

        Console.WriteLine($"[Server] Burst: {totalChunks} chunks × {BurstChunkSize} B " +
                          $"= {totalChunks * BurstChunkSize} B over ~{spreadMs} ms");

        for (int i = 0; i < chunks.Count; i++)
        {
            await writer.WriteAsync(chunks[i]);
            totalSent += chunks[i].Length;

            if (delayPerChunk > 0 && i < totalChunks - 1)
                await Task.Delay(delayPerChunk);
        }

        long elapsed = Environment.TickCount64 - startMs;
        Console.WriteLine($"[Server] Burst complete: {totalSent} bytes in {elapsed} ms.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static List<byte[]> GenerateBurstData(int targetBytes, int chunkSize)
    {
        var list  = new List<byte[]>();
        int total = 0;
        byte counter = 0;

        while (total < targetBytes)
        {
            int size = Math.Min(chunkSize, targetBytes - total);
            var chunk = new byte[size];
            for (int i = 0; i < size; i++)
                chunk[i] = counter++;          // incrementing pattern so client can validate
            list.Add(chunk);
            total += size;
        }
        return list;
    }

    private static bool IsTrigger(byte[] data)
    {
        if (data.Length != TriggerLength) return false;
        for (int i = 0; i < TriggerPrefix.Length; i++)
            if (data[i] != TriggerPrefix[i]) return false;
        return true;
    }

    private static string BytesToHex(byte[] data)
    {
        if (data.Length <= 16)
            return BitConverter.ToString(data);
        return BitConverter.ToString(data, 0, 16) + $"... (+{data.Length - 16} more)";
    }
}
