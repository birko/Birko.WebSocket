using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Birko.WebSocket
{
    public class WebSocketClient
    {
        private TcpClient _client;
        private NetworkStream _stream;
        public Guid Guid { get; }

        public event Action<WebSocketClient, string> OnStringData;
        public event Action<WebSocketClient, byte[]> OnData;
        public event Action<WebSocketClient> OnHandShake;
        public event Action<WebSocketClient> OnStopped;
        public event Action<Exception> OnException;

        public bool IsRunning { get; private set; } =  false;

        public WebSocketClient(TcpClient client)
        {
            Guid = Guid.NewGuid();
            _client = client;
            _stream = client.GetStream();
        }

        public void Stop()
        {
            if (IsRunning)
            {
                IsRunning = false;
            }
        }

        public void Start()
        {
            if (IsRunning)
            {
                OnStopped.Invoke(null);
                return;
            }
            IsRunning = true;
            //enter to an infinite cycle to be able to handle every change in stream
            while (IsRunning)
            {
                while (IsRunning && !_stream.DataAvailable);
                while (IsRunning && _client.Available < 3) ;
                if(IsRunning)
                {
                    Byte[] bytes = new Byte[_client.Available];
                    _stream.Read(bytes, 0, _client.Available);
                    string data = Encoding.UTF8.GetString(bytes);

                    if (Regex.IsMatch(data, "^GET"))// test if it is handshake
                    {
                        WriteHandShake(_stream, data);
                        OnHandShake?.Invoke(this);
                    }
                    else
                    {
                        bool fin = (bytes[0] & 0b10000000) != 0,
                        mask = (bytes[1] & 0b10000000) != 0; // must be true, "All messages from the client to the server have this bit set"

                        int opcode = bytes[0] & 0b00001111, // expecting 1 - text message
                            msglen = bytes[1] - 128, // & 0111 1111
                            offset = 2;

                        if (msglen == 126)
                        {
                            // was ToUInt16(bytes, offset) but the result is incorrect
                            msglen = BitConverter.ToUInt16(new byte[] { bytes[3], bytes[2] }, 0);
                            offset = 4;
                        }
                        else if (msglen == 127)
                        {
                            throw new NotImplementedException("Larger messages not implemented");
                            //Console.WriteLine("TODO: msglen == 127, needs qword to store msglen");
                            //// i don't really know the byte order, please edit this
                            //msglen = BitConverter.ToUInt64(new byte[] { bytes[5], bytes[4], bytes[3], bytes[2], bytes[9], bytes[8], bytes[7], bytes[6] }, 0);
                            //offset = 10;
                        }

                        if (msglen == 0)
                        {
                            OnData?.Invoke(this, new byte[0]);
                            OnStringData?.Invoke(this, null);
                        }
                        else if (mask)
                        {
                            byte[] decoded = new byte[msglen];
                            byte[] masks = new byte[4] { bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3] };
                            offset += 4;

                            for (int i = 0; i < msglen; ++i)
                                decoded[i] = (byte)(bytes[offset + i] ^ masks[i % 4]);

                            OnData?.Invoke(this, decoded);
                            OnStringData?.Invoke(this, Encoding.UTF8.GetString(decoded));
                        }
                        else
                        {
                            throw new NotImplementedException("Mask bit not set");
                        }
                    }
                }
            }
            _stream?.Close();
            _client?.Close();
            _stream = null;
            _client = null;
            OnStopped.Invoke(this);
        }

        private static void WriteHandShake(NetworkStream stream, string data)
        {
            const string eol = "\r\n"; // HTTP/1.1 defines the sequence CR LF as the end-of-line marker

            Byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 101 Switching Protocols" + eol
                + "Connection: Upgrade" + eol
                + "Upgrade: websocket" + eol
                + "Sec-WebSocket-Accept: " + Convert.ToBase64String(
                    System.Security.Cryptography.SHA1.Create().ComputeHash(
                        Encoding.UTF8.GetBytes(
                            new System.Text.RegularExpressions.Regex("Sec-WebSocket-Key: (.*)").Match(data).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
                        )
                    )
                ) + eol
                + eol);

            stream.Write(response, 0, response.Length);
        }

        public void SendString(string str)
        {
            Send((!string.IsNullOrEmpty(str)) ? Encoding.UTF8.GetBytes(str) : new byte[0]);
        }

        public void Send(byte[] data)
        {
            //ns is a NetworkStream class parameter
            try
            {
                const int frameSize = 64;
                var parts = data.Select((b, i) => new { b, i })
                                .GroupBy(x => x.i / (frameSize - 1))
                                .Select(x => x.Select(y => y.b).ToArray())
                                .ToList();

                for (int i = 0; i < parts.Count; i++)
                {
                    byte cmd = 0;
                    if (i == 0) cmd |= 1;
                    if (i == parts.Count - 1) cmd |= 0x80;

                    _stream.WriteByte(cmd);
                    _stream.WriteByte((byte)parts[i].Length);
                    _stream.Write(parts[i], 0, parts[i].Length);
                }

                _stream.Flush();
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                throw;
            }
        }
    }
}
