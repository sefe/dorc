using System;
using System.Threading;
using System.Threading.Tasks;

class Program {
    static async Task Main() {
        var cts = new CancellationTokenSource();
        var t = Task.Run(async () => {
            await Task.Delay(100);
            cts.Dispose(); // Dispose before task completes
            return 42;
        });
        
        var result = await t.WaitAsync(cts.Token);
        Console.WriteLine("Done! " + result);
    }
}
