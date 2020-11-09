using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace ConcurrencyPools
{
    public abstract class BoundedPool
    {
        protected interface IWorker
        {
            /// <summary>
            ///     Exception callback fired when an exception occurs in the worker.
            /// </summary>
            public event EventHandler<Exception>? ExceptionOccurred;

            /// <summary>
            ///     Start the worker executing work.
            /// </summary>
            public void Start();

            /// <summary>
            ///     Safely cancel the worker when it next observes the internal cancellation token.
            /// </summary>
            /// <remarks>
            ///     If the worker is currently executing work, this means it will shut down after
            ///     that work finishes.
            /// </remarks>
            public void Cancel();

            /// <summary>
            ///     Exits the running context as aggressively as possible.
            /// </summary>
            /// <remarks>
            ///     Only use this when shutting down the thread pool in an emergency.
            /// </remarks>
            public void Abort();
        }

        public abstract class Work
        {
            /// <summary>
            ///     Executes the work.
            /// </summary>
            /// <remarks>
            ///     This is the method executed on the pool.
            /// </remarks>
            public abstract void Execute();
        }

        /// <summary>
        ///     Used to wrap the <see cref="Work.Execute()" /> method and provide a common
        ///     type for the pool workers to consume.
        /// </summary>
        public delegate void WorkInvocation();

        protected readonly CancellationTokenSource CancellationTokenSource;
        protected readonly ManualResetEventSlim ModifyWorkerGroupReset;

        /// <Remarks>
        ///     It may seem odd to use a <see cref="List{T}" /> instead of something such like a
        ///     <see cref="ConcurrentBag{T}" />, but this is because the worker group shouldn't be
        ///     modified in a multi-threaded context. An internal reset event is provided to ensure that
        ///     while the worker group is modified, no other pool actions can occur.
        /// </Remarks>
        protected readonly List<IWorker> WorkerGroup;

        protected readonly ChannelReader<WorkInvocation> WorkReader;
        protected readonly ChannelWriter<WorkInvocation> WorkWriter;

        /// <summary>
        ///     Total count of active workers.
        /// </summary>
        public virtual int WorkerCount => WorkerGroup.Count;

        public CancellationToken CancellationToken => CancellationTokenSource.Token;

        protected BoundedPool(bool singleReader, bool singleWriter)
        {
            CancellationTokenSource = new CancellationTokenSource();
            ModifyWorkerGroupReset = new ManualResetEventSlim(true);

            Channel<WorkInvocation> workChannel = Channel.CreateUnbounded<WorkInvocation>(new UnboundedChannelOptions
            {
                SingleReader = singleReader,
                SingleWriter = singleWriter
            });

            WorkWriter = workChannel.Writer;
            WorkReader = workChannel.Reader;

            WorkerGroup = new List<IWorker>();
        }

        /// <summary>
        ///     Exception callback fired when an exception occurs in any of the workers.
        /// </summary>
        public event EventHandler<Exception>? ExceptionOccurred;

        /// <summary>
        ///     Creates a worker for the pool.
        /// </summary>
        /// <returns>An initialized <see cref="IWorker" /> to add to the worker group.</returns>
        protected abstract IWorker CreateWorker();

        /// <summary>
        ///     Queues a <see cref="WorkInvocation" /> on the pool.
        /// </summary>
        /// <param name="workInvocation">The <see cref="WorkInvocation" /> to be queued.</param>
        /// <exception cref="InvalidOperationException">Thrown when the pool has no active workers.</exception>
        /// <exception cref="Exception">Thrown when adding to the work queue fails. This exception is rare.</exception>
        public virtual void QueueWork(WorkInvocation workInvocation)
        {
            // ensure the worker group isn't being modified
            ModifyWorkerGroupReset.Wait(CancellationToken);

            if (WorkerCount == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(BoundedAsyncPool)} has no active workers. Call {nameof(DefaultPoolSize)}() or {nameof(ModifyPoolSize)}().");
            }
            else if (!WorkWriter.TryWrite(workInvocation)) throw new Exception("Failed to queue work.");
        }

        public virtual void QueueWork(Work work)
        {
            // ensure the worker group isn't being modified
            ModifyWorkerGroupReset.Wait(CancellationToken);

            if (WorkerCount == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(BoundedAsyncPool)} has no active workers. Call {nameof(DefaultPoolSize)}() or {nameof(ModifyPoolSize)}().");
            }
            else if (!WorkWriter.TryWrite(work.Execute)) throw new Exception("Failed to queue work.");
        }

        /// <summary>
        ///     Initialize the pool with the default size.
        /// </summary>
        /// <remarks>
        ///     The default size is the maximum of: <see cref="Environment.ProcessorCount" /> -or- 1.
        /// </remarks>
        public void DefaultPoolSize() => ModifyPoolSize((uint)Math.Max(1, Environment.ProcessorCount - 2));

        /// <summary>
        ///     Modifies the total number of workers in the worker group.
        /// </summary>
        /// <param name="size">New desired size of the worker group.</param>
        public virtual void ModifyPoolSize(uint size)
        {
            if (size == WorkerCount || CancellationToken.IsCancellationRequested) return;

            ModifyWorkerGroupReset.Wait(CancellationToken);
            ModifyWorkerGroupReset.Reset();

            if (WorkerCount > size)
            {
                for (int index = WorkerCount - 1; index >= size; index--)
                {
                    IWorker worker = WorkerGroup[index];
                    worker.Cancel();
                    worker.ExceptionOccurred -= ExceptionOccurredCallback;

                    WorkerGroup.RemoveAt(index);
                }
            }
            else
            {
                for (int index = WorkerCount; index < size; index++)
                {
                    IWorker worker = CreateWorker();
                    worker.ExceptionOccurred += ExceptionOccurredCallback;
                    worker.Start();

                    WorkerGroup.Add(worker);
                }
            }

            ModifyWorkerGroupReset.Set();
        }

        /// <summary>
        ///     Safely cancels all workers.
        /// </summary>
        public void Stop() => CancellationTokenSource.Cancel();

        /// <summary>
        ///     Aggressively abort all workers.
        /// </summary>
        /// <remarks>
        ///     Only use this method in emergency situations. Undefined behaviour occurs when threads stop executing abruptly.
        /// </remarks>
        /// <param name="abort">Whether or not to abort all workers in the worker group.</param>
        public void Abort(bool abort)
        {
            if (!abort) return;

            ModifyWorkerGroupReset.Wait(CancellationToken);
            ModifyWorkerGroupReset.Reset();

            foreach (IWorker worker in WorkerGroup) worker?.Abort();

            WorkerGroup.Clear();

            _Active = null;

            // allow any errant waiters to fire so the pool can be cleaned up
            ModifyWorkerGroupReset.Set();
        }

        protected void ExceptionOccurredCallback(object? sender, Exception exception) => ExceptionOccurred?.Invoke(sender, exception);


        #region Active Pool State

        private static BoundedPool? _Active;

        /// <summary>
        ///     The currently active <see cref="BoundedPool" />.
        /// </summary>
        /// <exception cref="NullReferenceException">Thrown when there is no active <see cref="BoundedPool" />.</exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when there is already an active <see cref="BoundedPool" />, and a new one cannot be assigned.
        /// </exception>
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
