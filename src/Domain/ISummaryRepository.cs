using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    public interface ISummaryRepository
    {
        Task SaveSummaryAsync(SummaryRecord record);
    }
}
