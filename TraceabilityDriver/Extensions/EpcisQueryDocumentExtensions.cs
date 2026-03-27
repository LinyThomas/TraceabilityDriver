using OpenTraceability.Models.Events;

namespace TraceabilityDriver.Extensions
{
    public static class EpcisQueryDocumentExtensions
    {
        public static string ToJson(this EPCISQueryDocument doc)
        {
            string json = OpenTraceability.Mappers.OpenTraceabilityMappers.EPCISQueryDocument.JSON.Map(doc);
            return json;
        }

        public static EPCISQueryDocument FromJson(this EPCISQueryDocument doc, string json)
        {
            doc = OpenTraceability.Mappers.OpenTraceabilityMappers.EPCISQueryDocument.JSON.Map(json);
            return doc;
        }
    }
}
