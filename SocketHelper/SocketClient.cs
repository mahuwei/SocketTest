using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SocketHelper {
  /// <summary>
  ///   这个委托实现连接通知
  /// </summary>
  /// <param name="isConnected">已连接：true</param>
  /// <param name="message">连接断开时返回断开原因</param>
  public delegate void SocketPublishStatusEventHandler(bool isConnected, string message);

  public class SocketClient : IDisposable {
    private const int CsTestConnectSecond = 10;
    private static SocketClient _socketPublish;

    private static readonly ManualResetEvent TimeoutObject = new ManualResetEvent(false);
    private static readonly object LockObjIsConnectSuccess = new object();
    private readonly string _ip;
    private readonly int _port;
    private bool _isStop;

    private string _sockErrorStr;
    private SocketAsyncResult _stateObjectCurrent;
    private int _testConnectSecond;

    private SocketClient(string ip, int port) {
      _ip = ip;
      _port = port;
      _isStop = false;
      var clientThread = new Thread(BeginSocket) { Name = "SocketPublishMonitor" };
      clientThread.Start();
    }

    private bool IsConnectSuccess { get; set; }

    public void Dispose() {
      Stop();
    }

    public event PublishData OnPublishData;

    public event SocketPublishStatusEventHandler OnSocketStatusChange;

    /// <summary>
    ///   初始化扫码支付（顾客主扫）
    /// </summary>
    /// <param name="ip">Ip</param>
    /// <param name="port"></param>
    /// <returns></returns>
    public static SocketClient GetInstance(string ip, int port) {
      if (_socketPublish != null && !_socketPublish._isStop) {
        return _socketPublish;
      }

      _socketPublish = new SocketClient(ip, port);
      return _socketPublish;
    }

    public static SocketClient GetInstance() {
      if (_socketPublish != null) {
        return _socketPublish;
      }

      throw new Exception("SocketPublish未初始化，需要先调用： SocketPublish GetInstance(string ip, int port)");
    }

    public void Stop() {
      _isStop = true;
      if (_stateObjectCurrent?.WorkSocket == null || !_stateObjectCurrent.WorkSocket.Connected) {
        return;
      }

      Send(SocketAsyncResult.SocketCloseString);
      OnPublishData = null;
      _stateObjectCurrent.WorkSocket.Disconnect(true);
      _stateObjectCurrent = null;
    }

    public void Send(string message) {
      if (string.IsNullOrEmpty(message)) {
        throw new Exception("不能发送null数据。");
      }

      var sendData = SocketAsyncResult.CreateSendData(message);
      _stateObjectCurrent.WorkSocket.Send(sendData);
      _testConnectSecond = 0;
    }

    private void BeginSocket() {
      do {
        _testConnectSecond++;
        if (_testConnectSecond >= CsTestConnectSecond) {
          if (_stateObjectCurrent?.WorkSocket == null || IsSocketConnected() == false) {
            StartSocket();
          }

          _testConnectSecond = 0;
        }

        Thread.Sleep(1000);
      } while (!_isStop);
    }

    private void StartSocket() {
      var ipEnd = new IPEndPoint(IPAddress.Parse(_ip), _port);
      var socketClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

      _stateObjectCurrent = new SocketAsyncResult { WorkSocket = socketClient };

      #region 异步连接代码

      _sockErrorStr = null;
      //复位timeout事件
      TimeoutObject.Reset();
      try {
        socketClient.BeginConnect(ipEnd, OnConnectedCallback, _stateObjectCurrent);
      }
      catch (Exception err) {
        _sockErrorStr = err.ToString();
        return;
      }

      //直到timeout，或者TimeoutObject.set()
      if (TimeoutObject.WaitOne(CsTestConnectSecond * 1000, false)) {
        return;
      }

      _sockErrorStr = "Time Out";

      #endregion
    }

    /// <summary>
    ///   异步连接回调函数
    /// </summary>
    /// <param name="iar"></param>
    private void OnConnectedCallback(IAsyncResult iar) {
      lock (LockObjIsConnectSuccess) {
        _sockErrorStr = null;
        var isConnectSuccess = false;
        var message = "";
        var client = (SocketAsyncResult)iar.AsyncState;
        if (client == null) {
          throw new Exception("OnConnectedCallback clent is null.");
        }

        try {
          client.WorkSocket.EndConnect(iar);
          isConnectSuccess = true;
          message = "建立连接成功。";
          //开始接收数据
          StartReceive();
        }
        catch (Exception e) {
          message = e.GetBaseException().Message;
          isConnectSuccess = false;
        }
        finally {
          TimeoutObject.Set();
          PushStatusChange(isConnectSuccess, message);
        }
      }
    }

    private void PushStatusChange(bool isConnected, string message, bool isReplay = false) {
      if (OnSocketStatusChange == null) {
        return;
      }

      if (isConnected == IsConnectSuccess && message == _sockErrorStr && isReplay == false) {
        return;
      }

      IsConnectSuccess = isConnected;
      _sockErrorStr = message;
      OnSocketStatusChange?.Invoke(IsConnectSuccess, _sockErrorStr);
    }

    /// <summary>
    ///   开始KeepAlive检测函数
    /// </summary>
    private void StartReceive() {
      _stateObjectCurrent.WorkSocket.BeginReceive(_stateObjectCurrent.Buffer, 0, SocketAsyncResult.BufferSize,
        SocketFlags.None, OnReceive,
        _stateObjectCurrent);
    }

    private void OnReceive(IAsyncResult ar) {
      var state = (SocketAsyncResult)ar.AsyncState;
      if (state == null) {
        return;
      }

      try {
        var handler = state.WorkSocket;
        var bytesRead = handler.EndReceive(ar);
        if (bytesRead > 0) {
          var tmp = new byte[bytesRead];
          Array.ConstrainedCopy(state.Buffer, 0, tmp, 0, bytesRead);
          var hasCompleted = state.AddData(tmp, out var messages);
          if (hasCompleted) {
            foreach (var message in messages) {
              OnPublishData?.Invoke(message);
            }
          }
        }

        handler.BeginReceive(state.Buffer, 0, SocketAsyncResult.BufferSize, SocketFlags.None,
          OnReceive, state);
      }
      catch (SocketException ex) {
        PushStatusChange(false, $"Code:{ex.NativeErrorCode} {ex.Message}");
      }
      catch (Exception) {
        if (state.WorkSocket == null || state.WorkSocket.Connected == false) {
          return;
        }

        state.WorkSocket.BeginReceive(state.Buffer, 0, SocketAsyncResult.BufferSize, SocketFlags.None, OnReceive,
          state);
      }
    }

    /// <summary>
    ///   当socket.connected为false时，进一步确定下当前连接状态
    /// </summary>
    /// <returns></returns>
    private bool IsSocketConnected() {
      #region remarks

      /********************************************************************************************
       * 当Socket.Concreted为false时， 如果您需要确定连接的当前状态，请进行非阻塞、零字节的 Send 调用。
       * 如果该调用成功返回或引发 WOODBLOCK 错误代码 (10035)，则该套接字仍然处于连接状态； 
       * 否则，该套接字不再处于连接状态。
       * Depending on http://msdn.microsoft.com/zh-cn/library/system.net.sockets.socket.connected.aspx?cs-save-lang=1&cs-lang=csharp#code-snippet-2
      ********************************************************************************************/

      #endregion

      if (_stateObjectCurrent?.WorkSocket == null || _stateObjectCurrent.WorkSocket.Connected == false) {
        return false;
      }

      #region 过程

      var connectState = false;
      var message = "";
      try {
        var tmp = Encoding.UTF8.GetBytes(SocketAsyncResult.CsHeartbeat);
        _stateObjectCurrent.WorkSocket.Blocking = false;
        _stateObjectCurrent.WorkSocket.Send(tmp, tmp.Length, 0);
        connectState = true;
        message = "连接还在保持";
      }
      catch (SocketException e) {
        if (e.NativeErrorCode.Equals(10035)) {
          connectState = true;
          message = "连接还在保持(10035)";
        }
        else {
          Console.WriteLine("Disconnected: error code {0}!", e.NativeErrorCode);
          message = $"Code:{e.NativeErrorCode} {e.Message}";
          connectState = false;
        }
      }
      finally {
        PushStatusChange(connectState, message, true);
      }

      return connectState;

      #endregion
    }
  }
}