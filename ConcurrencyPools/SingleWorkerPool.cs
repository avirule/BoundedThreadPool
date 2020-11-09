using System;
using System.Threading.Tasks;

namespace ConcurrencyPools
{
    public class SingleWorkerPool : BoundedPool
    {
        public override int WorkerCount => 1;

        public SingleWorkerPool() : base(true, false) => Task.Run(Worker, CancellationToken);

        private async Task Worker()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                try
                {
                    (await WorkReader.ReadAsync(CancellationToken).ConfigureAwait(false)).Invoke();
                }
                catch (Exception exception)
                {
                    ExceptionOccurredCallback(this, exception);
                }
            }
        }

        public override void ModifyPoolSize(uint size) => throw new InvalidOperationException("Cannot modify pool size.");

        protected override IWorker CreateWorker() => throw new NotImplementedException();

        public static void SetActivePool() => Active = new SingleWorkerPool();
    }
}
