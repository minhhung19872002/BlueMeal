namespace BlueMeal.Api.Auth;

/// <summary>
/// Caps how many PBKDF2 password checks (210,000 iterations, ~0.1–0.2 s of CPU each) run at the same time in one API
/// instance. During a login burst (shift start) the extra logins wait in a short queue instead of taking every core,
/// so the screens of users who are already signed in stay fast. Setting: Auth:MaxConcurrentPasswordHashes
/// (default: a quarter of the CPU cores, at least 2).
/// </summary>
public sealed class PasswordHashGate(IConfiguration config)
{
    private readonly SemaphoreSlim gate = new(Limit(config));

    public static int Limit(IConfiguration config)
        => config.GetValue<int?>("Auth:MaxConcurrentPasswordHashes") is { } n && n > 0 ? n : Math.Max(2, Environment.ProcessorCount / 4);

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await work(); }
        finally { gate.Release(); }
    }
}
