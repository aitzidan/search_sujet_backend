namespace RobotAutomation.Application.Robots;

public interface IRobotPage
{
    string Url { get; }

    Task GotoAsync(string url, string waitUntil, CancellationToken ct);

    Task FillAsync(string selector, string value, CancellationToken ct);

    Task ClickAsync(string selector, CancellationToken ct);

    Task SelectOptionAsync(string selector, string value, CancellationToken ct);

    Task SelectOptionByLabelAsync(string selector, string label, CancellationToken ct);

    Task<bool> IsDisabledAsync(string selector, CancellationToken ct);

    Task<bool> IsEditableAsync(string selector, CancellationToken ct);

    Task<string?> GetValueAsync(string selector, CancellationToken ct);

    Task<int> CountAsync(string selector, CancellationToken ct);

    Task<string?> GetTextAsync(string selector, CancellationToken ct);

    Task<bool> IsVisibleAsync(string selector, CancellationToken ct);

    Task<bool> IsInViewportAsync(string selector, CancellationToken ct);

    Task ScrollToTopAsync(CancellationToken ct);

    Task WaitForSelectorAsync(string selector, int timeoutMs, CancellationToken ct);

    Task WaitForHiddenAsync(string selector, int timeoutMs, CancellationToken ct);

    Task WaitForUrlAsync(string urlPattern, int timeoutMs, CancellationToken ct);

    Task SetInputFilesAsync(string selector, string filePath, CancellationToken ct);

    Task<string> ScreenshotAsync(string filePath, CancellationToken ct);
}
