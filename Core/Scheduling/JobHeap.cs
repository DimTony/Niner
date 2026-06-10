namespace Core.Scheduling;

public record HeapEntry(Guid JobId, double Score);

public class JobHeap
{
    private readonly List<HeapEntry> _heap = new();

    public int Count => _heap.Count;

    public void Push(HeapEntry entry)
    {
        _heap.Add(entry);
        BubbleUp(_heap.Count - 1);
    }

    public HeapEntry? Pop()
    {
        if (_heap.Count == 0) return null;

        var min = _heap[0];
        var last = _heap.Count - 1;

        _heap[0] = _heap[last];
        _heap.RemoveAt(last);

        if (_heap.Count > 0)
            SiftDown(0);

        return min;
    }

    public HeapEntry? Peek()
        => _heap.Count > 0 ? _heap[0] : null;

    public void UpdateScore(Guid jobId, double newScore)
    {
        var index = _heap.FindIndex(e => e.JobId == jobId);
        if (index < 0) return;

        var oldScore = _heap[index].Score;
        _heap[index] = new HeapEntry(jobId, newScore);

        if (newScore < oldScore)
            BubbleUp(index);
        else
            SiftDown(index);
    }

    public void Remove(Guid jobId)
    {
        var index = _heap.FindIndex(e => e.JobId == jobId);
        if (index < 0) return;

        var last = _heap.Count - 1;
        if (index == last)
        {
            _heap.RemoveAt(last);
            return;
        }

        _heap[index] = _heap[last];
        _heap.RemoveAt(last);

        BubbleUp(index);
        SiftDown(index);
    }

    private void BubbleUp(int i)
    {
        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (_heap[parent].Score <= _heap[i].Score) break;
            Swap(i, parent);
            i = parent;
        }
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            var left    = 2 * i + 1;
            var right   = 2 * i + 2;
            var smallest = i;

            if (left  < _heap.Count && _heap[left].Score  < _heap[smallest].Score)
                smallest = left;
            if (right < _heap.Count && _heap[right].Score < _heap[smallest].Score)
                smallest = right;

            if (smallest == i) break;

            Swap(i, smallest);
            i = smallest;
        }
    }

    private void Swap(int a, int b)
        => (_heap[a], _heap[b]) = (_heap[b], _heap[a]);
}