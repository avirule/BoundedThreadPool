using System.Threading;
using System.Threading.Channels;

namespace ConcurrencyPools
{
    public class BoundedThreadPool : BoundedPool
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
                while (!_CompoundToken.IsCancellationRequested)
                {
                    if (_WorkChannel.TryRead(out WorkInvocation item)) item();
                    else Thread.Sleep(1);
                }
            }

            public void Abort() => _InternalThread.Abort();

            public void Start() => _InternalThread.Start();
            public void Cancel() => _InternalCancellation.Cancel();
        }

        protected BoundedThreadPool() { }

        protected override IWorker CreateWorker() => new Worker(CancellationTokenSource.Token, WorkReader);
    }
}
