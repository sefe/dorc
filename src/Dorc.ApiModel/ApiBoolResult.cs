using System.Runtime.Serialization;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Returns operation status for API(serialization only)
    /// </summary>
    public class ApiBoolResult : ISerializable
    {
        public bool Result { set; get; }
        public string Message { set; get; }
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("Message", Message);
            info.AddValue("Result", Result);
        }
    }
}