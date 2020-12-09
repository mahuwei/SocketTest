using System;
using System.Threading;

namespace ManualResetEventSample {
  internal class Program {
    // mre is used to block and release threads manually. It is
    // created in the unsignaled state.
    private static readonly ManualResetEvent Mre = new ManualResetEvent(false);

    private static void Main() {
      Console.WriteLine("\nStart 3 named threads that block on a ManualResetEvent:\n");

      for (var i = 0; i <= 2; i++) {
        var t = new Thread(ThreadProc) { Name = "Thread_" + i };
        t.Start();
      }

      Thread.Sleep(500);
      Console.WriteLine("\nWhen all three threads have started, press Enter to call Set()"
                        + "\nto release all the threads.\n");
      Console.ReadLine();

      Mre.Set();

      Thread.Sleep(500);
      Console.WriteLine("\nWhen a ManualResetEvent is signaled, threads that call WaitOne()"
                        + "\ndo not block. Press Enter to show this.\n");
      Console.ReadLine();

      for (var i = 3; i <= 4; i++) {
        var t = new Thread(ThreadProc) { Name = "Thread_" + i };
        t.Start();
      }

      Thread.Sleep(500);
      Console.WriteLine("\nPress Enter to call Reset(), so that threads once again block"
                        + "\nwhen they call WaitOne().\n");
      Console.ReadLine();

      Mre.Reset();

      // Start a thread that waits on the ManualResetEvent.
      var t5 = new Thread(ThreadProc) { Name = "Thread_5" };
      t5.Start();

      Thread.Sleep(500);
      Console.WriteLine("\nPress Enter to call Set() and conclude the demo.");
      Console.ReadLine();

      Mre.Set();

      // If you run this example in Visual Studio, uncomment the following line:
      Console.ReadLine();
    }

    private static void ThreadProc() {
      var name = Thread.CurrentThread.Name;

      Console.WriteLine(name + " starts and calls mre.WaitOne()");

      Mre.WaitOne();

      Console.WriteLine(name + " ends.");
    }
  }
}