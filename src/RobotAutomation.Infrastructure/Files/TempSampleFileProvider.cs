using RobotAutomation.Application.Files;

namespace RobotAutomation.Infrastructure.Files;

/// <summary>
/// Writes a tiny sample EDI XML file under the temp folder once and reuses it. Enough to exercise
/// the file-upload step; the real robot would point at the generated télédéclaration zip.
/// </summary>
internal sealed class TempSampleFileProvider : ISampleFileProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _path;

    public async Task<string> GetSampleEdiFileAsync(CancellationToken ct)
    {
        if (_path is not null && File.Exists(_path)) return _path;

        await _lock.WaitAsync(ct);
        try
        {
            if (_path is not null && File.Exists(_path)) return _path;

            var dir = Path.Combine(Path.GetTempPath(), "robot-automation", "samples");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "declaration-tva-sample.xml");
            const string content =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<DeclarationTVA sample=\"true\" />\n";
            await File.WriteAllTextAsync(path, content, ct);
            _path = path;
            return path;
        }
        finally
        {
            _lock.Release();
        }
    }
}
