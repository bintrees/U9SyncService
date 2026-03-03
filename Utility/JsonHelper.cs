using System.Text.Encodings.Web;
using System.Text.Json;

namespace U9SyncService.Utility
{
    public static class JsonHelper
    {
        public static readonly JsonSerializerOptions JsOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, 
            WriteIndented = true,
            PropertyNameCaseInsensitive = true, // 忽略大小写
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // 可选 可能导致冲突
        };

        public static string Serialize<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, JsOptions);
        }

        public static T? Deserialize<T>(string json)
        {
             return JsonSerializer.Deserialize<T>(json, JsOptions);
                       
        }
    }
}
