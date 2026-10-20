namespace MinecraftServerManager.Services;

public enum ToastLevel { Info, Success, Warning, Error }

public sealed record Toast(Guid Id, string Message, ToastLevel Level);

/// <summary>Per-circuit (scoped) notification queue displayed by the Toasts component.</summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];

    public event Action? Changed;

    public IReadOnlyList<Toast> Toasts
    {
        get { lock (_toasts) return _toasts.ToList(); }
    }

    public void Info(string message) => Show(message, ToastLevel.Info);
    public void Success(string message) => Show(message, ToastLevel.Success);
    public void Warning(string message) => Show(message, ToastLevel.Warning);
    public void Error(string message) => Show(message, ToastLevel.Error);

    public void Show(string message, ToastLevel level)
    {
        var toast = new Toast(Guid.NewGuid(), message, level);
        lock (_toasts) _toasts.Add(toast);
        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id, level == ToastLevel.Error ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(5));
    }

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_toasts) removed = _toasts.RemoveAll(t => t.Id == id) > 0;
        if (removed) Changed?.Invoke();
    }

    private async Task DismissLaterAsync(Guid id, TimeSpan delay)
    {
        await Task.Delay(delay);
        Dismiss(id);
    }
}
