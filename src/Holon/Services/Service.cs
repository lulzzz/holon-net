﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Holon.Services
{
    /// <summary>
    /// Represents an service.
    /// </summary>
    public class Service : IDisposable
    {
        #region Fields
        private Node _node;
        private ServiceAddress _addr;
        private Broker _broker;
        private bool _disposed;
        private BrokerQueue _queue;
        private IServiceBehaviour _behaviour;
        private CancellationTokenSource _loopCancel;
        private ServiceType _type;
        private ServiceExecution _execution;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the service execution strategy.
        /// </summary>
        public ServiceExecution Execution {
            get {
                return _execution;
            }
        }

        /// <summary>
        /// Gets the service type.
        /// </summary>
        public ServiceType Type {
            get {
                return _type;
            }
        }

        /// <summary>
        /// Gets the underlying behaviour.
        /// </summary>
        public IServiceBehaviour Behaviour {
            get {
                return _behaviour;
            }
        }

        /// <summary>
        /// Gets the service address.
        /// </summary>
        public ServiceAddress Address {
            get {
                return _addr;
            }
        }

        /// <summary>
        /// Gets the broker.
        /// </summary>
        internal Broker Broker {
            get {
                return _broker;
            }
        }
        #endregion

        #region Events
        /// <summary>
        /// Called when a service behaviour creates an unhandled exception.
        /// </summary>
        public event EventHandler<ServiceExceptionEventArgs> UnhandledException;

        /// <summary>
        /// Handles unhandled exceptions.
        /// </summary>
        /// <param name="e">The exception event args.</param>
        /// <returns>If the exception was handled.</returns>
        protected bool OnUnhandledException(ServiceExceptionEventArgs e) {
            if (UnhandledException == null)
                return false;

            UnhandledException.Invoke(this, e);
            return true;
        }
        #endregion

        #region Methods
        /// <summary>
        /// Disposes the service.
        /// </summary>
        public void Dispose() {
            if (_disposed)
                return;
            _disposed = true;

#if DEBUG_DISPOSE
            Debug.WriteLine("> Service::Dispose: {0}", _addr);
#endif

            // cancel loop
            if (_loopCancel != null)
                _loopCancel.Cancel();

            // dispose of queue
            _queue.Dispose();

#if DEBUG_DISPOSE
            Debug.WriteLine("< Service::Disposed: {0}", _addr);
#endif
        }

        /// <summary>
        /// Creates the queue and internal consumer for this service.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the queue already exists.</exception>
        /// <returns></returns>
        internal async Task<BrokerQueue> SetupAsync() {
            // check if queue has already been created
            if (_queue != null)
                throw new InvalidOperationException("The broker queue has already been created");

            // create queue
            await _broker.DeclareExchange(_addr.Namespace, "topic", true, false);

            if (_type == ServiceType.Singleton) {
                // declare one exclusive queue
                _queue = await _broker.CreateQueueAsync(_addr.ToString(), false, true, _addr.Namespace, _addr.RoutingKey, null);
            } else if (_type == ServiceType.Fanout) {
                // declare queue with unique name
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) {
                    // get unique string
                    byte[] uniqueId = new byte[20];
                    rng.GetBytes(uniqueId);
                    string uniqueIdStr = BitConverter.ToString(uniqueId).Replace("-", "").ToLower();

                    _queue = await _broker.CreateQueueAsync(string.Format("{0}%{1}", _addr.ToString(), uniqueIdStr), false, false, _addr.Namespace, _addr.RoutingKey, null);
                }
            } else if (_type == ServiceType.Balanced) {
                // declare one queue shared between many
                _queue = await _broker.CreateQueueAsync(_addr.ToString(), false, false, _addr.Namespace, _addr.RoutingKey, null);
            }


            // begin loop
            ServiceLoop();

            return _queue;
        }

        /// <summary>
        /// Explicitly binds this service to another routing key in the namespace.
        /// </summary>
        /// <param name="routingKey">The routing key.</param>
        /// <returns></returns>
        public Task BindAsync(string routingKey) {
            return _queue.BindAsync(_addr.Namespace, routingKey);
        }

        /// <summary>
        /// Changes the broker then creates the queue and internal consumer for this service.
        /// </summary>
        /// <param name="broker"></param>
        /// <returns></returns>
        internal Task<BrokerQueue> ResetupAsync(Broker broker) {
            // cancel existing loop
            _loopCancel.Cancel();

            // resetup
            _broker = broker;
            _queue = null;
            return SetupAsync();
        }

        /// <summary>
        /// Handles a single envelope.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        private async void ServiceHandle(Envelope envelope) {
            try {
                if (_behaviour is IAsyncServiceBehaviour)
                    await ((IAsyncServiceBehaviour)_behaviour).HandleAsync(envelope);
                else
                    await Task.Run(() => _behaviour.Handle(envelope));
            } catch (Exception ex) {
                OnUnhandledException(new ServiceExceptionEventArgs(_behaviour, ex));
            }
        }

        /// <summary>
        /// Handles a single envelope asyncronously.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        private Task ServiceHandleAsync(Envelope envelope) {
            if (_behaviour is IAsyncServiceBehaviour)
                return ((IAsyncServiceBehaviour)_behaviour).HandleAsync(envelope);
            else
                return Task.Run(() => _behaviour.Handle(envelope));
        }

        /// <summary>
        /// Service worker to receive messages from queue and hand to behaviour.
        /// </summary>
        private async void ServiceLoop() {
            // assert loop not running
            Debug.Assert(_loopCancel == null, "ServiceLoop already running");

            // create cancellation token
            _loopCancel = new CancellationTokenSource();

            while (true) {
                Envelope envelope = null;

                try {
                    envelope = new Envelope(await _queue.ReceiveAsync(_loopCancel.Token), _node);
                } catch(OperationCanceledException) {
                    return;
                }

                // handle
                try {
                    if (_execution == ServiceExecution.Serial) {
                        await ServiceHandleAsync(envelope);
                    } else {
                        ServiceHandle(envelope);
                    }
                } catch(Exception ex) {
                    OnUnhandledException(new ServiceExceptionEventArgs(_behaviour, ex));
                }
            }
        }
        #endregion

        #region Constructors
        internal Service(Node node, Broker broker, ServiceAddress addr, IServiceBehaviour behaviour, ServiceType type, ServiceExecution execution) {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));
            _behaviour = behaviour ?? throw new ArgumentNullException(nameof(behaviour));
            _addr = addr;
            _type = type;
            _execution = execution;
        }
        #endregion
    }

    /// <summary>
    /// Represents arguments for an unhandled service exception event.
    /// </summary>
    public class ServiceExceptionEventArgs
    {
        #region Fields
        private Exception _exception;
        private IServiceBehaviour _behaviour;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the exception.
        /// </summary>
        public Exception Exception {
            get {
                return _exception;
            }
        }

        /// <summary>
        /// Gets the behaviour which raised the exception.
        /// </summary>
        public IServiceBehaviour Behaviour {
            get {
                return _behaviour;
            }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new service exception event argument object.
        /// </summary>
        /// <param name="behaviour">The behaviour which raised the exception.</param>
        /// <param name="ex">The exception.</param>
        public ServiceExceptionEventArgs(IServiceBehaviour behaviour, Exception ex) {
            _behaviour = behaviour;
            _exception = ex;
        }
        #endregion
    }
}