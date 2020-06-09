using System;
using System.Net;
using SocketHelper;

namespace SocketServer {
  internal class Program {
    private static void Main(string[] args) {
      Console.Title = "Socket Server";
      //AsynchronousSocketListener.Start();
      var ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
      var ipAddress = ipHostInfo.AddressList[0];

      var tcpServer = SocketHelper.SocketServer.GetInstance("192.168.1.129", 11000);
      tcpServer.OnPublishData += message => {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} 接收数据: {message}");
      };

      var isStop = false;
      do {
        var input = Console.ReadLine();
        if (string.IsNullOrEmpty(input)) {
          continue;
        }

        var commands = input.Split(' ');
        switch (commands[0]) {
          case "send":
            SendData(tcpServer, commands[1]);
            break;
          case "quit":
            isStop = true;
            break;
          case "restart":
            break;
        }
      } while (!isStop);

      tcpServer.Stop();

      Console.WriteLine("\nPress ENTER to continue...");
      Console.Read();
    }

    private static void SendData(SocketHelper.SocketServer tcpServer, string command) {
      try {
        tcpServer.SendToAll(command);
      }
      catch (Exception e) {
        Console.WriteLine(e);
      }
    }
  }
}