using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace ConcurrencyPools
{
    public abstract class BoundedPool
    {
        protected interface IWorker
        {
            public event EventHandler<Exception>? ExceptionOccurred;

            public void Start();
            public void Cancel();
        }

        public abstract class Work
        {
            public abstract void Execute();
        }

        public delegate void WorkInvocation();

        protected readonly CancellationTokenSource CancellationTokenSource;
        protected readonly ManualResetEventSlim ModifyWorkersReset;
        protected readonly List<IWorker> Workers;
        protected readonly ChannelReader<WorkInvocation> WorkReader;
        protected readonly ChannelWriter<WorkInvocation> WorkWriter;

        public event EventHandler<Exception>? ExceptionOccurred;

        public int WorkerCount => Workers.Count;

        protected BoundedPool()
        {
            CancellationTokenSource = new CancellationTokenSource();
            ModifyWorkersReset = new ManualResetEventSlim(true);

            Channel<WorkInvocation> workChannel = Channel.CreateUnbounded<WorkInvocation>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });

            WorkWriter = workChannel.Writer;
            WorkReader = workChannel.Reader;

            Workers = new List<IWorker>();
        }

        protected abstract IWorker CreateWorker();

        public void QueueWork(WorkInvocation workInvocation)
        {
            ModifyWorkersReset.Wait(CancellationTokenSource.Token);

            if (WorkerCount == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(BoundedAsyncPool)} has no active workers. Call {nameof(DefaultThreadPoolSize)}() or {nameof(ModifyThreadPoolSize)}().");
            }
            else if (!WorkWriter.TryWrite(workInvocation)) throw new Exception("Failed to queue work.");
        }

        public void QueueWork(Work work)
        {
            ModifyWorkersReset.Wait(CancellationTokenSource.Token);

            if (WorkerCount == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(BoundedAsyncPool)} has no active workers. Call {nameof(DefaultThreadPoolSize)}() or {nameof(ModifyThreadPoolSize)}().");
            }
            else if (!WorkWriter.TryWrite(work.Execute)) throw new Exception("Failed to queue work.");
        }

        public void DefaultThreadPoolSize() => ModifyThreadPoolSize((uint)Math.Max(1, Environment.ProcessorCount - 2));

        /// <summary>
        ///     Modifies <see cref="BoundedAsyncPool" />'s total number of worker threads.
        /// </summary>
        /// <param name="size">Desired size of thread pool.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <see cref="size" /> is less than 1.</exception>
        public void ModifyThreadPoolSize(uint size)
        {
            if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), "Size must be greater than 1.");
            else if (size == WorkerCount) return;

            ModifyWorkersReset.Wait(CancellationTokenSource.Token);
            ModifyWorkersReset.Reset();

            if (WorkerCount > size)
            {
                for (int index = WorkerCount - 1; index >= size; index--)
                {
                    IWorker worker = Workers[index];
                    worker.Cancel();
                    worker.ExceptionOccurred -= ExceptionOccurredCallback;

                    Workers.RemoveAt(index);
                }
            }
            else
            {
                for (int index = WorkerCount; index < size; index++)
                {
                    IWorker worker = CreateWorker();
                    worker.ExceptionOccurred += ExceptionOccurredCallback;
                    worker.Start();

                    Workers.Add(worker);
                }
            }

            ModifyWorkersReset.Set();
        }

        public void Stop() => CancellationTokenSource.Cancel();

        public void Abort(bool abort)
        {
            if (!abort) return;

            ModifyWorkersReset.Wait(CancellationTokenSource.Token);
            ModifyWorkersReset.Reset();

            foreach (IWorker worker in Workers) worker.Cancel();

            Workers.Clear();

            _Active = null;

            ModifyWorkersReset.Set();
        }

        private void ExceptionOccurredCallback(object? sender, Exception exception) => ExceptionOccurred?.Invoke(sender, exception);

        #region Active Pool State

        private static BoundedPool? _Active;

        public static BoundedPool Active
        {
            get
            {
                if (_Active is not null) return _Active;
                else throw new NullReferenceException(nameof(Active));
            }
            protected set
            {
                if (_Active is null) _Active = value;
                else throw new InvalidOperationException("Cannot assign active pool more than once.");
            }
        }

        #endregion
    }
}
