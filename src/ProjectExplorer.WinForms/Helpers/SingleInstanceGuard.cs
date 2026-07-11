namespace ProjectExplorer.WinForms.Helpers;

/// <summary>
/// Named Mutex + EventWaitHandle pair implementing "Prevent multiple copies": the first
/// launch under that setting owns the mutex and listens for an activation signal; later
/// launches under that setting detect the mutex is already held, signal the running
/// instance, and exit without ever creating a window.
///
/// Uses session-local (unprefixed) names rather than "Global\" — this app only needs to
/// detect other copies for the same user's desktop session, and Global-namespace objects
/// can throw UnauthorizedAccessException in Terminal Services / mixed-elevation scenarios
/// this app has no reason to run into. Any failure to create the Mutex is treated as "act
/// like the first instance" — single-instance enforcement is a UX nicety, never a reason
/// to block startup.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "ProjectNestExplorer_SingleInstance";
    private const string SignalName = "ProjectNestExplorer_ActivateRequest";

    private readonly Mutex? _mutex;
    private EventWaitHandle? _activateSignal;
    private RegisteredWaitHandle? _registeredWait;

    public bool IsFirstInstance { get; }

    public SingleInstanceGuard()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            IsFirstInstance = createdNew;
            if (!IsFirstInstance)
            {
                _mutex.Dispose();
                _mutex = null;
            }
        }
        catch
        {
            IsFirstInstance = true;
        }
    }

    /// <summary>
    /// Signals the already-running instance to activate itself. Only meaningful when
    /// IsFirstInstance is false.
    /// </summary>
    public void SignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(SignalName);
            signal.Set();
        }
        catch
        {
            // Running instance hasn't started listening yet, or the signal handle
            // isn't there for some other reason — nothing more we can do.
        }
    }

    /// <summary>
    /// Starts listening for activation requests from later launches, invoking
    /// onActivateRequested (on a thread-pool thread — the caller must marshal onto the UI
    /// thread itself) each time one arrives. Only meaningful when IsFirstInstance is true.
    /// </summary>
    public void ListenForActivation(Action onActivateRequested)
    {
        if (!IsFirstInstance) return;

        try
        {
            _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _activateSignal,
                (state, timedOut) => onActivateRequested(),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch
        {
            // Non-critical — worst case, later launches just won't be able to refocus us.
        }
    }

    public void Dispose()
    {
        _registeredWait?.Unregister(null);
        _activateSignal?.Dispose();
        _mutex?.Dispose();
    }
}
