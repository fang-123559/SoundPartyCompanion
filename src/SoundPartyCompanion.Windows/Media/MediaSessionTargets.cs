namespace SoundPartyCompanion.Windows.Media;

// Tokens identify session objects, never application IDs shared by browser tabs.
internal sealed class MediaSessionTargets<T> where T : class
{
    private readonly Dictionary<string, T> _targets = new();
    public void Update(IEnumerable<T> sessions)
    {
        var current = sessions.ToArray();
        lock (_targets)
        {
            foreach (var id in _targets.Where(pair => !current.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
                _targets.Remove(id);
            foreach (var session in current)
                if (!_targets.ContainsValue(session)) _targets.Add(Guid.NewGuid().ToString("N"), session);
        }
    }
    public string? GetId(T? session)
    {
        lock (_targets) return _targets.FirstOrDefault(pair => Equals(pair.Value, session)).Key;
    }
    public T? Resolve(string id)
    {
        lock (_targets) return _targets.GetValueOrDefault(id);
    }
}
