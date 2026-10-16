namespace Dorc.Monitor.Notifications.Teams
{
    internal interface ITeamsConversationClient
    {
        Task<string> CreateConversationAsync(string aadObjectId, CancellationToken cancellationToken);

        Task SendCardAsync(string conversationId, string cardJson, CancellationToken cancellationToken);
    }
}
