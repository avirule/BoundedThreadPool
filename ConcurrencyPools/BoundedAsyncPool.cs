using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ConcurrentPools
{
    public static class BoundedAsyncPool
    {
        public delegate void WorkInvocation();

        public abstract class Work
        {
            public abstract void Execute();
        }

        private class Worker
        {
            private readonly CancellationTokenSource _InternalCancellation;
            private readonly CancellationToken _CompoundToken;
            private readonly ChannelReader<WorkInvocation> _WorkChannel;

            public Worker(CancellationToken cancellationToken, ChannelReader<WorkInvocation> workChannel)
            {
                _InternalCancellation = new CancellationTokenSource();
                _CompoundToken = CancellationTokenSource.CreateLinkedTokenSource(_InternalCancellation.Token, cancellationToken).Token;
                _WorkChannel = workChannel;
            }

            private async Task Runtime()
            {
                while (!_CompoundToken.IsCancellationRequested)
                {
                    await _WorkChannel.WaitToReadAsync(_CompoundToken);
                    (await _WorkChannel.ReadAsync(_CompoundToken)).Invoke();
                }
            }

            public void Cancel() => _InternalCancellation.Cancel();
        }

        private static readonly CancellationTokenSource _CancellationTokenSource;
        private static readonly ManualResetEventSlim _ModifySizeReset;
        private static readonly ChannelWriter<WorkInvocation> _WorkWriter;
        private static readonly ChannelReader<WorkInvocation> _WorkReader;
        private static readonly List<Worker> _Workers;

        public static int WorkerCount => _Workers.Count;

        static BoundedAsyncPool()
        {
            _CancellationTokenSource = new CancellationTokenSource();
            _ModifySizeReset = new ManualResetEventSlim(true);

            Channel<WorkInvocation> workChannel = Channel.CreateUnbounded<WorkInvocation>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true
            });
            _WorkWriter = workChannel.Writer;
            _WorkReader = workChannel.Reader;

            _Workers = new List<Worker>();
        }

        public static void QueueWork(WorkInvocation workInvocation)
        {
            _ModifySizeReset.Wait(_CancellationTokenSource.Token);

            if (WorkerCount == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(BoundedAsyncPool)} has no active workers. Call {nameof(DefaultThreadPoolSize)}() or {nameof(ModifyThreadPoolSize)}().");
            }
            else if (!_WorkWriter.TryWrite(workInvocation))
            {
                throw new Exception("Failed to queue work.");
            }
        }

        public static void QueueWork(Work work)
        {
            _ModifySizeReset.Wait(_CancellationTokenSource.Token);

            if (WorkerCount == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(BoundedAsyncPool)} has no active workers. Call {nameof(DefaultThreadPoolSize)}() or {nameof(ModifyThreadPoolSize)}().");
            }
            else if (!_WorkWriter.TryWrite(work.Execute))
            {
                throw new Exception("Failed to queue work.");
            }
        }

        public static void DefaultThreadPoolSize() => ModifyThreadPoolSize(Math.Max(1, Environment.ProcessorCount - 2));

        /// <summary>
        ///     Modifies <see cref="BoundedAsyncPool" />'s total number of worker threads.
        /// </summary>
        /// <param name="size">Desired size of thread pool.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <see cref="size" /> is less than 1.</exception>
        public static void ModifyThreadPoolSize(int size)
        {
            if (size < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be greater than 1.");
            }
            else if (size == WorkerCount)
            {
                return;
            }

            _ModifySizeReset.Wait(_CancellationTokenSource.Token);
            _ModifySizeReset.Reset();

            if (WorkerCount > size)
            {
                for (int index = WorkerCount - 1; index >= size; index--)
                {
                    _Workers[index].Cancel();
                    _Workers.RemoveAt(index);
                }
            }
            else
            {
                for (int index = WorkerCount; index < size; index++)
                {
                    _Workers.Add(new Worker(_CancellationTokenSource.Token, _WorkReader));
                }
            }

            _ModifySizeReset.Set();
        }

        public static void Stop() => _CancellationTokenSource.Cancel();

        public static void Abort(bool abort)
        {
            if (!abort)
            {
                return;
            }

            _ModifySizeReset.Wait(_CancellationTokenSource.Token);
            _ModifySizeReset.Reset();

            foreach (Worker worker in _Workers)
            {
                worker.Cancel();
            }

            _Workers.Clear();

        }
    }
}