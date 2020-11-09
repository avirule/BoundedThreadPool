using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ConcurrencyPools
{
    public sealed class BoundedAsyncPool : BoundedPool
    {
        private class Worker : IWorker
        {
            private readonly CancellationToken _CompoundToken;
            private readonly CancellationTokenSource _InternalCancellation;
            private readonly ChannelReader<WorkInvocation> _WorkChannel;

            public Worker(CancellationToken cancellationToken, ChannelReader<WorkInvocation> workChannel)
            {
                _InternalCancellation = new CancellationTokenSource();
                _CompoundToken = CancellationTokenSource.CreateLinkedTokenSource(_InternalCancellation.Token, cancellationToken).Token;
                _WorkChannel = workChannel;
            }

            private async Task Runtime()
            {
                async Task ChannelCompletionCancellation()
                {
                    await _WorkChannel.Completion.ConfigureAwait(false);

                    _InternalCancellation.Cancel();
                }

                Task.Run(ChannelCompletionCancellation, _CompoundToken);

                while (!_CompoundToken.IsCancellationRequested)
                {
                    try
                    {
                        (await _WorkChannel.ReadAsync(_CompoundToken).ConfigureAwait(false)).Invoke();
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
            public void Start() => Task.Run(Runtime, _CompoundToken);

            /// <inheritdoc />
            public void Cancel() => _InternalCancellation.Cancel();

            public void Abort() => _InternalCancellation.Cancel();
        }

        public BoundedAsyncPool() : base(false, false) { }

        /// <summary>
        ///     Set <see cref="BoundedThreadPool" /> as the active pool.
        /// </summary>
        public static void SetActivePool() => Active = new BoundedAsyncPool();

        protected override IWorker CreateWorker() => new Worker(CancellationToken, WorkReader);
    }
}
