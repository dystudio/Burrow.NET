﻿using System;
using System.Collections.Generic;
using System.Threading;
using NSubstitute;
using NUnit.Framework;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

// ReSharper disable InconsistentNaming
namespace Burrow.Tests.BurrowConsumerTests
{
    [TestFixture]
    public class MethodHandleMessageDelivery
    {
        [Test]
        public void When_called_should_execute_methods_on_message_handler()
        {
            // Arrange
            var waitHandler = new AutoResetEvent(false);
            var model = Substitute.For<IModel>();
            model.IsOpen.Returns(true);
            var msgHandler = Substitute.For<IMessageHandler>();
            //To decrease the messagages in progress so it doesn't have to wait when dispose at the end
            msgHandler.When(x => x.HandleMessage(Arg.Any<BasicDeliverEventArgs>()))
                      .Do(callInfo => msgHandler.HandlingComplete += Raise.Event<MessageHandlingEvent>(callInfo.Arg<BasicDeliverEventArgs>()));

            var watcher = Substitute.For<IRabbitWatcher>();
            watcher.IsDebugEnable.Returns(true);
            msgHandler.When(x => x.HandleMessage(Arg.Any<BasicDeliverEventArgs>()))
                      .Do(callInfo => waitHandler.Set());
            var consumer = new BurrowConsumer(model, msgHandler, watcher, true, 3)
                               {
                                   ConsumerTag = "ConsumerTag"
                               };
            Subscription.OutstandingDeliveryTags[consumer.ConsumerTag] = new List<ulong>();

            // Action
            consumer.Queue.Enqueue(new BasicDeliverEventArgs
            {
                BasicProperties = Substitute.For<IBasicProperties>(),
                ConsumerTag = "ConsumerTag"
            });
            waitHandler.WaitOne();


            // Assert
            msgHandler.DidNotReceive().HandleError(Arg.Any<BasicDeliverEventArgs>(), Arg.Any<Exception>());
            consumer.Dispose();
        }

        [Test]
        public void When_called_should_dispose_if_the_message_handler_throws_exception()
        {
            var waitHandler = new ManualResetEvent(false);
            var watcher = Substitute.For<IRabbitWatcher>();
            var model = Substitute.For<IModel>();
            model.IsOpen.Returns(true);
            var msgHandler = Substitute.For<IMessageHandler>();
            msgHandler.When(x => x.HandleMessage(Arg.Any<BasicDeliverEventArgs>()))
                .Do(callInfo =>
                    {
                        throw new Exception("Bad excepton");
                    }
                );

            watcher.When(x => x.Error(Arg.Any<Exception>())).Do(callInfo => waitHandler.Set());
            var consumer = new BurrowConsumer(model, msgHandler, watcher, true, 3) { ConsumerTag = "ConsumerTag" };
            Subscription.OutstandingDeliveryTags[consumer.ConsumerTag] = new List<ulong>();

            // Action
            consumer.Queue.Enqueue(new BasicDeliverEventArgs
            {
                BasicProperties = Substitute.For<IBasicProperties>(),
                ConsumerTag = "ConsumerTag"
            });
            Assert.IsTrue(waitHandler.WaitOne(1000));

            // Assert
            watcher.Received(1).Error(Arg.Any<Exception>());
            Assert.IsTrue(consumer.Status != ConsumerStatus.Active);
            
            Subscription.OutstandingDeliveryTags[consumer.ConsumerTag].Clear(); // To kill the while loop
        }
    }
}
// ReSharper restore InconsistentNaming