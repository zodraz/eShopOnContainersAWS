using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MassTransit;

namespace EventBus.Tests
{
    public class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public bool Handled { get; private set; }

        public TestIntegrationEventHandler()
        {
            Handled = false;
        }

        public async Task Handle(TestIntegrationEvent @event)
        {
            Handled = true;
        }

        public Task Consume(ConsumeContext<TestIntegrationEvent> context)
        {
            throw new NotImplementedException();
        }
    }
}
