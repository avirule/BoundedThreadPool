using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConcurrencyPools
{
    public class BoundedSemaphorePool : BoundedPool
    {
        private SemaphoreSlim _Semaphore;

        public BoundedSemaphorePool() : base(true, false)
        {
            _Semaphore = new SemaphoreSlim(0);

            Task.Run(WorkListener, CancellationToken);
        }

        private async Task WorkListener()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                try
                {
                    // wait for work
                    WorkInvocation work = await WorkReader.ReadAsync(CancellationToken).ConfigureAwait(false);

                    async Task WorkDispatch()
                    {
                        try
                        {
                            // wait to enter semaphore
                            await _Semaphore.WaitAsync(CancellationToken).ConfigureAwait(false);

                            // if we're not cancelled, execute work
                            if (!CancellationToken.IsCancellationRequested) work.Invoke();

                            // check cancellation again, in case we cancelled while work finished
                            // if not, release semaphore slot
                            if (!CancellationToken.IsCancellationRequested) _Semaphore.Release(1);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException && !CancellationToken.IsCancellationRequested)
                        {
                            ExceptionOccurredCallback(this, exception);
                        }
                    }

                    // if we're not cancelled, dispatch work
                    if (!CancellationToken.IsCancellationRequested) Task.Run(WorkDispatch, CancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException && !CancellationToken.IsCancellationRequested)
                {
                    ExceptionOccurredCallback(this, exception);
                }
            }
        }

        public override void ModifyPoolSize(uint size)
        {
            if ((size == WorkerCount) || CancellationToken.IsCancellationRequested) return;

            ModifyWorkerGroupReset.Wait(CancellationToken);
            ModifyWorkerGroupReset.Reset();

            if (WorkerCount > 0)
            {
                for (int i = 0; i < WorkerCount; i++) _Semaphore.Wait();
                _Semaphore.Release(WorkerCount);
            }

            _Semaphore.Dispose();
            _Semaphore = new SemaphoreSlim((int)size);

            // semaphore pool doesn't use workers, but we need to keep count accurate
            WorkerGroup.AddRange(Enumerable.Repeat<IWorker>(null!, (int)size));

            ModifyWorkerGroupReset.Set();
        }

        protected override IWorker CreateWorker() => throw new NotImplementedException();

        public static void SetActivePool() => Active = new BoundedSemaphorePool();

        ~BoundedSemaphorePool() => _Semaphore.Dispose();
    }
}
