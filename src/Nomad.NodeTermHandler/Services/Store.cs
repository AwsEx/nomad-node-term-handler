using Microsoft.Extensions.Options;
using Nomad.NodeTermHandler.Configuration;
using Nomad.NodeTermHandler.Models;
using Serilog;

namespace Nomad.NodeTermHandler.Services;

public class Store
{
    private readonly IOptions<Settings> _settings;
    private readonly Dictionary<string, InterruptionEvent> _interruptionEventStore;
    private readonly HashSet<string> _ignoredEvents;
    private bool _atLeastOneEvent;
    private int _callsSinceLastClean;
    private int _callsSinceLastLog;
    private readonly int _cleaningPeriod;
    private readonly int _loggingPeriod;

    public Store(IOptions<Settings> settings)
    {
        _settings = settings;
        _interruptionEventStore = new Dictionary<string, InterruptionEvent>();
        _ignoredEvents = new HashSet<string>();
        _cleaningPeriod = 7200;
        _loggingPeriod = 1800;
    }

    public void CancelInterruptionEvent(string eventId)
    {
        lock (_interruptionEventStore)
        {
            _interruptionEventStore.Remove(eventId);
        }
    }

    public void AddInterruptionEvent(InterruptionEvent interruptionEvent)
    {
        lock (_interruptionEventStore)
        {
            if (_interruptionEventStore.ContainsKey(interruptionEvent.EventId))
            {
                return;
            }

            Log.Information("Adding new event to the event store: {@event}", interruptionEvent);
            _interruptionEventStore[interruptionEvent.EventId] = interruptionEvent;

            if (!_ignoredEvents.Contains(interruptionEvent.EventId))
            {
                _atLeastOneEvent = true;
            }
        }
    }

    public (InterruptionEvent?, bool) GetActiveEvent()
    {
        CleanPeriodically();
        LogPeriodically();

        lock (_interruptionEventStore)
        {
            foreach (var interruptionEvent in _interruptionEventStore.Values)
            {
                if (ShouldEventDrain(interruptionEvent))
                {
                    return (interruptionEvent, true);
                }
            }

            return (null, false);
        }
    }

    public bool ShouldDrainNode()
    {
        lock (_interruptionEventStore)
        {
            return _interruptionEventStore.Values.Any(ShouldEventDrain);
        }
    }

    private bool ShouldEventDrain(InterruptionEvent interruptionEvent)
    {
        bool ignored = _ignoredEvents.Contains(interruptionEvent.EventId);

        return !ignored && interruptionEvent is { InProgress: false, NodeProcessed: false } && TimeUntilDrain(interruptionEvent) <= TimeSpan.Zero;
    }

    public TimeSpan TimeUntilDrain(InterruptionEvent interruptionEvent)
    {
        TimeSpan nodeTerminationGracePeriod = TimeSpan.FromSeconds(_settings.Value.NodeTerminationGracePeriod);
        DateTime drainTime = interruptionEvent.StartTime.Add(-1 * nodeTerminationGracePeriod);
        return drainTime - DateTime.UtcNow;
    }

    public void MarkAllAsProcessed(string nodeName)
    {
        lock (_interruptionEventStore)
        {
            foreach (var interruptionEvent in _interruptionEventStore.Values)
            {
                if (interruptionEvent.NodeName == nodeName)
                {
                    interruptionEvent.NodeProcessed = true;
                }
            }
        }
    }

    public void IgnoreEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
        {
            return;
        }

        lock (_ignoredEvents)
        {
            _ignoredEvents.Add(eventId);
        }
    }

    public bool ShouldUncordonNode(string nodeName)
    {
        lock (_interruptionEventStore)
        {
            if (!_atLeastOneEvent)
            {
                return false;
            }

            if (_interruptionEventStore.Count == 0)
            {
                return true;
            }

            foreach (var interruptionEvent in _interruptionEventStore.Values)
            {
                if (!_ignoredEvents.Contains(interruptionEvent.EventId) && interruptionEvent.NodeName == nodeName)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private void CleanPeriodically()
    {
        lock (_interruptionEventStore)
        {
            _callsSinceLastClean++;
            if (_callsSinceLastClean < _cleaningPeriod)
            {
                return;
            }

            Log.Information("Garbage-collecting the interruption event store");
            var toDelete = new List<string>();

            foreach (var e in _interruptionEventStore.Values)
            {
                if (e.NodeProcessed)
                {
                    toDelete.Add(e.EventId);
                }
            }

            foreach (var id in toDelete)
            {
                _interruptionEventStore.Remove(id);
            }

            _callsSinceLastClean = 0;
        }
    }

    private void LogPeriodically()
    {
        lock (_interruptionEventStore)
        {
            _callsSinceLastLog++;
            if (_callsSinceLastLog < _loggingPeriod)
            {
                return;
            }

            int drainableEventCount = _interruptionEventStore.Values.Count(ShouldEventDrain);

            Log.Information("Event store statistics: size = {size}, drainable-events = {drainableEvents}", _interruptionEventStore.Count, drainableEventCount);
            _callsSinceLastLog = 0;
        }
    }
}