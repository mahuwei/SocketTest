using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SocketClient {
  // State object for receiving data from remote device.  
  public class StateObject {
    // Size of receive buffer.  
    public const int BufferSize = 256;

    // Receive buffer.  
    public byte[] Buffer = new byte[BufferSize];

    // Received data string.  
    public StringBuilder Sb = new StringBuilder();

    // Client socket.  
    public Socket WorkSocket;
  }

  public class AsynchronousClient {
    // The port number for the remote device.  
    private const int Port = 11000;

    // ManualResetEvent instances signal completion.  
    private static readonly ManualResetEvent ConnectDone = new ManualResetEvent(false);

    private static readonly ManualResetEvent SendDone = new ManualResetEvent(false);

    private static readonly ManualResetEvent ReceiveDone = new ManualResetEvent(false);

    // The response from the remote device.  
    private static String _response = String.Empty;

    private static void StartClient() {
      // Connect to a remote device.  
      try {
        // Establish the remote endpoint for the socket.  
        // The name of the
        // remote device is "host.contoso.com".  
        //var ipHostInfo = Dns.GetHostEntry("host.contoso.com");
        var ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
        var ipAddress = ipHostInfo.AddressList[0];
        var remoteEp = new IPEndPoint(ipAddress, Port);

        // Create a TCP/IP socket.  
        var client = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        // Connect to the remote endpoint.  
        client.BeginConnect(remoteEp, ConnectCallback, client);
        ConnectDone.WaitOne();

        // Send test data to the remote device.  
        Send(client, "This is a test<EOF>");
        SendDone.WaitOne();

        // Receive the response from the remote device.  
        Receive(client);
        ReceiveDone.WaitOne();

        // Write the response to the console.  
        Console.WriteLine("Response received : {0}", _response);

        // Release the socket.  
        client.Shutdown(SocketShutdown.Both);
        client.Close();
      }
      catch (Exception e) {
        Console.WriteLine(e.ToString());
      }
    }

    private static void ConnectCallback(IAsyncResult ar) {
      try {
        // Retrieve the socket from the state object.  
        var client = (Socket)ar.AsyncState;

        // Complete the connection.  
        client.EndConnect(ar);

        Console.WriteLine("Socket connected to {0}", client.RemoteEndPoint);

        // Signal that the connection has been made.  
        ConnectDone.Set();
      }
      catch (Exception e) {
        Console.WriteLine(e.ToString());
      }
    }

    private static void Receive(Socket client) {
      try {
        // Create the state object.  
        var state = new StateObject { WorkSocket = client };

        // Begin receiving the data from the remote device.  
        client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
      }
      catch (Exception e) {
        Console.WriteLine(e.ToString());
      }
    }

    private static void ReceiveCallback(IAsyncResult ar) {
      try {
        // Retrieve the state object and the client socket
        // from the asynchronous state object.  
        var state = (StateObject)ar.AsyncState;
        var client = state.WorkSocket;

        // Read data from the remote device.  
        var bytesRead = client.EndReceive(ar);

        if (bytesRead > 0) {
          // There might be more data, so store the data received so far.  
          state.Sb.Append(Encoding.UTF8.GetString(state.Buffer, 0, bytesRead));

          // Get the rest of the data.  
          client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
        }
        else {
          // All the data has arrived; put it in response.  
          if (state.Sb.Length > 1) {
            _response = state.Sb.ToString();
          }

          // Signal that all bytes have been received.  
          ReceiveDone.Set();
        }
      }
      catch (Exception e) {
        Console.WriteLine(e.ToString());
      }
    }

    private static void Send(Socket client, String data) {
      // Convert the string data to byte data using ASCII encoding.  
      var byteData = Encoding.UTF8.GetBytes(data);

      // Begin sending the data to the remote device.  
      client.BeginSend(byteData, 0, byteData.Length, 0, SendCallback, client);
    }

    private static void SendCallback(IAsyncResult ar) {
      try {
        // Retrieve the socket from the state object.  
        var client = (Socket)ar.AsyncState;

        // Complete sending the data to the remote device.  
        var bytesSent = client.EndSend(ar);
        Console.WriteLine("Sent {0} bytes to server.", bytesSent);

        // Signal that all bytes have been sent.  
        SendDone.Set();
      }
      catch (Exception e) {
        Console.WriteLine(e.ToString());
      }
    }

    public static int Start() {
      StartClient();
      return 0;
    }
  }
}