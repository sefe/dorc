using FluentAssertions;
using Reqnroll;
using Tests.Acceptance.WebUI.Pages;

namespace Tests.Acceptance.WebUI.StepDefinitions
{
    [Binding]
    public class ProjectSearchSteps
    {
        private readonly HomePage _homePage;
        private readonly EnvironmentsPage _environmentsPage;

        /// <summary>
        /// Page Objects are injected by Reqnroll via ScenarioContext.ScenarioContainer.
        /// They are created once per scenario in BrowserHooks and reused across all step methods.
        /// This follows the Coral.Testing pattern for reusable, maintainable test logic.
        /// </summary>
        public ProjectSearchSteps(HomePage homePage, EnvironmentsPage environmentsPage)
        {
            _homePage = homePage;
            _environmentsPage = environmentsPage;
        }

        [Given("Launch the application")]
        public async Task GivenLaunchTheApplication()
        {
            await _homePage.LaunchApplication();
        }

        [Given("User able to view the Dorc application homepage")]
        public async Task GivenUserAbleToViewTheDorc()
        {
            var isLoaded = await _homePage.IsHomePageLoadedAsync();

            isLoaded.Should().BeTrue(
                "the DOrc header badge should be visible after launching {0}", _homePage.CurrentUrl);
        }

        [When("Click the Menu item Environment in the side bar")]
        public async Task WhenClickEnvironmentsMenu()
        {
            await _homePage.ClickEnvironmentsMenu();
        }

        [When("Search the Project \"(.*)\" in the search bar")]
        public async Task WhenSearchTheProject(string projectName)
        {
            await _environmentsPage.SearchProject(projectName);
        }

        [Then("\"(.*)\" project is displayed in the screen")]
        public async Task ThenProjectIsDisplayed(string projectName)
        {
            _homePage.CurrentUrl.Should().EndWith("/environments",
                "clicking the Environments menu should open the Environments page");

            var isDisplayed = await _environmentsPage.IsProjectDisplayedAsync(projectName);

            isDisplayed.Should().BeTrue(
                "searching for '{0}' should show it in the Environments grid", projectName);
        }
    }
}
