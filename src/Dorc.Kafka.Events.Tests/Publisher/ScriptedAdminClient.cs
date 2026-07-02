using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Scripted <see cref="IAdminClient"/> for the topic provisioners'
/// error-policy tests (same pattern as the lock layer's fake). Only
/// CreateTopicsAsync and GetMetadata are scriptable — the only members the
/// provisioners touch; everything else throws.
/// </summary>
internal sealed class ScriptedAdminClient : IAdminClient
{
    public Func<IEnumerable<TopicSpecification>, Task>? OnCreateTopics { get; set; }
    public Func<string, TimeSpan, Metadata>? OnGetMetadata { get; set; }
    public List<TopicSpecification> CreateRequests { get; } = new();

    public Task CreateTopicsAsync(IEnumerable<TopicSpecification> topics, CreateTopicsOptions? options = null)
    {
        CreateRequests.AddRange(topics);
        return OnCreateTopics?.Invoke(CreateRequests) ?? Task.CompletedTask;
    }

    public Metadata GetMetadata(string topic, TimeSpan timeout)
        => OnGetMetadata is not null ? OnGetMetadata(topic, timeout) : throw new NotSupportedException();

    public Metadata GetMetadata(TimeSpan timeout) => throw new NotSupportedException();

    public Handle Handle => throw new NotSupportedException();
    public string Name => "scripted-admin";
    public int AddBrokers(string brokers) => 0;
    public void SetSaslCredentials(string username, string password) { }
    public void Dispose() { }

    public List<GroupInfo> ListGroups(TimeSpan timeout) => throw new NotSupportedException();
    public GroupInfo ListGroup(string group, TimeSpan timeout) => throw new NotSupportedException();
    public Task CreatePartitionsAsync(IEnumerable<PartitionsSpecification> partitionsSpecifications, CreatePartitionsOptions? options = null) => throw new NotSupportedException();
    public Task DeleteTopicsAsync(IEnumerable<string> topics, DeleteTopicsOptions? options = null) => throw new NotSupportedException();
    public Task DeleteGroupsAsync(IList<string> groups, DeleteGroupsOptions? options = null) => throw new NotSupportedException();
    public Task<List<DeleteRecordsResult>> DeleteRecordsAsync(IEnumerable<TopicPartitionOffset> topicPartitionOffsets, DeleteRecordsOptions? options = null) => throw new NotSupportedException();
    public Task AlterConfigsAsync(Dictionary<ConfigResource, List<ConfigEntry>> configs, AlterConfigsOptions? options = null) => throw new NotSupportedException();
    public Task<List<IncrementalAlterConfigsResult>> IncrementalAlterConfigsAsync(Dictionary<ConfigResource, List<ConfigEntry>> configs, IncrementalAlterConfigsOptions? options = null) => throw new NotSupportedException();
    public Task<List<DescribeConfigsResult>> DescribeConfigsAsync(IEnumerable<ConfigResource> resources, DescribeConfigsOptions? options = null) => throw new NotSupportedException();
    public Task CreateAclsAsync(IEnumerable<AclBinding> aclBindings, CreateAclsOptions? options = null) => throw new NotSupportedException();
    public Task<DescribeAclsResult> DescribeAclsAsync(AclBindingFilter aclBindingFilter, DescribeAclsOptions? options = null) => throw new NotSupportedException();
    public Task<List<DeleteAclsResult>> DeleteAclsAsync(IEnumerable<AclBindingFilter> aclBindingFilters, DeleteAclsOptions? options = null) => throw new NotSupportedException();
    public Task<DeleteConsumerGroupOffsetsResult> DeleteConsumerGroupOffsetsAsync(string group, IEnumerable<TopicPartition> partitions, DeleteConsumerGroupOffsetsOptions? options = null) => throw new NotSupportedException();
    public Task<List<AlterConsumerGroupOffsetsResult>> AlterConsumerGroupOffsetsAsync(IEnumerable<ConsumerGroupTopicPartitionOffsets> groupPartitions, AlterConsumerGroupOffsetsOptions? options = null) => throw new NotSupportedException();
    public Task<List<ListConsumerGroupOffsetsResult>> ListConsumerGroupOffsetsAsync(IEnumerable<ConsumerGroupTopicPartitions> groupPartitions, ListConsumerGroupOffsetsOptions? options = null) => throw new NotSupportedException();
    public Task<ListConsumerGroupsResult> ListConsumerGroupsAsync(ListConsumerGroupsOptions? options = null) => throw new NotSupportedException();
    public Task<DescribeConsumerGroupsResult> DescribeConsumerGroupsAsync(IEnumerable<string> groups, DescribeConsumerGroupsOptions? options = null) => throw new NotSupportedException();
    public Task<DescribeUserScramCredentialsResult> DescribeUserScramCredentialsAsync(IEnumerable<string> users, DescribeUserScramCredentialsOptions? options = null) => throw new NotSupportedException();
    public Task AlterUserScramCredentialsAsync(IEnumerable<UserScramCredentialAlteration> alterations, AlterUserScramCredentialsOptions? options = null) => throw new NotSupportedException();
}
