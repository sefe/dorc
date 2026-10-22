using Microsoft.Playwright;

namespace Tests.Acceptance.WebUI.Pages.Components
{
    /// <summary>
    /// Shared toast notifications (vaadin-notification) shown after save/delete actions.
    /// </summary>
    public class AppNotification
    {
        private readonly IPage _page;

        private ILocator Notification(string text) =>
            _page.Locator("vaadin-notification-card").Filter(new() { HasText = text });

        public AppNotification(IPage page)
        {
            _page = page;
        }

        /// <summary>
        /// Returns true when a notification containing the text appears within the default timeout.
        /// </summary>
        public async Task<bool> IsShownAsync(string text)
        {
            try
            {
                await Notification(text).First.WaitForAsync();
                return true;
            }
            catch (System.TimeoutException)
            {
                return false;
            }
        }
    }
}
