using System;
using System.Linq;
using System.Net;

namespace SocketClient {
  internal class Program {
    private static void Main(string[] args) {
      Console.Title = $"Socket Client:{(args != null && args.Any() ? args[0] : "")}";
      var tcpClient = CreateClient();

      var isStop = false;
      do {
        var input = Console.ReadLine();
        if (string.IsNullOrEmpty(input)) {
          continue;
        }

        var commands = input.Split(' ');
        switch (commands[0]) {
          case "send":
            SendData(tcpClient, commands[1]);
            break;
          case "quit":
            isStop = true;
            break;
          case "stop":
            tcpClient.Stop();
            break;
          case "restart":
            tcpClient = CreateClient();
            break;
        }
      } while (!isStop);

      tcpClient.Stop();

      Console.WriteLine("\nPress ENTER to continue...");
      Console.Read();
    }

    private static SocketHelper.SocketClient CreateClient() {
      //AsynchronousClient.Start();
      var ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
      var ipAddress = ipHostInfo.AddressList[0];
      var tcpClient = SocketHelper.SocketClient.GetInstance("192.168.1.200", 11000);

      tcpClient.OnPublishData += message => {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} 接收数据: {message}");
      };

      tcpClient.OnSocketStatusChange += (connected, message) => {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} 状态变更，连接:{connected};原因 {message}");
      };
      return tcpClient;
    }

    private static void SendData(SocketHelper.SocketClient tcpClient, string cmd) {
      tcpClient.Send(cmd);
    }
  }
}