namespace Core.Scheduling;

public class TimingWheel
{
    private readonly int _slotCount;
    private readonly HashSet<Guid>[] _slots;
    private int _currentSlot;

    public int CurrentSlot => _currentSlot;
    public int SlotCount   => _slotCount;

    public TimingWheel(int slotCount = 3600)
    {
        _slotCount   = slotCount;
        _slots       = new HashSet<Guid>[slotCount];
        _currentSlot = 0;

        for (var i = 0; i < slotCount; i++)
            _slots[i] = new HashSet<Guid>();
    }

    // Assign a job to the slot corresponding to its scheduled time
    public int AddJob(Guid jobId, DateTimeOffset scheduledAt)
    {
        var slot = (int)(scheduledAt.ToUnixTimeSeconds() % _slotCount);
        _slots[slot].Add(jobId);
        return slot;
    }

    // Advance the wheel by one tick — returns jobs due in this slot
    public IReadOnlyList<Guid> Tick()
    {
        var due = _slots[_currentSlot].ToList();
        _slots[_currentSlot].Clear();
        _currentSlot = (_currentSlot + 1) % _slotCount;
        return due;
    }

    // Peek without advancing — used for benchmarking
    public IReadOnlyList<Guid> Peek(int slot)
    {
        var s = slot % _slotCount;
        return _slots[s].ToList();
    }

    public void RemoveJob(Guid jobId, int slot)
    {
        _slots[slot % _slotCount].Remove(jobId);
    }

    public int GetSlot(DateTimeOffset scheduledAt)
        => (int)(scheduledAt.ToUnixTimeSeconds() % _slotCount);
}