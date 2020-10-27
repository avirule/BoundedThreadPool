# Concurrency Pools
Offsers a set of useful pools to queue work into for concurrent execution.

## When should I use it?
The aim of these concurrency pools is to provide a way to execute a particular set of work in a bounded context. That is to say, you can manually specify how many workers are executing code at any given time. This means if you have, say, a 4-core 8-thread system, you could avoid clogging up every core with continuous work.

Otherwise, it can be useful when you want built-in semaphore-esque access to a resource, with concurrency already offered. 

## How do I use it?
A simple example of usage is shown here (also found in `Example/Program.cs`):

```csharp
void Startup()
{
    // we're going to use the bounded thread pool
    // this means we're using dedicated threads
    BoundedThreadPool.SetActivePool();
    // set pool size to default (`Environment.ProcessorCount - 2` or 1, whichever is greater)
    BoundedPool.Active.DefaultThreadPoolSize();

    // queue up work
    const int work_size = 500;
    WorkInvocation[] work = new WorkInvocation[work_size];
    for (int index = 0; index < work.Size; index++) 
        // this lambda is automatically cast to a `WorkInvocation`
        work[index] = () => Console.WriteLine(index);

    // queue the work invocations to be execute
    foreach (WorkInvocation workInvocation in work) 
        BoundedPool.Active.QueueWork(workInvocation);
}
```

### Exceptions
Exceptions can be handled with the `BoundedPool.ExceptionOccurred` event, which is of type `EventHandler<Exception>`. Workers should continue executing further work items if an exception occurs, so handling them properly is mostly the programmer's responsibility.

## Closing note
The overall API of this project is pretty bare, so functionality ought to be straightforward. Instantiate a pool, modify its size, then queue work onto it.

Of course, if you so choose, you can always roll your own pool as well. The `BoundedPool` abstract class provides most of the functionality, with you only needed to implement `IWorker CreateWorker()`, effectively giving you the option to handle specifically how your work is executed.

