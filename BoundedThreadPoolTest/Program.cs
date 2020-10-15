#region

using System;
using System.Diagnostics;
using System.Threading;
using ConcurrentPools;

#endregion

namespace BoundedThreadPoolTest
{
    internal class Program
    {
        private class TestWork : BoundedThreadPool.Work
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

            BoundedThreadPool.DefaultThreadPoolSize();

            TestWork[] work = new TestWork[50];
            for (int index = 0; index < work.Length; index++)
            {
                work[index] = new TestWork();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (TestWork testWork in work)
            {
                BoundedThreadPool.QueueWork(testWork);
            }

            Console.WriteLine(stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
