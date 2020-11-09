#region

using System;
using System.Diagnostics;
using System.Threading;
using ConcurrencyPools;

#endregion


namespace Example
{
    internal class Program
    {
        private class TestWork : BoundedPool.Work
        {
            private static int _TestInt;

            public override void Execute()
            {
                Interlocked.Increment(ref _TestInt);
                Console.WriteLine($"Test {_TestInt}");
            }
        }

        private static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            BoundedThreadPool.SetActivePool();
            BoundedPool.Active.DefaultPoolSize();

            TestWork[] work = new TestWork[50];
            for (int index = 0; index < work.Length; index++) work[index] = new TestWork();

            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (TestWork testWork in work) BoundedPool.Active.QueueWork(testWork);

            Console.WriteLine(stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
