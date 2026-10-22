using Microsoft.Playwright;

namespace Tests.Acceptance.WebUI.Pages.Components
{
    /// <summary>
    /// Shared side-bar menu (present on every DOrc page), so navigation is not duplicated per page.
    /// </summary>
    public class SideNavigation
    {
        private readonly IPage _page;

        private ILocator MenuLink(string name) =>
            _page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });

        // Audit sub-menu items carry a title attribute, e.g. title="Variables Audit".
        private ILocator AuditMenu => MenuLink("Audit");
        private ILocator AuditSubMenuLink(string title) => _page.GetByTitle(title, new() { Exact = true });

        public SideNavigation(IPage page)
        {
            _page = page;
        }

        public async Task OpenMenuAsync(string menuName)
        {
            await MenuLink(menuName).ClickAsync();
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        /// <summary>
        /// Audit -> sub page, e.g. OpenAuditPageAsync("Variables Audit").
        /// Expands the Audit menu only when the sub-menu item is not already visible.
        /// </summary>
        public async Task OpenAuditPageAsync(string auditPageTitle)
        {
            var subMenu = AuditSubMenuLink(auditPageTitle);
            if (!await subMenu.IsVisibleAsync())
                await AuditMenu.ClickAsync();

            await subMenu.ClickAsync();
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
    }
}
