using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterStack.Logs
{
    /// <summary>
    /// The Drain class is responsible for maintaining a queue of log events that need
    /// to be delivered to the server and periodically forwarding them to the server
    /// in batches.
    /// </summary>
    public sealed class Drain
    {
        private readonly int maxBatchSize;
        private readonly Client client;
        private readonly TimeSpan period;

        private readonly Task runningTask;
        private volatile Task sendTask = Task.CompletedTask;

        private readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        private readonly CancellationTokenSource cancellationTokenSource;

        /// <summary>
        /// Initializes a Better Stack Logs drain and starts periodic logs delivery.
        /// </summary>
        public Drain(
            Client client,
            TimeSpan? period = null,
            int maxBatchSize = 1000,
            CancellationToken? cancellationToken = null
        )
        {
            this.client = client;
            this.period = period ?? TimeSpan.FromMilliseconds(250);
            this.maxBatchSize = maxBatchSize;
            this.cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? CancellationToken.None);

            runningTask = Task.Run(run);
        }

        /// <summary>
        /// Adds a single log event to a queue. The log event will be delivered later in a batch.
        /// This method will throw an exception if the Drain is stopped.
        /// </summary>
        public void Enqueue(string log)
        {
            if (cancellationTokenSource.IsCancellationRequested) throw new DrainIsClosedException();

            queue.Enqueue(log);
        }

        /// <summary>
        /// Stops periodic logs delivery. The returned task will complete once the queue is flushed.
        /// </summary>
        public async Task Stop()
        {
            cancellationTokenSource.Cancel();
            await runningTask;
        }

        /// <summary>
        /// Waits for the pending queue to be flushed.
        /// </summary>
        public async Task Flush()
        {
            for (int i = 0; i < 3; ++i)
            {
                if (queue.IsEmpty)
                    break;
                await Task.Delay(period);
                await sendTask;
            }
            await Task.Delay(period);
            await sendTask;
        }

        private async Task run() {
            var nextDelay = period;

            var nextBatch = new List<string>();

            // XXX: We want the loop to run at least once, even if we stop
            //      the drain before the we manage to reach this point.
            do {
                if (nextDelay > TimeSpan.Zero) {
                    try {
                        await Task.Delay(nextDelay, cancellationTokenSource.Token);
                    } catch (OperationCanceledException) {
                        // finish the rest of the loop to flush everything
                    }
                }

                var start = DateTime.UtcNow;
                while (!queue.IsEmpty)
                {
                    nextBatch.Clear();
                    while (nextBatch.Count < maxBatchSize && queue.TryDequeue(out var log))
                        nextBatch.Add(log);

                    sendTask = client.Send(nextBatch);
                    await sendTask;
                    sendTask = Task.CompletedTask;
                }
                var flushDuration = DateTime.UtcNow - start;
                nextDelay = period - flushDuration;
            } while (!cancellationTokenSource.IsCancellationRequested);
        }
    }
}
