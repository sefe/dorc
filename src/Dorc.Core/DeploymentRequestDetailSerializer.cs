using System.Xml.Serialization;

namespace Dorc.Core
{
    public class DeploymentRequestDetailSerializer
    {
        private readonly XmlSerializer _serializer;

        public DeploymentRequestDetailSerializer()
        {
            _serializer = new XmlSerializer(typeof(DeploymentRequestDetail));
        }

        public string Serialize(DeploymentRequestDetail requestDetail)
        {
            using (var writer = new StringWriter())
            {
                _serializer.Serialize(writer, requestDetail);

                return writer.ToString();
            }
        }

        public DeploymentRequestDetail? Deserialize(string xml)
        {
            using (var reader = new StringReader(xml))
            {
                return _serializer.Deserialize(reader) as DeploymentRequestDetail;
            }
        }
    }
}