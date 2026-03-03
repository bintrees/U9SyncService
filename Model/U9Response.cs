using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace U9SyncService.Model
{
    public class U9Response
    {
        [JsonPropertyName("DocType")]
        public string DocType { get; set; }
        [JsonPropertyName("IsSuccess")]
        public bool IsSuccess { get; set; }
        [JsonPropertyName("Msg")]
        public string Msg { get; set; }
        [JsonPropertyName("DocNo")]
        public string DocNo { get; set; }
        [JsonPropertyName("ID")]
        public long ID { get; set; }
      

    }
}
