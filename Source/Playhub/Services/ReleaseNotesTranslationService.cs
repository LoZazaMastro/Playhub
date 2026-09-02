using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

// No default network provider: Google's supported API requires authenticated,
// billing-enabled setup. A provider receives only plain-text runs, in order.
public sealed class ReleaseNotesTranslationService
{
    public delegate Task<IReadOnlyList<string>> TranslateSegmentsAsync(
        IReadOnlyList<string> segments, string targetLanguage, CancellationToken cancellationToken);

    public sealed record Translation(string Markdown, bool IsAutomatic);

    private const int MaxSourceLength = 20_000;
    private const int CacheCapacity = 32;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex LanguagePattern = new(@"^[a-z]{2,3}(?:-[a-z0-9]{2,8})*$",
        RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex UnsupportedBlocks = new(@"(?m)^(?:[ \t]*(?:`{3,}|~{3,})| {4}|\t)",
        RegexOptions.CultureInvariant, RegexTimeout);

    // Protect syntax locally rather than asking a translator to round-trip it.
    // The caller supplies the shared description reader's normalized Markdown.
    private static readonly Regex ProtectedSyntax = new(
        @"(?<ticks>`+)[^\r\n]*?\k<ticks>|" +
        @"\]\((?:[^()\\\r\n]|\\.|(?<depth>\()|(?<-depth>\)))*(?(depth)(?!))\)|" +
        @"\]\[[^\]\r\n]*\]|(?m:^[ \t]*\[[^\]\r\n]+\]:[^\r\n]*)|" +
        @"<[^>\r\n]+>|https?://[^\s<>]+|\\.|" +
        @"(?m:^[ \t]*(?:>[ \t]*)*(?:#{1,6}|[-+*]|\d+[.)])[ \t]+)|" +
        @"(?m:^[ \t]*(?:>[ \t]*)+)|[\r\n`*_\[\]#<>~|()!\\]",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex UnsafeTranslation = new(
        @"[\p{C}\r\n`*_\[\]#<>~|\\]|https?://|&(?:#\w+|[a-z]+);|^(?:[-+]|\d+[.)])\s",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);

    private readonly TranslateSegmentsAsync? _translate;
    private readonly TimeSpan _timeout;
    private readonly object _cacheLock = new();
    private readonly Dictionary<(string Markdown, string Language), Translation> _cache = new();
    private readonly Queue<(string Markdown, string Language)> _cacheOrder = new();

    public ReleaseNotesTranslationService(TranslateSegmentsAsync? translate = null, TimeSpan? timeout = null)
    {
        _translate = translate;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public bool IsConfigured => _translate is not null;

    public async Task<Translation> TranslateAsync(string markdown, string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var original = new Translation(markdown, false);
        if (_translate is null || cancellationToken.IsCancellationRequested ||
            string.IsNullOrWhiteSpace(markdown) || markdown.Length > MaxSourceLength ||
            string.IsNullOrWhiteSpace(targetLanguage)) return original;

        var language = targetLanguage.Trim().ToLowerInvariant();
        if (language.Length > 35 || !LanguagePattern.IsMatch(language)) return original;
        var key = (markdown, language);
        lock (_cacheLock)
            if (_cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            // Unsupported code blocks stay original. BuildDescription normally
            // removes these before this service is called.
            if (UnsupportedBlocks.IsMatch(markdown)) return original;
            var runs = FindTextRuns(markdown);
            if (runs.Count == 0) return original;
            var segments = Array.AsReadOnly(runs.Select(run => markdown.Substring(run.Start, run.Length)).ToArray());
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_timeout);
            var workToken = deadline.Token;
            // Even a provider with a synchronous preamble cannot block the UI.
            // WaitAsync also bounds providers that fail to observe cancellation.
            var translated = await Task.Run(() => _translate(segments, language, workToken), workToken)
                .WaitAsync(workToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            deadline.Token.ThrowIfCancellationRequested();
            if (translated is null || translated.Count != runs.Count) return original;

            var output = new StringBuilder(markdown.Length);
            var cursor = 0;
            for (var index = 0; index < runs.Count; index++)
            {
                var value = translated[index]?.Trim();
                if (string.IsNullOrWhiteSpace(value) || value.Length > MaxSourceLength || UnsafeTranslation.IsMatch(value))
                    return original;
                var run = runs[index];
                output.Append(markdown, cursor, run.Start - cursor);
                output.Append(value);
                cursor = run.Start + run.Length;
                if (output.Length > MaxSourceLength * 4) return original;
            }
            output.Append(markdown, cursor, markdown.Length - cursor);
            cancellationToken.ThrowIfCancellationRequested();
            deadline.Token.ThrowIfCancellationRequested();
            var translatedMarkdown = output.ToString();
            if (translatedMarkdown.Length > MaxSourceLength * 4 ||
                !ProtectedSyntax.Matches(markdown).Select(match => match.Value).SequenceEqual(
                    ProtectedSyntax.Matches(translatedMarkdown).Select(match => match.Value), StringComparer.Ordinal))
                return original;
            var result = new Translation(translatedMarkdown, !string.Equals(translatedMarkdown, markdown, StringComparison.Ordinal));
            lock (_cacheLock)
            {
                if (!_cache.ContainsKey(key))
                {
                    if (_cache.Count >= CacheCapacity) _cache.Remove(_cacheOrder.Dequeue());
                    _cacheOrder.Enqueue(key);
                }
                _cache[key] = result;
            }
            return result;
        }
        catch (Exception)
        {
            // Translation never prevents opening the popup or downloading an
            // update. Failures/cancellation are not cached, so a later open can retry.
            return original;
        }
    }

    private static List<(int Start, int Length)> FindTextRuns(string markdown)
    {
        var runs = new List<(int Start, int Length)>();
        void Add(int start, int end)
        {
            while (start < end && char.IsWhiteSpace(markdown[start])) start++;
            while (end > start && char.IsWhiteSpace(markdown[end - 1])) end--;
            for (var index = start; index < end; index++)
                if (char.IsLetter(markdown[index]))
                {
                    runs.Add((start, end - start));
                    break;
                }
        }
        var cursor = 0;
        foreach (Match match in ProtectedSyntax.Matches(markdown))
        {
            Add(cursor, match.Index);
            cursor = match.Index + match.Length;
        }
        Add(cursor, markdown.Length);
        return runs;
    }
}
