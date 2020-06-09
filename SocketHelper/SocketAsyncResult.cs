using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Timers;

namespace SocketHelper {
  public class SocketAsyncResult {
    /// <summary>
    ///   一次接收数据时长
    /// </summary>
    public const int ReadDataSeconds = 10;

    /// <summary>
    ///   接收数据Buffer大小
    /// </summary>
    public const int BufferSize = 8 * 1024;

    public const string SocketCloseString = "CloseSocket";

    /// <summary>
    ///   心跳消息
    /// </summary>
    public const string CsHeartbeat = "Heartbeat";

    public const string CsSocketHeader = "LEN={0}";
    public const int CsSocketHeaderLen = 14;
    public const int CsSocketHeaderLenFlag = 4;
    private readonly bool _isTimeReceive;

    // Received data string.  
    private List<byte> _receiveData = new List<byte>();

    private Timer _timer;

    // Receive buffer.  
    public byte[] Buffer = new byte[BufferSize];

    // Client  socket.  
    public Socket WorkSocket;

    /// <summary>
    ///   构造函数
    /// </summary>
    /// <param name="isTimeReceive">是否接收计时</param>
    public SocketAsyncResult(bool isTimeReceive = false) {
      _isTimeReceive = isTimeReceive;
    }

    public string Ip { get; set; }
    public int Port { get; set; }

    /// <summary>
    ///   获取接收的数据，如果有完整数据，则返回true，并将消息返回
    /// </summary>
    /// <param name="data">接收到的数据</param>
    /// <param name="messages">返回消息对列</param>
    /// <returns>返回值:true=有完整消息;false=没有完成消息；</returns>
    public bool AddData(byte[] data, out List<string> messages) {
      messages = new List<string>();
      if (data == null || data.Length == 0) {
        return false;
      }

      if (_receiveData.Any() == false) {
        // 判断是否以长度开头
        if (!JudgeSocketHeaderStart(data)) {
          return false;
        }

        StartTime();
      }

      _receiveData.AddRange(data);

      if (_receiveData.Count <= CsSocketHeaderLen) {
        return false;
      }

      var dataReceive = _receiveData.ToArray();
      GetMessages(messages, dataReceive, out var leftBytes);
      if (!messages.Any()) {
        return false;
      }

      _receiveData = new List<byte>();
      if (leftBytes == null || !leftBytes.Any()) {
        if (_timer == null) {
          return true;
        }

        _timer.Dispose();
        _timer = null;
        return true;
      }

      // 判断剩余部分是否正常
      if (JudgeSocketHeaderStart(leftBytes)) {
        _receiveData.AddRange(leftBytes);
        StartTime();
      }

      return true;
    }

    /// <summary>
    ///   判断是否以长度头（CsSocketHeader）开始
    /// </summary>
    /// <param name="data">数据信息</param>
    /// <returns>返回值:true=是；false=否；</returns>
    private static bool JudgeSocketHeaderStart(byte[] data) {
      var length = data.Length;
      if (length >= CsSocketHeaderLenFlag) {
        length = CsSocketHeaderLenFlag;
      }

      var header = Encoding.UTF8.GetString(data, 0, length);

      return header.Equals(CsSocketHeader.Substring(0, length), StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    ///   进行接收计时，超时没有收全则放弃当前以收取的数据
    /// </summary>
    private void StartTime() {
      if (_isTimeReceive == false) {
        return;
      }

      if (_timer != null) {
        _timer.Dispose();
        _timer = null;
      }

      _timer = new Timer(ReadDataSeconds) { AutoReset = false };
      _timer.Elapsed += (sender, args) => {
        _receiveData = new List<byte>();
        _timer.Dispose();
        _timer = null;
      };
      _timer.Enabled = true;
    }

    /// <summary>
    ///   处理消息内容；要递归调用
    /// </summary>
    /// <param name="messages">要返回的消息对列</param>
    /// <param name="dataReceive">接收到数据</param>
    /// <param name="leftBytes">处理消息后剩余的数据</param>
    private void GetMessages(List<string> messages, byte[] dataReceive, out byte[] leftBytes) {
      var tmp = new byte[CsSocketHeaderLen];
      Array.ConstrainedCopy(dataReceive, 0, tmp, 0, CsSocketHeaderLen);
      var header = Encoding.UTF8.GetString(tmp);
      var headerInfo = header.Split('=');
      var dataSize = Convert.ToInt32(headerInfo[1]);
      if (dataReceive.Length < dataSize) {
        leftBytes = dataReceive;
        return;
      }

      var dataBuffer = new byte[dataSize - CsSocketHeaderLen];
      Array.ConstrainedCopy(dataReceive, CsSocketHeaderLen, dataBuffer, 0, dataBuffer.Length);
      var message = Encoding.UTF8.GetString(dataBuffer);
      messages.Add(message);
      if (dataReceive.Length - dataSize == 0) {
        leftBytes = null;
        return;
      }

      var retBytes = new byte[dataReceive.Length - dataSize];
      Array.ConstrainedCopy(dataReceive, dataSize, retBytes, 0, retBytes.Length);
      GetMessages(messages, retBytes, out leftBytes);
    }

    /// <summary>
    ///   生成要发送的数据byte[]，生产时添加长度头（长度中包含消息头的长度）
    /// </summary>
    /// <param name="data">需要发送的字符串数据</param>
    /// <returns></returns>
    public static byte[] CreateSendData(string data) {
      if (string.IsNullOrEmpty(data)) {
        throw new ArgumentException(nameof(data));
      }

      var bytes = Encoding.UTF8.GetBytes(data);
      var len = bytes.Length + CsSocketHeaderLen;
      bytes = Encoding.UTF8.GetBytes(
        $"{string.Format(CsSocketHeader, len.ToString().PadLeft(CsSocketHeaderLen - CsSocketHeaderLenFlag, '0'))}{data}");
      return bytes;
    }
  }

  /// <summary>
  ///   用于发布消息
  /// </summary>
  /// <param name="message"></param>
  public delegate void PublishData(string message);
}