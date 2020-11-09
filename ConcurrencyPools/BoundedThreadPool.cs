using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ConcurrencyPools
{
    /// <summary>
    ///     Used for executing work on a dedicated pool of threads.
    /// </summary>
    /// <remarks>
    ///     <p>
    ///         The workers for this pool use a check-read-sleep loop. This means that it's possible for
    ///         there to be not-insignificant delay (whatever the NtTime slice is set to) between when work
    ///         is queued and when that work is executed.
    ///     </p>
    ///     <p>
    ///         With this in mind, unless you absolutely require a dedicated pool of threads (such as in the case
    ///         that the .NET ThreadPool isn't available), it would be best to use the <see cref="BoundedAsyncPool" />
    ///         class.
    ///     </p>
    /// </remarks>
    public sealed class BoundedThreadPool : BoundedPool
    {
        private class Worker : IWorker
        {
            private readonly CancellationToken _CompoundToken;
            private readonly CancellationTokenSource _InternalCancellation;
            private readonly Thread _InternalThread;
            private readonly ChannelReader<WorkInvocation> _WorkChannel;

            public Worker(CancellationToken cancellationToken, ChannelReader<WorkInvocation> workChannel)
            {
                _InternalCancellation = new CancellationTokenSource();
                _CompoundToken = CancellationTokenSource.CreateLinkedTokenSource(_InternalCancellation.Token, cancellationToken).Token;
                _WorkChannel = workChannel;
                _InternalThread = new Thread(Runtime);
            }

            private void Runtime()
            {
                async Task ChannelCompletionCancellation()
                {
                    await _WorkChannel.Completion;

                    _InternalCancellation.Cancel();
                }

                Task.Run(ChannelCompletionCancellation, _CompoundToken);

                while (!_CompoundToken.IsCancellationRequested)
                {
                    try
                    {
                        if (_WorkChannel.TryRead(out WorkInvocation item)) item();
                        else Thread.Sleep(1);
                    }
                    catch (Exception exception)
                    {
                        ExceptionOccurred?.Invoke(this, exception);
                    }
                }
            }

            /// <inheritdoc />
            public event EventHandler<Exception>? ExceptionOccurred;

            /// <inheritdoc />
            public void Start() => _InternalThread.Start();

            /// <inheritdoc />
            public void Cancel() => _InternalCancellation.Cancel();

            /// <inheritdoc />
            public void Abort() => _InternalThread.Abort();
        }

        public BoundedThreadPool() : base(false, false) {}

        /// <summary>
        ///     Set <see cref="BoundedThreadPool" /> as the active pool.
        /// </summary>
        public static void SetActivePool() => Active = new BoundedThreadPool();

        protected override IWorker CreateWorker() => new Worker(CancellationTokenSource.Token, WorkReader);
    }
}
