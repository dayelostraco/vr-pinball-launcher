using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace VRLauncher
{
    /// <summary>
    /// Reads and writes explicit data contracts without depending on the Unity
    /// engine. Contract member names preserve existing state and sidecar formats.
    /// Invalid field types raise SerializationException for the caller to handle.
    /// </summary>
    public static class JsonData
    {
        public static T Read<T>(string json)
        {
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            // Mono wraps field-conversion errors in reflection exceptions; .NET
            // reports SerializationException directly. Present one contract to callers.
            catch (System.Exception ex) when (ex is SerializationException || ex is TargetInvocationException)
            {
                throw new SerializationException("JSON field could not be converted to its declared type.", ex);
            }
        }

        public static string Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
