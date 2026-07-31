using System;
using System.IO;

namespace Domain
{
    // Development implementation of the seam: prints the order instead of sending it anywhere.
    public class ConsoleDispatchSink : IDispatchSink
    {
        public void send(DispatchOrder order)
        {
            Console.WriteLine($"[ROUTED] {order.UnitId} ({order.UnitType}) -> " +
                $"{order.IncidentId} [{order.Priority}] @ {order.Location} {order.SentAt:HH:mm:ss}");
        }
    }
}
