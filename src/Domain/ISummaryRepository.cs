using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    // Write-only by design: briefings are an audit trail of what the shift was told, not
    // something the app reads back.
    public interface ISummaryRepository
    {
        Task SaveSummaryAsync(SummaryRecord record);
    }
}
