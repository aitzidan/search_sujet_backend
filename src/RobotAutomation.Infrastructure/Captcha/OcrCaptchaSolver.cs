using System.Drawing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RobotAutomation.Application.Captcha;
using RobotAutomation.Application.Configuration;
using RobotAutomation.Application.Robots.Abstractions;
using Tesseract;

namespace RobotAutomation.Infrastructure.Captcha;

/// <summary>
/// Free, local CAPTCHA OCR via the open-source Tesseract engine (charlesw/tesseract wrapper +
/// the "eng" trained model — no third-party solving service, no network call).
///
/// A distorted CAPTCHA rarely yields to one fixed recipe, so this tries several candidate
/// preprocessings (raw, a few binarization thresholds, inverted for light-on-dark images) across
/// both engine modes and keeps the highest-confidence non-empty reading. The legacy engine usually
/// wins on CAPTCHAs — LSTM is trained on real words, whereas these are random character strings.
///
/// Every attempt dumps what it actually fed the OCR under the screenshot directory, so a bad read
/// can be diagnosed by looking at the image instead of guessing.
///
/// A <see cref="TesseractEngine"/> is created per attempt rather than cached: it is not safe to use
/// concurrently, and runs execute in parallel.
/// </summary>
internal sealed class OcrCaptchaSolver : ICaptchaSolver
{
    private static readonly string TessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
    private const string Whitelist = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Mean confidence at or above which a reading is accepted without trying further
    /// variants. Measured at ~0.9 for clean readings and well under 0.4 for garbled ones.</summary>
    private const float AcceptConfidence = 0.75f;

    /// <summary>Below this length a reading is treated as a partial read worth retrying, even if the
    /// engine reported high confidence on the fragment it did find.</summary>
    private const int MinPlausibleLength = 4;

    private readonly PlaywrightOptions _options;

    public OcrCaptchaSolver(IOptions<PlaywrightOptions> options) => _options = options.Value;

    public async Task<string> SolveAsync(RobotContext ctx, CancellationToken ct)
    {
        var challenge = ctx.Portal.Selectors.CaptchaChallenge;
        if (string.IsNullOrWhiteSpace(challenge))
            return string.Empty; // portal has no CAPTCHA configured

        await ctx.Page.WaitForSelectorAsync(challenge, ctx.DefaultTimeoutMs, ct);
        var (source, origin) = await CaptureAsync(ctx, challenge, ct);
        var diagnosticsDir = DiagnosticsDir(ctx);
        Save(diagnosticsDir, "captcha-source.png", source);
        ctx.Logger.LogInformation("CAPTCHA image captured from {Origin} ({Bytes} bytes); diagnostics in {Dir}",
            origin, source.Length, diagnosticsDir);

        var best = Recognize(source, diagnosticsDir, ctx.Logger);
        if (best is null)
        {
            throw new InvalidOperationException(
                "L'OCR n'a rien reconnu dans l'image du CAPTCHA. " +
                $"Images inspectées dans {diagnosticsDir} — si l'image y est vide ou illisible, le problème est la capture ; " +
                "sinon le CAPTCHA est trop déformé pour l'OCR (basculez DgiPortals:real:CaptchaMode sur \"Manual\").");
        }

        ctx.Logger.LogInformation(
            "OCR read CAPTCHA as '{Text}' (confiance {Confidence:P0}, via {Variant}/{Mode}) — vérifiez {Dir} si la connexion échoue",
            best.Text, best.Confidence, best.Variant, best.Mode, diagnosticsDir);
        return best.Text;
    }

    /// <summary>
    /// Prefers decoding the image straight out of a <c>data:</c> URI — those are the original,
    /// unscaled pixels, whereas an element screenshot captures whatever CSS rendered it to (the DGI
    /// login shrinks its CAPTCHA with <c>max-width:50%</c>, losing detail OCR needs). Falls back to
    /// an element screenshot for normally-served images.
    /// </summary>
    private static async Task<(byte[] Bytes, string Origin)> CaptureAsync(RobotContext ctx, string selector, CancellationToken ct)
    {
        var src = await ctx.Page.GetAttributeAsync(selector, "src", ct);
        if (src is not null)
        {
            var comma = src.IndexOf(',');
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
            {
                try
                {
                    return (Convert.FromBase64String(src[(comma + 1)..]), "data-URI (pixels d'origine)");
                }
                catch (FormatException)
                {
                    // Not base64 (e.g. a data:...;utf8 SVG) — fall through to the screenshot path.
                }
            }
        }

        return (await ctx.Page.ScreenshotElementAsync(selector, ct), "capture d'écran de l'élément");
    }

    /// <summary>
    /// Walks candidate (image variant × engine mode × segmentation) combinations best-guess-first and
    /// stops as soon as one reads confidently, falling back to progressively more aggressive
    /// preprocessing only when needed. Ordering matters for speed: each engine loads a 23 MB model, and
    /// the untouched image read by the legacy engine wins the overwhelming majority of the time.
    /// </summary>
    private static Reading? Recognize(byte[] source, string diagnosticsDir, ILogger logger)
    {
        Reading? best = null;

        foreach (var (variant, bytes) in Variants(source))
        {
            Save(diagnosticsDir, $"captcha-{variant}.png", bytes);

            // Legacy first: it reads random character strings better than the LSTM engine, which is
            // trained on real words.
            foreach (var mode in new[] { EngineMode.TesseractOnly, EngineMode.Default })
            {
                TesseractEngine engine;
                try
                {
                    engine = new TesseractEngine(TessDataPath, "eng", mode);
                    engine.SetVariable("tessedit_char_whitelist", Whitelist);
                    engine.SetVariable("tessedit_pageseg_mode", "7");
                    //engine.SetVariable("tessedit_ocr_engine_mode", "1");
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Could not create Tesseract engine ({Mode})", mode);
                    continue;
                }

                using (engine)
                {
                    foreach (var seg in new[] { PageSegMode.SingleLine, PageSegMode.SingleWord, PageSegMode.SparseText })
                    {
                        var reading = TryRead(engine, bytes, seg, variant, mode, logger);
                        if (reading is not null && (best is null || reading.Confidence > best.Confidence))
                            best = reading;

                        if (best is not null && best.Confidence >= AcceptConfidence && best.Text.Length >= MinPlausibleLength)
                            return best;
                    }
                }
            }
        }

        return best;
    }

    private static Reading? TryRead(
        TesseractEngine engine, byte[] png, PageSegMode seg, string variant, EngineMode mode, ILogger logger)
    {
        try
        {
            using var pix = Pix.LoadFromMemory(png);
            using var page = engine.Process(pix, seg);
            var cleaned = new string((page.GetText() ?? "").Where(char.IsLetterOrDigit).ToArray());
            return cleaned.Length == 0 ? null : new Reading(cleaned, page.GetMeanConfidence(), variant, mode);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "OCR attempt failed ({Variant}/{Mode}/{Seg})", variant, mode, seg);
            return null;
        }
    }

    /// <summary>Candidate renderings of the same image: untouched, upscaled+binarized at a few
    /// thresholds, and inverted (for light text on a dark background, which Tesseract reads poorly).</summary>
    private static IEnumerable<(string Variant, byte[] Bytes)> Variants(byte[] source)
    {
        yield return ("raw", source);
        foreach (var threshold in new[] { 100, 140, 180 })
        {
            yield return ($"bw{threshold}", Binarize(source, threshold, invert: false));
            yield return ($"bw{threshold}-inv", Binarize(source, threshold, invert: true));
        }
    }

    /// <summary>Flatten onto white (a transparent CAPTCHA PNG would otherwise read as all-black),
    /// upscale, then threshold to pure black/white.</summary>
    private static byte[] Binarize(byte[] pngBytes, int threshold, bool invert)
    {
        using var source = new Bitmap(new MemoryStream(pngBytes));
        const int scale = 4;
        using var scaled = new Bitmap(source.Width * scale, source.Height * scale);
        using (var g = Graphics.FromImage(scaled))
        {
            g.Clear(Color.White);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
        }

        for (var y = 0; y < scaled.Height; y++)
        {
            for (var x = 0; x < scaled.Width; x++)
            {
                var p = scaled.GetPixel(x, y);
                var gray = (p.R * 0.299) + (p.G * 0.587) + (p.B * 0.114);
                var dark = gray <= threshold;
                if (invert) dark = !dark;
                scaled.SetPixel(x, y, dark ? Color.Black : Color.White);
            }
        }

        using var output = new MemoryStream();
        scaled.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>One folder per attempt — a retry loads a different CAPTCHA image, and writing them all to
    /// the same place would leave only the last one to inspect.</summary>
    private string DiagnosticsDir(RobotContext ctx)
    {
        var attempt = ctx.Items.TryGetValue("captchaAttempt", out var value) && value is int i ? i : 1;
        return Path.Combine(
            _options.ResolveArtifactDirectory(),
            ctx.RunId.ToString("N"),
            "captcha",
            $"attempt-{attempt:D2}");
    }

    private static void Save(string dir, string name, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
        }
        catch
        {
            // Diagnostics are best-effort — never fail a run because we could not write them.
        }
    }

    private sealed record Reading(string Text, float Confidence, string Variant, EngineMode Mode);
}
