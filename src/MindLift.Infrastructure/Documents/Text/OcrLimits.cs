using System.Text;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;

namespace MindLift.Infrastructure.Documents.Text;

/// <summary>Raised when too many documents are being read at once and a place did not free up in time. The document is left to try again.</summary>
public sealed class OcrBusyException() : Exception("The document reader is busy with other documents.");

/// <summary>
/// Limits how many documents are read at the same time, across the whole app. Reading is heavy (rendering and recognising pages), so a rush of uploads waits its turn
/// instead of taking every processor from the people using the app.
/// </summary>
public sealed class OcrGate(IOptions<OcrOptions> options)
{
    private readonly SemaphoreSlim _places = new(Math.Max(1, options.Value.MaxConcurrentDocuments));

    /// <summary>Waits up to <c>QueueSeconds</c> for a place. Dispose the result to give the place back.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        var wait = TimeSpan.FromSeconds(Math.Max(0, options.Value.QueueSeconds));
        if (!await _places.WaitAsync(wait, cancellationToken))
        {
            throw new OcrBusyException();
        }

        return new Place(_places);
    }

    private sealed class Place(SemaphoreSlim places) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                places.Release();
            }
        }
    }
}

/// <summary>Reads text with a ceiling, so a file or a program's output of any size cannot fill memory.</summary>
public static class BoundedText
{
    /// <summary>
    /// Reads up to <paramref name="maxChars"/> characters. <c>Truncated</c> says there was more. With <paramref name="drain"/> the rest is read and thrown away,
    /// which a program's error output needs so that it never blocks writing; without it reading stops at the limit.
    /// </summary>
    public static async Task<(string Text, bool Truncated)> ReadAsync(TextReader reader, int maxChars, bool drain, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            var room = maxChars - text.Length;
            if (room > 0)
            {
                text.Append(buffer, 0, Math.Min(read, room));
            }

            if (read > room)
            {
                truncated = true;
                if (!drain)
                {
                    break;
                }
            }
        }

        return (text.ToString(), truncated);
    }
}
