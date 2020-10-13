using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SocketHelper {
  /// <summary>
  ///   Socket消息
  /// </summary>
  public class SocketMessage {
    public const int CsZipBeginLength = 2 * 1024 * 1024;

    /// <summary>
    ///   消息类型
    ///   注: messageType % 2 == 0表示返回消息，否则为请求消息（即：返回消息类型=请求消息类型+1）
    /// </summary>
    public int MessageType { get; set; }

    /// <summary>
    ///   是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    ///   失败原因
    /// </summary>
    public string ErrorDetail { get; set; }

    /// <summary>
    ///   数据信息
    /// </summary>
    public string Data { get; private set; }

    /// <summary>
    ///   是否压缩数据
    /// </summary>
    public bool IsZip { get; set; }

    private void CopyTo(Stream src, Stream dest) {
      var bytes = new byte[4096];

      int cnt;

      while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0) {
        dest.Write(bytes, 0, cnt);
      }
    }

    /// <summary>
    ///   设置数据
    ///   设置数据大于<see cref="CsZipBeginLength" />将会进行压缩
    /// </summary>
    /// <param name="data"></param>
    public void SetData(string data) {
      if (data.Length < CsZipBeginLength) {
        Data = data;
        IsZip = false;
        return;
      }

      var bytes = Encoding.UTF8.GetBytes(data);
      using var msi = new MemoryStream(bytes);
      using var mso = new MemoryStream();
      using (var gs = new GZipStream(mso, CompressionMode.Compress)) {
        CopyTo(msi, gs);
      }

      var ret = mso.ToArray();
      Data = Convert.ToBase64String(ret);
      IsZip = true;
    }

    /// <summary>
    ///   获取数据
    /// </summary>
    /// <returns></returns>
    public string GetData() {
      if (IsZip == false) {
        return Data;
      }

      var bytes = Convert.FromBase64String(Data);
      using var msi = new MemoryStream(bytes);
      using var mso = new MemoryStream();
      using (var gs = new GZipStream(msi, CompressionMode.Decompress)) {
        CopyTo(gs, mso);
      }

      return Encoding.UTF8.GetString(mso.ToArray());
    }
  }
}