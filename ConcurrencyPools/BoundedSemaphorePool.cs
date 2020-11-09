using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConcurrencyPools
{
    public class BoundedSemaphorePool : BoundedPool
    {
        private SemaphoreSlim _Semaphore;

        public override int WorkerCount => _Semaphore.CurrentCount;

        public BoundedSemaphorePool() : base(true, false) => _Semaphore = new SemaphoreSlim(0);

        public override void QueueWork(WorkInvocation workInvocation)
        {
            // ensure the worker group isn't being modified
            ModifyWorkerGroupReset.Wait(CancellationToken);

            if (WorkerCount == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(BoundedAsyncPool)} has no active workers. Call {nameof(DefaultPoolSize)}() or {nameof(ModifyPoolSize)}().");
            }
            else
            {
                async Task WorkDispatch()
                {
                    try
                    {
                        // wait to enter semaphore
                        await _Semaphore.WaitAsync(CancellationToken).ConfigureAwait(false);

                        // if we're not cancelled, execute work
                        if (!CancellationToken.IsCancellationRequested) workInvocation();

                        // check cancellation again, in case we cancelled while work finished
                        // if not, release semaphore slot
                        if (!CancellationToken.IsCancellationRequested) _Semaphore.Release(1);
                    }
                    catch (Exception exception)
                    {
                        ExceptionOccurredCallback(this, exception);
                    }
                }

                Task.Run(WorkDispatch, CancellationToken);
            }
        }

        public override void QueueWork(Work work) => QueueWork(work.Execute);

        public override void ModifyPoolSize(uint size)
        {
            if ((size == WorkerCount) || CancellationToken.IsCancellationRequested) return;

            ModifyWorkerGroupReset.Wait(CancellationToken);
            ModifyWorkerGroupReset.Reset();

            if (WorkerCount > 0)
            {
                for (int i = 0; i < _Semaphore.CurrentCount; i++) _Semaphore.Wait();
                _Semaphore.Release(WorkerCount);
            }

            _Semaphore.Dispose();
            _Semaphore = new SemaphoreSlim((int)size);

            ModifyWorkerGroupReset.Set();
        }

        protected override IWorker CreateWorker() => throw new NotImplementedException();

        public static void SetActivePool() => Active = new BoundedSemaphorePool();

        ~BoundedSemaphorePool() => _Semaphore.Dispose();
    }
}
