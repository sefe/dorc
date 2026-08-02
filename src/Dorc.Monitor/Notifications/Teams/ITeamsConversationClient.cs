namespace Dorc.Monitor.Notifications.Teams
{
    internal interface ITeamsConversationClient
    {
        Task<string> CreateConversationAsync(string aadObjectId);

        Task SendCardAsync(string conversationId, string cardJson);
    }
}
