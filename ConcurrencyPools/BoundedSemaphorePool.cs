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

            Task.Run(WorkListener);
        }

        private async Task WorkListener()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                try
                {
                    WorkInvocation work = await WorkReader.ReadAsync(CancellationToken);

                    async Task WorkDispatch()
                    {
                        try
                        {
                            await _Semaphore.WaitAsync(CancellationToken);

                            work.Invoke();

                            _Semaphore.Release(1);
                        }
                        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested) { }
                        catch (Exception exception)
                        {
                            ExceptionOccurredCallback(this, exception);
                        }
                    }

                    // dispatch work
                    Task.Run(WorkDispatch, CancellationToken);
                }
                catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    ExceptionOccurredCallback(this, exception);
                }
            }
        }

        public override void ModifyThreadPoolSize(uint size)
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
