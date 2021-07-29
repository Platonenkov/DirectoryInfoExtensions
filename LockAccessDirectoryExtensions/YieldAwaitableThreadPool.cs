using System.Runtime.CompilerServices;


#nullable enable
namespace System.Threading.Tasks
{
    internal readonly ref struct YieldAwaitableThreadPool
    {
        private readonly bool _LockContext;

        public YieldAwaitableThreadPool(in bool LockContext) => this._LockContext = LockContext;

        public YieldAwaitableThreadPool.Awaiter GetAwaiter() => new YieldAwaitableThreadPool.Awaiter(in this._LockContext);

        public readonly struct Awaiter :
          ICriticalNotifyCompletion,
          INotifyCompletion,
          IEquatable<YieldAwaitableThreadPool.Awaiter>
        {
            private readonly bool _LockContext;
            private static readonly WaitCallback __WaitCallbackRunAction = new WaitCallback(YieldAwaitableThreadPool.Awaiter.RunAction);
            private static readonly SendOrPostCallback __SendOrPostCallbackRunAction = new SendOrPostCallback(YieldAwaitableThreadPool.Awaiter.RunAction);

            public Awaiter(in bool LockContext) => this._LockContext = LockContext;

            public bool IsCompleted => false;

            public void GetResult()
            {
            }

            private static void RunAction(object? State) => ((Action)State)();

            public void OnCompleted(Action Continuation) => YieldAwaitableThreadPool.Awaiter.QueueContinuation(Continuation, true, this._LockContext);

            public void UnsafeOnCompleted(Action Continuation) => YieldAwaitableThreadPool.Awaiter.QueueContinuation(Continuation, false, this._LockContext);

            private static void QueueContinuation(
              Action Continuation,
              bool FlowContext,
              bool LockContext)
            {
                if (Continuation == null)
                    throw new ArgumentNullException(nameof(Continuation));
                SynchronizationContext current1 = SynchronizationContext.Current;
                if (LockContext && current1 != null && current1.GetType() != typeof(SynchronizationContext))
                {
                    current1.Post(YieldAwaitableThreadPool.Awaiter.__SendOrPostCallbackRunAction, (object)Continuation);
                }
                else
                {
                    TaskScheduler current2 = TaskScheduler.Current;
                    if (!LockContext || current2 == TaskScheduler.Default)
                    {
                        if (FlowContext)
                            ThreadPool.QueueUserWorkItem(YieldAwaitableThreadPool.Awaiter.__WaitCallbackRunAction, (object)Continuation);
                        else
                            ThreadPool.UnsafeQueueUserWorkItem(YieldAwaitableThreadPool.Awaiter.__WaitCallbackRunAction, (object)Continuation);
                    }
                    else
                        Task.Factory.StartNew(Continuation, new CancellationToken(), TaskCreationOptions.PreferFairness, current2);
                }
            }

            public override bool Equals(object? obj) => obj is YieldAwaitableThreadPool.Awaiter awaiter && awaiter._LockContext == this._LockContext;

            public override int GetHashCode() => this._LockContext.GetHashCode();

            public static bool operator ==(
              YieldAwaitableThreadPool.Awaiter left,
              YieldAwaitableThreadPool.Awaiter right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(
              YieldAwaitableThreadPool.Awaiter left,
              YieldAwaitableThreadPool.Awaiter right)
            {
                return !(left == right);
            }

            public bool Equals(YieldAwaitableThreadPool.Awaiter other) => other._LockContext == this._LockContext;
        }
    }
}
