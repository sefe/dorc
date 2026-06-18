using System;
using System.Threading.Tasks;
using Confluent.Kafka;

class Program {
    static async Task Main() {
        var cfg = new ProducerConfig { BootstrapServers = "localhost:9999", MessageTimeoutMs = 50 };
        var producer = new ProducerBuilder<string, string>(cfg).Build();
        producer.Dispose();
        Console.WriteLine("Disposed");
        try {
            var t = producer.ProduceAsync("test-topic", new Message<string, string> { Key = "k", Value = "v" });
            var r = await t;
            Console.WriteLine("ProduceAsync ok: " + r.Offset);
        } catch (Exception ex) { Console.WriteLine("ProduceAsync threw: " + ex.GetType().FullName + ": " + ex.Message); }
    }
}
