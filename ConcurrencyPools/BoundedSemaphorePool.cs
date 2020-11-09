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
            async Task WorkDispatcher(WorkInvocation work)
            {
                await _Semaphore.WaitAsync(CancellationTokenSource.Token);

                work.Invoke();

                _Semaphore.Release(1);
            }

            while (!CancellationTokenSource.IsCancellationRequested)
            {
                WorkInvocation work = await WorkReader.ReadAsync(CancellationTokenSource.Token);

                // dispatch work
                Task.Run(() => WorkDispatcher(work));
            }
        }

        public override void ModifyThreadPoolSize(uint size)
        {
            if (size == WorkerCount) return;

            ModifyWorkerGroupReset.Wait(CancellationTokenSource.Token);
            ModifyWorkerGroupReset.Reset();

            for (int i = 0; i < WorkerCount; i++) _Semaphore.Wait();

            _Semaphore.Release(WorkerCount);
            _Semaphore.Dispose();
            _Semaphore = new SemaphoreSlim((int)size);

            // semaphore pool doesn't use workers, but we need to keep count accurate
            WorkerGroup.AddRange(Enumerable.Repeat<IWorker>(null!, (int)size));

            ModifyWorkerGroupReset.Set();
        }

        protected override IWorker CreateWorker() => throw new NotImplementedException();

        public static void SetActive() => Active = new BoundedSemaphorePool();
    }
}
