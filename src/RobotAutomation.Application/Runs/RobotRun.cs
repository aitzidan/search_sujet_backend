using RobotAutomation.Domain.Enums;
using RobotAutomation.Domain.ValueObjects;

namespace RobotAutomation.Application.Runs;

public sealed class RobotRun
{
    private readonly Lock _gate = new();
    private readonly List<StepLogEntry> _steps = new();
    private readonly List<string> _screenshots = new();
    private readonly Dictionary<string, string?> _data = new();

    public required Guid RunId { get; init; }
    public required string RobotKey { get; init; }
    public required string PortalName { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public RobotStatus Status { get; private set; } = RobotStatus.Pending;
    public string? CurrentStepName { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? ErrorMessage { get; private set; }

    public IReadOnlyList<StepLogEntry> Steps { get { lock (_gate) return _steps.ToList(); } }
    public IReadOnlyList<string> Screenshots { get { lock (_gate) return _screenshots.ToList(); } }
    public IReadOnlyDictionary<string, string?> Data { get { lock (_gate) return new Dictionary<string, string?>(_data); } }

    public void MarkRunning()
    {
        lock (_gate)
        {
            Status = RobotStatus.Running;
            StartedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public int BeginStep(string name)
    {
        lock (_gate)
        {
            var order = _steps.Count;
            CurrentStepName = name;
            _steps.Add(new StepLogEntry(order, name, StepStatus.Running,
                DateTimeOffset.UtcNow, null, null, null, null));
            return order;
        }
    }

    public void CompleteStep(int order, StepStatus status, string? message, string? screenshotPath, string? currentUrl)
    {
        lock (_gate)
        {
            var started = _steps[order].StartedAtUtc;
            _steps[order] = new StepLogEntry(order, _steps[order].StepName, status,
                started, DateTimeOffset.UtcNow, message, screenshotPath, currentUrl);
            if (screenshotPath is not null) _screenshots.Add(screenshotPath);
        }
    }

    public void AddScreenshot(string path)
    {
        lock (_gate) _screenshots.Add(path);
    }

    public void SetData(IEnumerable<KeyValuePair<string, string?>> data)
    {
        lock (_gate)
        {
            foreach (var (key, value) in data) _data[key] = value;
        }
    }

    public void Finish(RobotStatus status, string? errorMessage = null)
    {
        lock (_gate)
        {
            Status = status;
            ErrorMessage = errorMessage;
            CurrentStepName = null;
            CompletedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public RobotRun Snapshot()
    {
        lock (_gate)
        {
            var copy = new RobotRun
            {
                RunId = RunId,
                RobotKey = RobotKey,
                PortalName = PortalName,
                CreatedAtUtc = CreatedAtUtc,
                Status = Status,
                CurrentStepName = CurrentStepName,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                ErrorMessage = ErrorMessage
            };
            copy._steps.AddRange(_steps);
            copy._screenshots.AddRange(_screenshots);
            foreach (var (key, value) in _data) copy._data[key] = value;
            return copy;
        }
    }
}
