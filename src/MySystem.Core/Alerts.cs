namespace MySystem.Core;

public enum AlertComparison { Above, Below }
public sealed record AlertRule(string Id, string Name, string MetricId, double Threshold,
    TimeSpan SustainedFor, TimeSpan Cooldown, bool Enabled = true, AlertComparison Comparison = AlertComparison.Above);
public sealed record AlertEvent(string RuleId, string Name, DateTimeOffset Timestamp, Metric Metric, bool IsCleared);

/// <summary>Thresholds are user preferences, not universal hardware safety claims.</summary>
public sealed class AlertEngine
{
    private sealed class State
    {
        public DateTimeOffset? Since;
        public DateTimeOffset? LastEvent;
        public bool Active;
        public AlertRule? Definition;
    }
    private readonly Dictionary<string, State> _states = new();
    private readonly Queue<AlertEvent> _history = new();
    public IReadOnlyList<AlertEvent> History => _history.ToArray();

    public IReadOnlyList<AlertEvent> Evaluate(TelemetrySnapshot snapshot, IEnumerable<AlertRule> rules)
    {
        var events = new List<AlertEvent>();
        var known = new HashSet<string>();
        foreach (var rule in rules)
        {
            known.Add(rule.Id);
            if (!_states.TryGetValue(rule.Id, out var state) || state.Definition != rule)
                _states[rule.Id] = state = new State { Definition = rule };
            var metric = snapshot.Find(rule.MetricId);
            if (!rule.Enabled || metric is not { Availability: MetricAvailability.Available, Value: not null } ||
                !double.IsFinite(metric.Value.Value))
            {
                state.Since = null; // Missing samples cannot satisfy a sustained threshold.
                continue;
            }
            bool exceeded = rule.Comparison == AlertComparison.Above
                ? metric.Value.Value > rule.Threshold : metric.Value.Value < rule.Threshold;
            if (!exceeded)
            {
                state.Since = null;
                if (state.Active)
                {
                    state.Active = false;
                    events.Add(new(rule.Id, rule.Name, snapshot.Timestamp, metric, true));
                }
                continue;
            }
            state.Since ??= snapshot.Timestamp;
            if (!state.Active && snapshot.Timestamp - state.Since >= rule.SustainedFor &&
                (state.LastEvent == null || snapshot.Timestamp - state.LastEvent >= rule.Cooldown))
            {
                state.Active = true;
                state.LastEvent = snapshot.Timestamp;
                events.Add(new(rule.Id, rule.Name, snapshot.Timestamp, metric, false));
            }
        }
        foreach (var id in _states.Keys.Where(id => !known.Contains(id)).ToArray()) _states.Remove(id);
        foreach (var entry in events)
        {
            _history.Enqueue(entry);
            while (_history.Count > 500) _history.Dequeue();
        }
        return events;
    }
}
