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
        private static IDictionary<string, bool> _isClientReady = null;

        private Queue<WebSocketClient> _clients = null;
        private Queue<Thread> _threads = null;
        public IPAddress IP { get; }
        public int Port { get; }

        public event Action<WebSocketClient> OnClient;
        public event Action<WebSocket> OnStopped;

        public bool IsRunning { get; private set; } = false;

        public string Key { get => $"{IP}:{Port}"; }

        public WebSocket(IPAddress ipAddress, int port = 80)
        {
            IP = ipAddress;
            Port = port;
            if (_usedListeners == null)
            {
                _usedListeners = new Dictionary<string, TcpListener>();
            }
            if (_isClientReady == null)
            {
                _isClientReady = new Dictionary<string, bool>();
            }
            else if(_isClientReady.ContainsKey(Key) && !_isClientReady[Key])
            {
                throw new Exception($"WebSocket for {Key} is still running");
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
            if (!_usedListeners.ContainsKey(Key))
            {
                _usedListeners.Add(Key, new TcpListener(IP, Port));
                _usedListeners[Key].Start();
                while (IsRunning)
                {
                    Thread.Sleep(100);
                    if (!_isClientReady.ContainsKey(Key) || _isClientReady[Key])
                    {
                        _isClientReady[Key] = false;
                        if (_threads == null)
                        {
                            _threads = new Queue<Thread>();
                        }
                        Thread t = new Thread(ThreadProc);
                        _threads.Enqueue(t);
                        t.Start(_usedListeners[Key]);
                    }
                }
                _usedListeners[Key].Stop();
                while (_clients?.Any() == true)
                {
                    var c = _clients?.Dequeue();
                    c.Stop();
                }
                _usedListeners.Remove(Key);
                _isClientReady.Remove(Key);
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
                    _isClientReady[Key] = true;
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
