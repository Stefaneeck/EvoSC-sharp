﻿using System;
using System.Collections.Generic;
using NLog;

namespace EvoSC.Core.Events;

public class EventManager : IDisposable
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    public EventManager()
    {
    }

    private Dictionary<EventType, Dictionary<Guid, Action>> _eventCache = new Dictionary<EventType, Dictionary<Guid, Action>>();
    private bool _disposedValue;

    public void RegisterEventType(EventType eventType, Guid pluginId, Action action)
    {
        if (!_eventCache.ContainsKey(eventType))
            _eventCache.Add(eventType, new Dictionary<Guid, Action>());

        if (!_eventCache[eventType].ContainsKey(pluginId))
            _eventCache[eventType].Add(pluginId, action);

        _logger.Trace($"Added new event of type '{eventType} for plugin {pluginId}");
    }

    public void UnregisterEvent(EventType eventType, Guid pluginId)
    {
        if (_eventCache.ContainsKey(eventType))
            _eventCache[eventType].Remove(pluginId);

        _logger.Trace($"Removed event of type '{eventType} for plugin {pluginId}");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            _eventCache.Clear();
            _eventCache = null;

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
