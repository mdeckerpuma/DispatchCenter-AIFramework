using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    public interface IDispatchSink
    {
        void send(DispatchOrder Order);
    }
}
