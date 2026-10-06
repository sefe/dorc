using Dorc.Core.HighAvailability;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class NoOpDistributedLockServiceTests
    {
        [TestMethod]
        public void IsEnabled_ShouldBeFalse()
        {
            var service = new NoOpDistributedLockService();

            Assert.IsFalse(service.IsEnabled);
        }

        [TestMethod]
        public async Task TryAcquireLockAsync_ShouldReturnNull()
        {
            var service = new NoOpDistributedLockService();

            var result = await service.TryAcquireLockAsync("test-resource", 5000, CancellationToken.None);

            Assert.IsNull(result);
        }
    }
}
