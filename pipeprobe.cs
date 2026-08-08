using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
class P {
  static async Task Main() {
    try {
      using var c = new NamedPipeClientStream(".", "ONEVO.Agent.Pipe.v1", PipeDirection.InOut, PipeOptions.Asynchronous);
      Console.WriteLine("Connecting...");
      await c.ConnectAsync(3000);
      Console.WriteLine("Connected! IsConnected=" + c.IsConnected);
      using var r = new StreamReader(c, leaveOpen: true);
      using var w = new StreamWriter(c, leaveOpen: true) { AutoFlush = true };
      var line = await r.ReadLineAsync();
      Console.WriteLine("First line: " + line);
    } catch (Exception ex) {
      Console.WriteLine("FAIL: " + ex.GetType().Name + " " + ex.Message);
    }
  }
}
