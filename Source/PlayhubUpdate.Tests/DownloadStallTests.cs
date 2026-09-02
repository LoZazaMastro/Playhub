using System.Net;
using System.Net.Http;
using System.Threading.Channels;
using Playhub.Services;

namespace UpdateTests;

internal static class DownloadStallTests
{
    internal static async Task RunAsync(Func<string, Func<Task>, Task> test, Action<bool, string> require,
        PlayhubUpdateService.UpdateInfo info, byte[] payload)
    {
        void VerifyCleanup(ManualTimeProvider clock)
        {
            require(!Directory.GetFiles(AppPaths.DownloadsRoot, "*.part", SearchOption.AllDirectories).Any(),
                "partial download remained");
            var destination = Path.Combine(AppPaths.DownloadsRoot, "updates", info.AssetName!);
            require(File.ReadAllBytes(destination).SequenceEqual(payload), "last valid installer changed");
            require(clock.AllTimersDisposed, "download timeout timer remained active");
        }

        void ReplyWith(ControlledReadStream stream) => State.Reply = (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        };

        await test("download headers time out after 60 seconds", async () =>
        {
            var clock = new ManualTimeProvider();
            var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            State.Reply = async (_, token) =>
            {
                entered.SetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("stalled headers resumed without cancellation");
            };
            var download = new PlayhubUpdateService(clock).DownloadInstallerAsync(info);
            var requestToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(59));
            require(!requestToken.IsCancellationRequested && !download.IsCompleted, "headers timed out before 60 seconds");
            clock.Advance(TimeSpan.FromSeconds(1));
            await ExpectCancellationAsync(download);
            require(requestToken.IsCancellationRequested, "header request was not cancelled");
            VerifyCleanup(clock);
        });

        await test("first body read has a fresh 60 seconds after slow headers", async () =>
        {
            var clock = new ManualTimeProvider();
            using var stream = new ControlledReadStream();
            var headers = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            State.Reply = (_, token) => headers.Task.WaitAsync(token);
            var download = new PlayhubUpdateService(clock).DownloadInstallerAsync(info);
            clock.Advance(TimeSpan.FromSeconds(59));
            headers.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
            var read = await stream.NextReadAsync();
            clock.Advance(TimeSpan.FromSeconds(59));
            require(!read.Token.IsCancellationRequested && !download.IsCompleted, "header time consumed body budget");
            clock.Advance(TimeSpan.FromSeconds(1));
            await ExpectCancellationAsync(download);
            require(read.Token.IsCancellationRequested && stream.IsDisposed, "stalled response stream remained open");
            VerifyCleanup(clock);
        });

        await test("mid-body stall cancels and removes a written partial file", async () =>
        {
            var clock = new ManualTimeProvider();
            using var stream = new ControlledReadStream();
            ReplyWith(stream);
            var download = new PlayhubUpdateService(clock).DownloadInstallerAsync(info);
            (await stream.NextReadAsync()).Complete(payload[..32768]);
            var read = await stream.NextReadAsync();
            require(Directory.GetFiles(AppPaths.DownloadsRoot, "*.part", SearchOption.AllDirectories).Length == 1,
                "test did not reach a partially written download");
            clock.Advance(TimeSpan.FromSeconds(60));
            await ExpectCancellationAsync(download);
            require(read.Token.IsCancellationRequested && stream.IsDisposed, "stalled read was not cancelled and disposed");
            VerifyCleanup(clock);
        });

        await test("progressing download can exceed 60 seconds in total", async () =>
        {
            var clock = new ManualTimeProvider();
            using var stream = new ControlledReadStream();
            ReplyWith(stream);
            double fraction = 0;
            var download = new PlayhubUpdateService(clock).DownloadInstallerAsync(info,
                new InlineProgress<PlayhubUpdateService.DownloadProgress>(value => fraction = value.Fraction));
            for (var offset = 0; offset < payload.Length; offset += 16384)
            {
                var read = await stream.NextReadAsync();
                clock.Advance(TimeSpan.FromSeconds(45));
                require(!read.Token.IsCancellationRequested, "progressing download hit an overall deadline");
                read.Complete(payload[offset..Math.Min(offset + 16384, payload.Length)]);
            }
            (await stream.NextReadAsync()).Complete(Array.Empty<byte>());
            var file = await download.WaitAsync(TimeSpan.FromSeconds(5));
            require(File.ReadAllBytes(file).SequenceEqual(payload) && fraction == 1, "slow download was corrupted or incomplete");
            require(stream.IsDisposed, "successful response stream remained open");
            VerifyCleanup(clock);
        });

        await test("caller cancellation interrupts pending headers", async () =>
        {
            var clock = new ManualTimeProvider();
            using var caller = new CancellationTokenSource();
            var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            State.Reply = async (_, token) =>
            {
                entered.SetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("cancelled headers resumed");
            };
            var download = new PlayhubUpdateService(clock).DownloadInstallerAsync(info, cancellationToken: caller.Token);
            var requestToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            caller.Cancel();
            await ExpectCancellationAsync(download);
            require(requestToken.IsCancellationRequested, "caller cancellation did not reach headers");
            VerifyCleanup(clock);
        });

        await test("caller cancellation interrupts body and removes partial file", async () =>
        {
            var clock = new ManualTimeProvider();
            using var caller = new CancellationTokenSource();
            using var stream = new ControlledReadStream();
            ReplyWith(stream);
            var download = new PlayhubUpdateService(clock).DownloadInstallerAsync(info, cancellationToken: caller.Token);
            (await stream.NextReadAsync()).Complete(payload[..32768]);
            var read = await stream.NextReadAsync();
            caller.Cancel();
            await ExpectCancellationAsync(download);
            require(read.Token.IsCancellationRequested && stream.IsDisposed, "caller cancellation did not stop body read");
            VerifyCleanup(clock);
        });
    }

    private static async Task ExpectCancellationAsync(Task<string> download)
    {
        try { await download.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("download completed instead of cancelling");
    }

    private sealed class ControlledReadStream : Stream
    {
        private readonly Channel<PendingRead> _reads = Channel.CreateUnbounded<PendingRead>();
        public bool IsDisposed { get; private set; }
        public Task<PendingRead> NextReadAsync() => _reads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = new PendingRead(cancellationToken);
            _reads.Writer.TryWrite(read);
            var chunk = await read.Chunk.Task.WaitAsync(cancellationToken);
            chunk.AsMemory().CopyTo(buffer);
            return chunk.Length;
        }
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class PendingRead(CancellationToken token)
    {
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource<byte[]> Chunk { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(byte[] bytes) => Chunk.SetResult(bytes);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = new();
        private long _ticks;
        public bool AllTimersDisposed { get { lock (_gate) return _timers.Count > 0 && _timers.All(timer => timer.Disposed); } }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                var timer = new ManualTimer(this, callback, state);
                timer.Change(dueTime, period);
                _timers.Add(timer);
                return timer;
            }
        }

        public void Advance(TimeSpan amount)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _ticks += amount.Ticks;
                due = _timers.Where(timer => !timer.Disposed && timer.Due <= _ticks).ToList();
                foreach (var timer in due) timer.Due = long.MaxValue;
            }
            foreach (var timer in due) timer.Fire();
        }

        private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
        {
            public long Due { get; set; }
            public bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException("Only one-shot timers are expected.");
                lock (clock._gate)
                {
                    if (Disposed) return false;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock._ticks + dueTime.Ticks;
                    return true;
                }
            }
            public void Fire() => callback(state);
            public void Dispose() { lock (clock._gate) Disposed = true; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
