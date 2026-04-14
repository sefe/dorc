namespace Dorc.Kafka.Events;

public static class KafkaSubjectNames
{
    public const string RequestsNewTopic = "dorc.requests.new";
    public const string RequestsStatusTopic = "dorc.requests.status";
    public const string ResultsStatusTopic = "dorc.results.status";

    public const string RequestsNewValue = RequestsNewTopic + "-value";
    public const string RequestsStatusValue = RequestsStatusTopic + "-value";
    public const string ResultsStatusValue = ResultsStatusTopic + "-value";
}
