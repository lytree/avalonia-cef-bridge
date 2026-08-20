using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xilium.CefGlue.Common.Shared.RendererProcessCommunication
{
    public sealed class SerializableException
    {
        public string ExceptionType { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }

        public string SerializeToString() =>
            JsonSerializer.Serialize(this, CefGlueSharedJsonContext.Default.SerializableException);

        public static SerializableException DeserializeFromString(string content) =>
            JsonSerializer.Deserialize(content, CefGlueSharedJsonContext.Default.SerializableException);
    }

    [JsonSerializable(typeof(SerializableException))]
    internal partial class CefGlueSharedJsonContext : JsonSerializerContext
    {
    }
}
