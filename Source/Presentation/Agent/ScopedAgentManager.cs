using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Models.Agent;

namespace RimMind.Presentation.Agent
{
    public sealed class ScopedAgentManager : IScopedAgentManager
    {
        private readonly IScopedAgentFactory _factory;
        private readonly IAgentLoopScheduler _scheduler;
        private readonly Dictionary<string, IScopedAgent> _agents = new();

        public ScopedAgentManager(IScopedAgentFactory factory, IAgentLoopScheduler scheduler)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        public IScopedAgent GetOrCreate(string scopeType, string scopeId, IAgentBus agentBus, int? mapId = null)
        {
            var key = LegacyCompositeKey(scopeType, scopeId);
            return GetOrCreateByKey(
                key,
                () => _factory.Create(AgentScope.Custom(scopeType, scopeId, mapId), agentBus));
        }

        public IScopedAgent GetOrCreate(AgentScope scope, IAgentBus agentBus)
        {
            var key = scope.CompositeKey;
            return GetOrCreateByKey(key, () => _factory.Create(scope, agentBus));
        }

        private IScopedAgent GetOrCreateByKey(string key, Func<IScopedAgent> createAgent)
        {
            if (_agents.TryGetValue(key, out var existing))
                return existing;

            var agent = createAgent();
            try
            {
                _scheduler.Register(AgentLoopKeys.ForScoped(key), AgentLoopKind.Scoped, agent);
            }
            catch (Exception registrationError)
            {
                var teardownErrors = new List<Exception>();
                CollectLifecycleErrors(agent, teardownErrors);
                if (teardownErrors.Count == 0)
                    throw;

                teardownErrors.Insert(0, registrationError);
                throw new AggregateException("Scoped agent registration and teardown failed.", teardownErrors);
            }

            _agents[key] = agent;
            return agent;
        }

        public IScopedAgent? Find(string scopeType, string scopeId)
        {
            var key = LegacyCompositeKey(scopeType, scopeId);
            return _agents.TryGetValue(key, out var agent) ? agent : null;
        }

        public IScopedAgent? Find(AgentScope scope)
        {
            var key = scope.CompositeKey;
            return _agents.TryGetValue(key, out var agent) ? agent : null;
        }

        public IReadOnlyList<IScopedAgent> GetAll()
        {
            var result = new List<IScopedAgent>(_agents.Values);
            return result.AsReadOnly();
        }

        public bool Remove(string scopeType, string scopeId)
        {
            var key = LegacyCompositeKey(scopeType, scopeId);
            return RemoveByKey(key);
        }

        public bool Remove(AgentScope scope)
        {
            return RemoveByKey(scope.CompositeKey);
        }

        private bool RemoveByKey(string key)
        {
            if (_agents.TryGetValue(key, out var agent))
            {
                _scheduler.Unregister(AgentLoopKeys.ForScoped(key));
                var errors = new List<Exception>();
                try
                {
                    CollectLifecycleErrors(agent, errors);
                }
                finally
                {
                    _agents.Remove(key);
                }

                ThrowCollectedErrors(errors);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            var entries = new List<KeyValuePair<string, IScopedAgent>>(_agents);
            var errors = new List<Exception>();

            foreach (var pair in entries)
            {
                try
                {
                    _scheduler.Unregister(AgentLoopKeys.ForScoped(pair.Key));
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                    continue;
                }

                try
                {
                    CollectLifecycleErrors(pair.Value, errors);
                }
                finally
                {
                    _agents.Remove(pair.Key);
                }
            }

            ThrowCollectedErrors(errors);
        }

        private static void CollectLifecycleErrors(IScopedAgent agent, ICollection<Exception> errors)
        {
            try
            {
                agent.Cleanup();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }

            try
            {
                agent.Destroy();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        private static void ThrowCollectedErrors(IReadOnlyCollection<Exception> errors)
        {
            if (errors.Count > 0)
                throw new AggregateException("Scoped agent teardown failed.", errors);
        }

        private static string LegacyCompositeKey(string scopeType, string scopeId)
            => (string.IsNullOrWhiteSpace(scopeType) ? "unknown" : scopeType)
                + ":"
                + (string.IsNullOrWhiteSpace(scopeId) ? "unknown" : scopeId);
    }
}
