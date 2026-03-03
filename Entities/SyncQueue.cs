using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U9SyncService.Entities
{
    public class SyncQueue
    {
        public long Id { get; set; }
        public string OptType { get; set; }
        public string SourceKey { get; set; }
        public int State { get; set; }
        public string Payload { get; set; }
        public int RetryCount { get; set; }
        public string? ErrorMsg { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public string? CbCode { get; set; }
        public int EditFlag { get; set; }
    }
}
