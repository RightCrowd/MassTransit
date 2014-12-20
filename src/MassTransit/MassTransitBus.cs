// Copyright 2007-2014 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Context;
    using Pipeline;
    using Transports;


    public class MassTransitBus :
        IBusControl
    {
        readonly Uri _address;
        readonly IConsumePipe _consumePipe;
        readonly IList<IReceiveEndpoint> _receiveEndpoints;
        readonly List<Task> _runningTasks;
        readonly ISendEndpointProvider _sendEndpointProvider;
        readonly IPublishEndpoint _publishEndpoint;

        public MassTransitBus(Uri address, IConsumePipe consumePipe, ISendEndpointProvider sendEndpointProvider, IPublishEndpoint publishEndpoint,
            IEnumerable<IReceiveEndpoint> receiveEndpoints)
        {
            _address = address;
            _consumePipe = consumePipe;
            _sendEndpointProvider = sendEndpointProvider;
            _publishEndpoint = publishEndpoint;
            _receiveEndpoints = receiveEndpoints.ToList();
            _runningTasks = new List<Task>();
        }

        Task IPublishEndpoint.Publish<T>(T message, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish(object message, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, cancellationToken);
        }

        Task IPublishEndpoint.Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish(object message, Type messageType, CancellationToken cancellationToken)
        {
            return PublishEndpointConverterCache.Publish(this, message, messageType, cancellationToken);
        }

        Task IPublishEndpoint.Publish(object message, Type messageType, IPipe<PublishContext> publishPipe,
            CancellationToken cancellationToken)
        {
            return PublishEndpointConverterCache.Publish(this, message, messageType, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(object values, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish<T>(values, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(values, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish<T>(values, publishPipe, cancellationToken);
        }

        public Uri Address
        {
            get { return _address; }
        }

        IConsumePipe IBus.ConsumePipe
        {
            get { return _consumePipe; }
        }

        Task<ISendEndpoint> ISendEndpointProvider.GetSendEndpoint(Uri address)
        {
            return _sendEndpointProvider.GetSendEndpoint(address);
        }

        public async Task<BusHandle> Start(CancellationToken cancellationToken)
        {
            var receiveEndpointHandles = new List<ReceiveEndpointHandle>();

            Exception exception = null;
            try
            {
                Task<ReceiveEndpointHandle>[] receiveEndpoints = _receiveEndpoints.Select(x => x.Start(cancellationToken)).ToArray();
                foreach (var endpoint in receiveEndpoints)
                {
                    try
                    {
                        ReceiveEndpointHandle handle = await endpoint;

                        receiveEndpointHandles.Add(handle);
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                }
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            if (exception != null)
            {
                await Task.WhenAll(receiveEndpointHandles.Select(x => x.Stop(cancellationToken)));

                throw new MassTransitException("The service bus could not be started.", exception);
            }

            return new Handle(this, receiveEndpointHandles.ToArray(), _sendEndpointProvider);
        }


        class Handle :
            BusHandle
        {
            readonly IBusControl _bus;
            readonly ReceiveEndpointHandle[] _receiveEndpoints;
            readonly ISendEndpointProvider _sendEndpointProvider;

            public Handle(IBusControl bus, ReceiveEndpointHandle[] receiveEndpoints, ISendEndpointProvider sendEndpointProvider)
            {
                _bus = bus;
                _receiveEndpoints = receiveEndpoints;
                _sendEndpointProvider = sendEndpointProvider;
            }

            public void Dispose()
            {
                Stop().Wait();
            }

            public IBus Bus
            {
                get { return _bus; }
            }

            public async Task Stop(CancellationToken cancellationToken = new CancellationToken())
            {
                await Task.WhenAll(_receiveEndpoints.Select(x => x.Stop(cancellationToken)));

//                var disposable = _sendEndpointProvider as IDisposable;
//                if (disposable != null)
//                    disposable.Dispose();
            }
        }
    }
}