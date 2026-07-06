namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class DeploymentEngineTransientTests
    {
        // A stand-in for EF Core's RetryLimitExceededException, matched by type
        // name. This is the exception that previously slipped past the bare
        // `catch (SqlException)` and stopped the whole Monitor service (finding C-5).
        private sealed class RetryLimitExceededException : Exception
        {
            public RetryLimitExceededException(Exception inner) : base("retries exhausted", inner) { }
        }

        [TestMethod]
        public void IsTransientException_TimeoutException_IsTransient()
        {
            Assert.IsTrue(DeploymentEngine.IsTransientException(new TimeoutException()));
        }

        [TestMethod]
        public void IsTransientException_TimeoutWrappedInGenericException_IsTransient()
        {
            var wrapped = new InvalidOperationException("outer", new TimeoutException());
            Assert.IsTrue(DeploymentEngine.IsTransientException(wrapped));
        }

        [TestMethod]
        public void IsTransientException_RetryLimitExceededByName_IsTransient()
        {
            // Mirrors the real failure: EF's RetryLimitExceededException wrapping a
            // data-access error after retries are exhausted.
            var ex = new RetryLimitExceededException(new Exception("db unreachable"));
            Assert.IsTrue(DeploymentEngine.IsTransientException(ex));
        }

        [TestMethod]
        public void IsTransientException_PlainUnexpectedException_IsNotTransient()
        {
            Assert.IsFalse(DeploymentEngine.IsTransientException(new NullReferenceException()));
            Assert.IsFalse(DeploymentEngine.IsTransientException(new InvalidOperationException("bug")));
            Assert.IsFalse(DeploymentEngine.IsTransientException(null));
        }
    }
}
