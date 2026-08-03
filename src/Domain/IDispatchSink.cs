using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    // The seam where a dispatch leaves the system. Console today; a queue, a radio gateway or
    // a service call tomorrow, with no change to DispatchService.
    public interface IDispatchSink
    {
        void send(DispatchOrder Order);
    }
}
