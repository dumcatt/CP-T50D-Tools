namespace CPT50DTools.Cards;

/// <summary>
/// Background thumbnail decoder. The most recently requested card is decoded first so
/// the tiles currently on screen load before ones that were scrolled past.
/// </summary>
public sealed class ThumbnailLoader : IDisposable
{
    private readonly LinkedList<CardItem> _queue = new();
    private readonly Dictionary<CardItem, LinkedListNode<CardItem>> _queued = new();
    private readonly HashSet<CardItem> _inFlight = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread[] _workers;
    private int _generation;

    /// <summary>Raised on a worker thread after a thumbnail has been set.</summary>
    public event Action<CardItem>? ThumbnailLoaded;

    public ThumbnailLoader(int workerCount = 2)
    {
        _workers = new Thread[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            _workers[i] = new Thread(Work) { IsBackground = true, Name = $"Thumbnail worker {i}", Priority = ThreadPriority.BelowNormal };
            _workers[i].Start();
        }
    }

    public void Request(CardItem card)
    {
        lock (_lock)
        {
            if (card.Thumbnail != null || card.ThumbnailFailed) return;
            // Already being decoded: re-queuing it would leave a queue entry without a matching signal.
            if (_inFlight.Contains(card)) return;
            if (_queued.TryGetValue(card, out var node))
            {
                // Move to the front: it is visible right now.
                _queue.Remove(node);
                _queue.AddFirst(node);
                return;
            }
            _queued[card] = _queue.AddFirst(card);
        }
        _signal.Release();
    }

    /// <summary>Drops all pending requests (e.g. when the card list is cleared).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
            _queued.Clear();
            _generation++;
        }
    }

    private void Work()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            try { _signal.Wait(token); }
            catch (OperationCanceledException) { return; }

            // Drain the queue rather than taking one card per signal, so a missed signal can never
            // leave requested cards waiting forever.
            while (!token.IsCancellationRequested)
            {
                CardItem card;
                int gen;
                lock (_lock)
                {
                    if (_queue.Count == 0) break;
                    card = _queue.First!.Value;
                    _queue.RemoveFirst();
                    _queued.Remove(card);
                    _inFlight.Add(card);
                    gen = _generation;
                }

                Bitmap? thumb = null;
                bool failed = false;
                try { thumb = card.CreateThumbnail(); }
                catch { failed = true; }

                lock (_lock)
                {
                    _inFlight.Remove(card);
                    if (gen != _generation)
                    {
                        thumb?.Dispose();
                        continue;
                    }
                    card.Thumbnail = thumb;
                    card.ThumbnailFailed = failed;
                }
                ThumbnailLoaded?.Invoke(card);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _signal.Dispose();
    }
}
