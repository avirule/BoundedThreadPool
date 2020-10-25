using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ConcurrencyPools
{
    public class BoundedAsyncPool : BoundedPool
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
                while (!_CompoundToken.IsCancellationRequested)
                {
                    await _WorkChannel.WaitToReadAsync(_CompoundToken).ConfigureAwait(false);
                    (await _WorkChannel.ReadAsync(_CompoundToken).ConfigureAwait(false)).Invoke();
                }
            }

            public void Start() => Task.Run(Runtime, _CompoundToken);
            public void Cancel() => _InternalCancellation.Cancel();
        }

        private BoundedAsyncPool() { }
        public static void Create() => Active = new BoundedAsyncPool();

        protected override IWorker CreateWorker() => new Worker(CancellationTokenSource.Token, WorkReader);
    }
}
