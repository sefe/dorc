using Dorc.Kafka.Events.Configuration;

namespace Dorc.Kafka.Events.Tests.Configuration;

[TestClass]
public class KafkaTopicsOptionsValidatorTests
{
    private static KafkaTopicsOptions Valid() => new();

    [TestMethod]
    public void Validate_Defaults_Succeed()
    {
        var r = new KafkaTopicsOptionsValidator().Validate(null, Valid());
        Assert.IsTrue(r.Succeeded, string.Join("; ", r.Failures ?? Array.Empty<string>()));
    }

    [TestMethod]
    public void Validate_SefeShapedNames_Succeed()
    {
        var opts = new KafkaTopicsOptions
        {
            Locks = "tr.dv.gbl.deploy.locks.il2.dorc",
            RequestsNew = "tr.dv.gbl.deploy.request.il2.dorc",
            RequestsStatus = "tr.dv.gbl.deploy.requeststatus.il2.dorc",
            ResultsStatus = "tr.dv.gbl.deploy.resultstatus.il2.dorc"
        };
        var r = new KafkaTopicsOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Succeeded, string.Join("; ", r.Failures ?? Array.Empty<string>()));
    }

    [TestMethod]
    public void Validate_LocksEmpty_Fails()
    {
        var opts = Valid();
        opts.Locks = "";
        var r = new KafkaTopicsOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "Kafka:Topics:Locks");
    }

    [TestMethod]
    public void Validate_RequestsNewWhitespace_Fails()
    {
        var opts = Valid();
        opts.RequestsNew = "   ";
        var r = new KafkaTopicsOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "Kafka:Topics:RequestsNew");
    }

    [TestMethod]
    public void Validate_RequestsStatusEmpty_Fails()
    {
        var opts = Valid();
        opts.RequestsStatus = "";
        var r = new KafkaTopicsOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "Kafka:Topics:RequestsStatus");
    }

    [TestMethod]
    public void Validate_ResultsStatusEmpty_Fails()
    {
        var opts = Valid();
        opts.ResultsStatus = "";
        var r = new KafkaTopicsOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "Kafka:Topics:ResultsStatus");
    }
}
