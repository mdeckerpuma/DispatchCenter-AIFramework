using System;
using System.IO;

namespace Domain
{
    public class ConsoleDispatchSink : IDispatchSink
    {
        public void send(DispatchOrder order)
        {
            Console.WriteLine($"[ROUTED] {order.UnitId} ({order.UnitType}) -> " +
                $"{order.IncidentId} [{order.Priority}] @ {order.Location} {order.SentAt:HH:mm:ss}");
        }
    }
}
