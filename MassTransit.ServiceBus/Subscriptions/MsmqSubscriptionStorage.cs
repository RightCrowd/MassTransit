using System;
using System.Collections.Generic;
using System.Messaging;
using System.Runtime.Serialization.Formatters.Binary;
using log4net;
using MassTransit.ServiceBus.Subscriptions.Messages;

namespace MassTransit.ServiceBus.Subscriptions
{
    public class MsmqSubscriptionStorage :
        ISubscriptionStorage
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof (MsmqSubscriptionStorage));

        private BinaryFormatter _formatter;
        private Cursor _peekCursor;
        private MessageQueue _storageQueue;
        private IMessageQueueEndpoint _storageEndpoint;
        private readonly ISubscriptionStorage _subscriptionCache;
        private IEndpoint _endpoint;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="storageEndpoint">the name of the queue that stores all of the subscriptions</param>
        /// <param name="endpoint"></param>
        /// <param name="subscriptionCache">in memory cache</param>
        public MsmqSubscriptionStorage(IMessageQueueEndpoint storageEndpoint, IEndpoint endpoint, ISubscriptionStorage subscriptionCache)
        {
			_storageEndpoint = storageEndpoint;
            _endpoint = endpoint;
            _subscriptionCache = subscriptionCache;
			_storageQueue = new MessageQueue(_storageEndpoint.QueueName, QueueAccessMode.SendAndReceive);

            //TODO: should there be a bus instance here so we can subscribe to messages and send messages?

            Initialize();
        }

        private void Initialize()
        {
            MessagePropertyFilter mpf = new MessagePropertyFilter();
            mpf.SetAll();

            _storageQueue.MessageReadPropertyFilter = mpf;

            _formatter = new BinaryFormatter();

            _peekCursor = _storageQueue.CreateCursor();

            _storageQueue.BeginPeek(TimeSpan.FromHours(24), _peekCursor, PeekAction.Current, this, QueuePeekCompleted);
        }


        private void QueuePeekCompleted(IAsyncResult asyncResult)
        {
            //TODO: Shouldn't we check this when we are created?
            if (_storageQueue == null)
                return;

            Message msg = _storageQueue.EndPeek(asyncResult);

            if (_log.IsDebugEnabled)
                _log.DebugFormat("Subscription Update Received: Id {0}", msg.Id);

            IMessage[] messages = _formatter.Deserialize(msg.BodyStream) as IMessage[];
            if (messages != null)
            {
                foreach (SubscriptionChange subscriptionMessage in messages)
                {
                    if (_log.IsDebugEnabled)
                        _log.DebugFormat("Subscription Subscribe: {0} Message Type: {1} Mode: {2}", msg.ResponseQueue.Path, subscriptionMessage.MessageName, subscriptionMessage.ChangeType.ToString());

                    if (subscriptionMessage.ChangeType == SubscriptionChange.SubscriptionChangeType.Add) //would there ever be anything but?
                    {
                        _subscriptionCache.Add(subscriptionMessage.MessageName, subscriptionMessage.Address);
                    }
                }
            }

            _storageQueue.BeginPeek(TimeSpan.FromHours(24), _peekCursor, PeekAction.Next, this, QueuePeekCompleted);
        }

        public IList<Uri> List(string messageName)
        {
            return _subscriptionCache.List(messageName);
        }

        public IList<Uri> List()
        {
            return _subscriptionCache.List();
        }

        public void Add(string messageName, Uri endpoint)
        {
            SubscriptionChange subscriptionChange =
                new SubscriptionChange(messageName, endpoint,
                                        SubscriptionChange.SubscriptionChangeType.Add);

            Send(subscriptionChange);

            _subscriptionCache.Add(messageName, endpoint);

            if (_log.IsDebugEnabled)
                _log.DebugFormat("Adding Subscription to {0} for {1}", messageName, endpoint);
        }

        private void Send(IMessage message)
        {
            Message msg = new Message();

            msg.ResponseQueue = new MessageQueue(_storageEndpoint.QueueName);
            msg.Recoverable = true;

            _formatter.Serialize(msg.BodyStream, new IMessage[] {message});

            _storageQueue.Send(msg);
        }

        public void Remove( string messageName, Uri endpoint)
        {
            _subscriptionCache.Remove(messageName, endpoint);

            SubscriptionChange subscriptionChange =
                new SubscriptionChange(messageName, endpoint,
                                        SubscriptionChange.SubscriptionChangeType.Remove);

            Send(subscriptionChange);

            if (_log.IsDebugEnabled)
                _log.DebugFormat("Removing Subscription to {0} for {1}", messageName, endpoint);
        }

        public void Dispose()
        {
            _storageQueue.Close();
            _storageQueue.Dispose();
            _storageQueue = null;

            _subscriptionCache.Dispose();
        }
    }
}