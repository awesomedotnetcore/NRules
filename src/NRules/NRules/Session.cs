using System;
using System.Collections.Generic;
using System.Linq;
using NRules.Diagnostics;
using NRules.Rete;

namespace NRules
{
    /// <summary>
    /// Represents a rules engine session. Created by <see cref="ISessionFactory"/>.
    /// Each session has its own working memory, and exposes operations that 
    /// manipulate facts in it, as well as fire matching rules.
    /// </summary>
    /// <event cref="IEventProvider.FactInsertingEvent">Before processing fact insertion.</event>
    /// <event cref="IEventProvider.FactInsertedEvent">After processing fact insertion.</event>
    /// <event cref="IEventProvider.FactUpdatingEvent">Before processing fact update.</event>
    /// <event cref="IEventProvider.FactUpdatedEvent">After processing fact update.</event>
    /// <event cref="IEventProvider.FactRetractingEvent">Before processing fact retraction.</event>
    /// <event cref="IEventProvider.FactRetractedEvent">After processing fact retraction.</event>
    /// <event cref="IEventProvider.ActivationCreatedEvent">When a set of facts matches a rule.</event>
    /// <event cref="IEventProvider.ActivationUpdatedEvent">When a set of facts is updated and re-matches a rule.</event>
    /// <event cref="IEventProvider.ActivationDeletedEvent">When a set of facts no longer matches a rule.</event>
    /// <event cref="IEventProvider.RuleFiringEvent">Before rule's actions are executed.</event>
    /// <event cref="IEventProvider.RuleFiredEvent">After rule's actions are executed.</event>
    /// <event cref="IEventProvider.ConditionFailedEvent">When there is an error during condition evaluation,
    /// before throwing exception to the client.</event>
    /// <event cref="IEventProvider.ActionFailedEvent">When there is an error during action evaluation,
    /// before throwing exception to the client.</event>
    /// <exception cref="RuleConditionEvaluationException">Error while evaluating any of the rules' conditions.
    /// This exception can also be observed as an event <see cref="IEventProvider.ConditionFailedEvent"/>.</exception>
    /// <exception cref="RuleActionEvaluationException">Error while evaluating any of the rules' actions.
    /// This exception can also be observed as an event <see cref="IEventProvider.ActionFailedEvent"/>.</exception>
    /// <threadsafety instance="false" />
    public interface ISession
    {
        /// <summary>
        /// Provider of events from the current rule session.
        /// Use it to subscribe to various rules engine lifecycle events.
        /// </summary>
        IEventProvider Events { get; }

        /// <summary>
        /// Rules dependency resolver.
        /// </summary>
        IDependencyResolver DependencyResolver { get; set; }

        /// <summary>
        /// Inserts new facts to the rules engine memory.
        /// </summary>
        /// <remarks>Bulk session operations are more performant than individual operations on a set of facts.</remarks>
        /// <param name="facts">Facts to add.</param>
        /// <exception cref="ArgumentException">If any fact already exists in working memory.</exception>
        void InsertAll(IEnumerable<object> facts);

        /// <summary>
        /// Inserts new fact to the rules engine memory.
        /// </summary>
        /// <remarks>Bulk session operations are more performant than individual operations on a set of facts.</remarks>
        /// <param name="fact">Facts to add.</param>
        /// <exception cref="ArgumentException">If fact already exists in working memory.</exception>
        void Insert(object fact);

        /// <summary>
        /// Inserts a fact to the rules engine memory if the fact does not exist.
        /// </summary>
        /// <param name="fact">Fact to add.</param>
        /// <returns>Whether the fact was inserted or not.</returns>
        bool TryInsert(object fact);

        /// <summary>
        /// Updates existing facts in the rules engine memory.
        /// </summary>
        /// <remarks>Bulk session operations are more performant than individual operations on a set of facts.</remarks>
        /// <param name="facts">Facts to update.</param>
        /// <exception cref="ArgumentException">If any fact does not exist in working memory.</exception>
        void UpdateAll(IEnumerable<object> facts);

        /// <summary>
        /// Updates existing fact in the rules engine memory.
        /// </summary>
        /// <remarks>Bulk session operations are more performant than individual operations on a set of facts.</remarks>
        /// <param name="fact">Fact to update.</param>
        /// <exception cref="ArgumentException">If fact does not exist in working memory.</exception>
        void Update(object fact);

        /// <summary>
        /// Updates a fact in the rules engine memory if the fact exists.
        /// </summary>
        /// <remarks>Bulk session operations are more performant than individual operations on a set of facts.</remarks>
        /// <param name="fact">Fact to update.</param>
        /// <returns>Whether the fact was updated or not.</returns>
        bool TryUpdate(object fact);

        /// <summary>
        /// Removes existing facts from the rules engine memory.
        /// </summary>
        /// <remarks>Bulk session operations are more performant than individual operations on a set of facts.</remarks>
        /// <param name="facts">Facts to remove.</param>
        /// <exception cref="ArgumentException">If any fact does not exist in working memory.</exception>
        void RetractAll(IEnumerable<object> facts);

        /// <summary>
        /// Removes existing fact from the rules engine memory.
        /// </summary>
        /// <remarks>Bulk session operations are more performant than individual operations on a set of facts.</remarks>
        /// <param name="fact">Fact to remove.</param>
        /// <exception cref="ArgumentException">If fact does not exist in working memory.</exception>
        void Retract(object fact);

        /// <summary>
        /// Removes a fact from the rules engine memory if the fact exists.
        /// </summary>
        /// <remarks>Bulk session operations are more performant than individual operations on a set of facts.</remarks>
        /// <param name="facts">Fact to remove.</param>
        /// <returns>Whether the fact was retracted or not.</returns>
        bool TryRetract(object facts);

        /// <summary>
        /// Starts rules execution cycle.
        /// This method blocks until there are no more rules to fire.
        /// </summary>
        void Fire();

        /// <summary>
        /// Creates a LINQ query to retrieve facts of a given type from the rules engine's memory.
        /// </summary>
        /// <typeparam name="TFact">Type of facts to query. Use <see cref="object"/> to query all facts.</typeparam>
        /// <returns>Queryable working memory of the rules engine.</returns>
        IQueryable<TFact> Query<TFact>();
    }

    /// <summary>
    /// See <see cref="ISession"/>.
    /// </summary>
    public sealed class Session : ISession, ISessionSnapshotProvider
    {
        private readonly IAgenda _agenda;
        private readonly INetwork _network;
        private readonly IWorkingMemory _workingMemory;
        private readonly IEventAggregator _eventAggregator;
        private readonly IExecutionContext _executionContext;

        internal Session(INetwork network, IAgenda agenda, IWorkingMemory workingMemory, IEventAggregator eventAggregator, IDependencyResolver dependencyResolver)
        {
            _network = network;
            _workingMemory = workingMemory;
            _agenda = agenda;
            _eventAggregator = eventAggregator;
            _executionContext = new ExecutionContext(this, _workingMemory, _agenda, _eventAggregator);
            DependencyResolver = dependencyResolver;
            _network.Activate(_executionContext);
        }

        public IEventProvider Events { get { return _eventAggregator; } }

        public IDependencyResolver DependencyResolver { get; set; }

        public void InsertAll(IEnumerable<object> facts)
        {
            var result = _network.PropagateAssert(_executionContext, facts);
            if (result.FailedCount > 0)
            {
                throw new ArgumentException("Facts for insert already exist", "facts");
            }
        }

        public void Insert(object fact)
        {
            InsertAll(new[] { fact });
        }

        public bool TryInsert(object fact)
        {
            var result = _network.PropagateAssert(_executionContext, new[] {fact});
            return result.FailedCount == 0;
        }

        public void UpdateAll(IEnumerable<object> facts)
        {
            var result = _network.PropagateUpdate(_executionContext, facts);
            if (result.FailedCount > 0)
            {
                throw new ArgumentException("Facts for update do not exist", "facts");
            }
        }

        public void Update(object fact)
        {
            UpdateAll(new[] {fact});
        }

        public bool TryUpdate(object fact)
        {
            var result = _network.PropagateUpdate(_executionContext, new[] {fact});
            return result.FailedCount == 0;
        }

        public void RetractAll(IEnumerable<object> facts)
        {
            var result = _network.PropagateRetract(_executionContext, facts);
            if (result.FailedCount > 0)
            {
                throw new ArgumentException("Facts for retract do not exist", "facts");
            }
        }

        public void Retract(object fact)
        {
            RetractAll(new[] {fact});
        }

        public bool TryRetract(object fact)
        {
            var result = _network.PropagateRetract(_executionContext, new[] {fact});
            return result.FailedCount == 0;
        }

        public void Fire()
        {
            while (_agenda.HasActiveRules())
            {
                Activation activation = _agenda.NextActivation();
                ICompiledRule rule = activation.Rule;
                var actionContext = new ActionContext(rule, this);

                _eventAggregator.RaiseRuleFiring(this, activation);
                foreach (IRuleAction action in rule.Actions)
                {
                    action.Invoke(_executionContext, actionContext, activation.Tuple, activation.TupleFactMap);
                }
                _eventAggregator.RaiseRuleFired(this, activation);

                if (actionContext.IsHalted) break;
            }
        }

        public IQueryable<TFact> Query<TFact>()
        {
            return _workingMemory.Facts.Select(x => x.Object).OfType<TFact>().AsQueryable();
        }

        SessionSnapshot ISessionSnapshotProvider.GetSnapshot()
        {
            var builder = new SnapshotBuilder();
            var visitor = new SessionSnapshotVisitor(_workingMemory);
            _network.Visit(builder, visitor);
            return builder.Build();
        }
    }
}
