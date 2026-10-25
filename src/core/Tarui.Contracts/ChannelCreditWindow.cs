namespace Tarui.Contracts;

/// <summary>
/// Bounded in-flight accounting for one streaming channel (<c>channel-credit-v1</c>).
/// <para>
/// A frame is "in flight" from the moment the native side hands it to the renderer until the renderer
/// acknowledges that its <c>onmessage</c> handler finished. The window caps how many frames may be in
/// flight at once, so a slow consumer slows the producer instead of letting an unbounded backlog grow
/// inside the browser process.
/// </para>
/// <para>
/// The window is deliberately dumb about transport: the owner decides when a frame is sent
/// (<see cref="AcquireAsync"/>) and when the renderer confirmed it (<see cref="TryRelease"/>). A
/// release only ever returns credit that was actually acquired, so a duplicate, forged or foreign
/// acknowledgement cannot mint capacity.
/// </para>
/// <para>
/// Accounting is guarded by a single lock instead of a semaphore: closing must wake every queued
/// producer with a stop signal, which a semaphore cannot express without over-releasing and racing
/// with the woken producers handing their slots back.
/// </para>
/// </summary>
public sealed class ChannelCreditWindow : IDisposable
{
    /// <summary>
    /// Frames a channel may keep in flight before the producer waits. Sixteen is small enough to bound
    /// renderer memory for large frames and large enough to keep a healthy consumer pipelined across a
    /// renderer round trip.
    /// </summary>
    public const int DefaultCapacity = 16;

    private readonly object _gate = new();
    private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
    private int _outstanding;
    private int _idleCredits;
    private int _disposeState;

    /// <summary>Creates a window with at most <paramref name="capacity"/> frames in flight.</summary>
    public ChannelCreditWindow(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _idleCredits = capacity;
    }

    /// <summary>Maximum number of frames that may be in flight at once.</summary>
    public int Capacity { get; }

    /// <summary>Frames currently awaiting an acknowledgement.</summary>
    public int Outstanding => Volatile.Read(ref _outstanding);

    /// <summary>Whether the channel was closed; a closed window never hands out credit again.</summary>
    public bool IsClosed => Volatile.Read(ref _disposeState) != 0;

    /// <summary>
    /// Takes one credit before a frame is sent. Returns <c>false</c> when the channel is closed, which the
    /// producer should treat as the end of the stream. Cancelling the token abandons the wait without
    /// consuming a credit.
    /// </summary>
    public async ValueTask<bool> AcquireAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool>? queued = null;
        lock (_gate)
        {
            if (IsClosed)
            {
                return false;
            }

            if (_idleCredits > 0)
            {
                _idleCredits--;
                _outstanding++;
                return true;
            }

            queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(queued);
        }

        try
        {
            // The credit was transferred to this waiter at grant time, so the continuation only
            // reports whether it was granted or the channel closed.
            return await queued.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (!queued.Task.IsCompleted)
                {
                    // Abandon the queue slot so Close does not signal a producer that already left.
                    RemoveWaiter(queued);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Returns one credit after the renderer confirmed the frame. Returns <c>false</c> when there was no
    /// frame in flight (duplicate acknowledgement, stale channel or a forged id), so the caller can ignore
    /// it without changing the accounting.
    /// </summary>
    public bool TryRelease()
    {
        lock (_gate)
        {
            if (IsClosed || _outstanding == 0)
            {
                return false;
            }

            _outstanding--;
            if (_waiters.Count > 0)
            {
                // Hand the credit straight to the next queued producer; outstanding stays accounted
                // because the frame it will send is in flight the moment the credit is granted.
                _outstanding++;
                var waiter = _waiters.Dequeue();
                waiter.TrySetResult(true);
            }
            else
            {
                _idleCredits++;
            }

            return true;
        }
    }

    /// <summary>
    /// Ends the channel: every queued producer wakes up and observes a stop signal, and further
    /// acquisitions fail immediately. Safe to call more than once.
    /// </summary>
    public void Close()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            while (_waiters.Count > 0)
            {
                _waiters.Dequeue().TrySetResult(false);
            }

            // A closed window never re-opens, so idle credit has no further meaning.
            _idleCredits = 0;
        }
    }

    /// <summary>Closes the window.</summary>
    public void Dispose() => Close();

    private void RemoveWaiter(TaskCompletionSource<bool> waiter)
    {
        // Caller holds the lock.
        var kept = new Queue<TaskCompletionSource<bool>>(_waiters.Count);
        while (_waiters.Count > 0)
        {
            var candidate = _waiters.Dequeue();
            if (!ReferenceEquals(candidate, waiter))
            {
                kept.Enqueue(candidate);
            }
        }

        while (kept.Count > 0)
        {
            _waiters.Enqueue(kept.Dequeue());
        }
    }
}
