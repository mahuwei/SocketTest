using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SocketHelper {
  /// <summary>
  ///   Socket Server
  /// </summary>
  public class SocketServer {
    private static SocketServer _tcpServer;
    private static readonly object LockObj = new object();

    private static readonly ManualResetEvent AllDone = new ManualResetEvent(false);
    private readonly List<SocketAsyncResult> _clients;
    private readonly bool _isIpOnly;
    private bool _isStop;
    private Socket _listener;

    /// <summary>
    ///   单例；私有构造函数
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    /// <param name="isIpOnly">是否Ip地址唯一(false；则ip和端口一样才判定未相同）</param>
    private SocketServer(string ip, int port, bool isIpOnly) {
      _isIpOnly = isIpOnly;
      _isStop = false;
      _clients = new List<SocketAsyncResult>();
      var ipAddress = IPAddress.Parse(ip);
      var localEndPoint = new IPEndPoint(ipAddress, port);
      _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

      try {
        _listener.Bind(localEndPoint);
        _listener.Listen(100);
        var accTh = new Thread(new ThreadStart(delegate {
          while (true) {
            if (_isStop) {
              break;
            }

            try {
              AllDone.Reset();

              // Start an asynchronous socket to listen for connections.  
              Console.WriteLine("Waiting for a connection...");
              _listener.BeginAccept(AcceptCallback, _listener);

              // Wait until a connection is made before continuing.  
              AllDone.WaitOne();
            }
            catch (Exception e) {
              Console.WriteLine("等待客户端连接:{0}", e);
            }
          }
        }));
        accTh.Start();
      }
      catch (SocketException skew) {
        Console.WriteLine(skew.ToString());
        throw;
      }
    }

    /// <summary>
    ///   发布数据
    /// </summary>
    public event PublishData OnPublishData;

    /// <summary>
    ///   异步操作一个传入的连接尝试
    /// </summary>
    /// <param name="ar"></param>
    private void AcceptCallback(IAsyncResult ar) {
      // Signal the main thread to continue.  
      AllDone.Set();

      // Get the socket that handles the client request.  
      var listener = (Socket)ar.AsyncState;
      if (listener == null) {
        return;
      }

      try {
        var handler = listener.EndAccept(ar);

        // Create the state object.  
        var state = new SocketAsyncResult(true) { WorkSocket = handler };
        if (handler.RemoteEndPoint is IPEndPoint ipEndPoint) {
          state.Ip = ipEndPoint.Address.ToString();
          state.Port = ipEndPoint.Port;
        }

        AddClient(state);
        handler.BeginReceive(state.Buffer, 0, SocketAsyncResult.BufferSize, 0, ReadCallback, state);
      }
      catch (Exception e) {
        Console.WriteLine(e);
      }
    }

    /// <summary>
    ///   异步读取客户发来的数据
    /// </summary>
    /// <param name="ar"></param>
    private void ReadCallback(IAsyncResult ar) {
      var state = (SocketAsyncResult)ar.AsyncState;
      if (state == null) {
        return;
      }

      var handler = state.WorkSocket;
      try {
        var bytesRead = handler.EndReceive(ar);

        if (bytesRead <= 0) {
          handler.BeginReceive(state.Buffer, 0, SocketAsyncResult.BufferSize, 0, ReadCallback, state);
          return;
        }

        var tmp = new byte[bytesRead];
        Array.ConstrainedCopy(state.Buffer, 0, tmp, 0, bytesRead);
        var hasCompleted = state.AddData(tmp, out var messages);
        if (hasCompleted) {
          foreach (var message in messages) {
            if (message.Equals(SocketAsyncResult.SocketCloseString, StringComparison.CurrentCultureIgnoreCase)) {
              RemoveClient(state);
              return;
            }

            OnPublishData?.Invoke(message);
          }
        }

        handler.BeginReceive(state.Buffer, 0, SocketAsyncResult.BufferSize, 0, ReadCallback, state);
      }
      catch (SocketException e) {
        Console.WriteLine("接收数据出错(SocketException):\n{0}", e);
        RemoveClient(state);
      }
      catch (ObjectDisposedException) {
        Console.WriteLine($"接收数据出错,Socket({state.Ip}:{state.Port}) ：ObjectDisposedException");
      }
      catch (Exception e) {
        Console.WriteLine("接收数据出错(Exception):\n{0}", e);
        RemoveClient(state);
      }
    }

    private void RemoveClient(SocketAsyncResult state) {
      var ret = _clients.Remove(state);
      if (ret == false) {
        return;
      }

      Console.WriteLine($"移除客户端连接 {state.Ip}:{state.Port}");
      state.WorkSocket.Shutdown(SocketShutdown.Both);
      state.WorkSocket.Close();
      state.WorkSocket.Dispose();
    }

    private void AddClient(SocketAsyncResult state) {
      //虽然有信号量,还是用lock增加系数
      lock (LockObj) {
        var oldStateObject = _isIpOnly
          ? _clients.Find(o => o.Ip == state.Ip)
          : _clients.Find(o => o.Ip == state.Ip && o.Port == state.Port);
        //如果不存在则添加,否则更新
        if (oldStateObject == null) {
          _clients.Add(state);
          Console.WriteLine($"{state.Ip}:{state.Port}连接成功。");
        }
        else {
          RemoveClient(oldStateObject);
          _clients.Add(state);
          Console.WriteLine($"{state.Ip}:{state.Port}重新连接成功。");
        }
      }
    }

    /// <summary>
    ///   向所有客户端发送数据。
    /// </summary>
    /// <param name="data">需要发送的数据</param>
    public void SendToAll(string data) {
      if (_clients == null || _clients.Any() == false) {
        Console.WriteLine("没有连接的客户端。");
        return;
      }

      try {
        Parallel.ForEach(_clients, new ParallelOptions { MaxDegreeOfParallelism = 5 }, item => {
          if (item != null) {
            SendToClient(item, data);
          }
        });
      }
      catch (Exception ex) {
        Console.Write($"SendToAll error:\n{ex}");
      }
    }

    /// <summary>
    ///   向某个客户端发送数据
    /// </summary>
    /// <param name="client">客户端</param>
    /// <param name="data">需要发送的数据</param>
    private void SendToClient(SocketAsyncResult client, string data) {
      if (_clients == null) {
        return;
      }

      try {
        var sendData = SocketAsyncResult.CreateSendData(data);
        client.WorkSocket.Send(sendData);
      }
      catch (Exception e) {
        Console.WriteLine($"发送数据出错:\n{e}");
        RemoveClient(client);
      }
    }

    /// <summary>
    ///   获取Socket Server实例
    ///   要求必须先调用带Ip和端口的方法。
    /// </summary>
    /// <returns>
    ///   <see cref="SocketServer" />
    /// </returns>
    public static SocketServer GetInstance() {
      if (_tcpServer == null) {
        throw new Exception("还未创建Socket Server.");
      }

      return _tcpServer;
    }

    /// <summary>
    ///   获取Socket Server实例
    /// </summary>
    /// <param name="ip">需要监听的ip地址</param>
    /// <param name="port">需要监听的端口号</param>
    /// <param name="isIpOnly">是否Ip地址唯一(false；则ip和端口一样才判定未相同）</param>
    /// <returns>
    ///   <see cref="SocketServer" />
    /// </returns>
    public static SocketServer GetInstance(string ip, int port, bool isIpOnly = true) {
      lock (LockObj) {
        _tcpServer ??= new SocketServer(ip, port, isIpOnly);
      }

      return _tcpServer;
    }

    /// <summary>
    ///   停止监听
    /// </summary>
    public void Stop() {
      _isStop = true;
      if (_listener == null) {
        return;
      }

      _listener.Close();
      _listener.Dispose();
      _listener = null;
      OnPublishData = null;
      _tcpServer = null;
    }
  }
}