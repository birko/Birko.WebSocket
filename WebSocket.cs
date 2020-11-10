using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Birko.WebSocket
{
    public class WebSocket
    {
        private static IDictionary<string, TcpListener> _usedListeners = null;
        private Queue<WebSocketClient> _clients = null;
        public IPAddress IP { get; }
        public int Port { get; }

        public event Action<WebSocketClient> OnClient;
        public event Action<WebSocket> OnStopped;

        public bool IsRunning { get; private set; } = false;

        public WebSocket(IPAddress ipAddress, int port = 80)
        {
            IP = ipAddress;
            Port = port;
            if (_usedListeners == null)
            {
                _usedListeners = new Dictionary<string, TcpListener>();
            }
        }

        public WebSocket(string ipAddress, int port = 80) : this(IPAddress.Parse(ipAddress), port) { }

        public void Start()
        {
            if (!IsRunning)
            {
                IsRunning = true;
            }
            else
            {
                OnStopped?.Invoke(null);
                return;
            }
            var key = $"{IP}:{Port}";
            if (!_usedListeners.ContainsKey(key))
            {
                _usedListeners.Add(key, new TcpListener(IP, Port));
                _usedListeners[key].Start();
                while (IsRunning)
                {
                    Thread.Sleep(100);
                    ThreadPool.QueueUserWorkItem(ThreadProc, _usedListeners[key]);
                }
                _usedListeners[key].Stop();
                while (_clients?.Any() == true)
                {
                    var c = _clients?.Dequeue();
                    c.Stop();
                }
                _usedListeners.Remove(key);
                OnStopped?.Invoke(this);
            }
        }

        private void ThreadProc(object obj)
        {
            TcpListener listener = (TcpListener)obj;
            TcpClient client = null;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch
            {
                client = null;
            }
            finally
            {
                if (client != null)
                {
                    if (_clients == null)
                    {
                        _clients = new Queue<WebSocketClient>();
                    }
                    var wsclient = new WebSocketClient(client);
                    _clients.Enqueue(wsclient);
                    OnClient?.Invoke(wsclient);
                }
            }
        }

        public void Stop()
        {
            if (IsRunning)
            {
                IsRunning = false;
            }
        }
    }
}
