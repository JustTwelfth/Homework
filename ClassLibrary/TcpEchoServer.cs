using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClassLibrary
{
    public class TcpEchoServer
    {
        private const int Port = 7777;
        private const int MaxMessageLength = 65536;
        private TcpListener listener;
        private Thread acceptThread, receiveThread, processThread;
        private TcpClient currentClient;
        private NetworkStream stream;
        private Queue<string> messageQueue = new Queue<string>();
        private AutoResetEvent messageEvent = new AutoResetEvent(false);
        private AutoResetEvent queueAccess = new AutoResetEvent(true);
        private ManualResetEvent stopEvent = new ManualResetEvent(false);
        private bool isRunning = true;
        private List<byte> dataBuffer = new List<byte>();
        private Encoding utf8Strict = Encoding.GetEncoding("utf-8", new EncoderReplacementFallback(""), new DecoderExceptionFallback());
        public event Action<string> LogEvent;

        public void Start()
        {
            listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            LogEvent?.Invoke("Сервер запущен");
            acceptThread = new Thread(AcceptLoop);
            acceptThread.Start();
        }

        public void Stop()
        {
            listener?.Stop();
            currentClient?.Close();
            currentClient?.Dispose();
            stream?.Dispose();
            stopEvent.Set();
            acceptThread?.Join();
            receiveThread?.Join();
            processThread?.Join();
            try
            {
                messageEvent.Dispose();
                queueAccess.Dispose();
                stopEvent.Dispose();
            }
            catch { }
            LogEvent?.Invoke("Сервер остановлен");
        }

        private void AcceptLoop()
        {
            while (true)
            {
                try
                {
                    ManualResetEvent acceptEvent = new ManualResetEvent(false);
                    if (stopEvent.WaitOne(0)) break;
                    var newClient = listener.AcceptTcpClient();
                    if (currentClient != null && currentClient.Connected)
                    {
                        newClient.Close();
                        LogEvent?.Invoke("Новое соединение отклонено, клиент уже подключен");
                    }
                    else
                    {
                        currentClient = newClient;
                        stream = currentClient.GetStream();
                        LogEvent?.Invoke("Клиент подключен");
                        receiveThread = new Thread(ReceiveLoop);
                        processThread = new Thread(ProcessLoop);
                        receiveThread.Start();
                        processThread.Start();
                    }
                }
                catch (SocketException) { if (stopEvent.WaitOne(0)) break; }
            }
        }

        private void ReceiveLoop()
        {
            byte[] readBuffer = new byte[16384];
            while (true)
            {
                if (stopEvent.WaitOne(0) || !currentClient.Connected) break;
                try
                {
                    int bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);
                    if (bytesRead == 0) break;
                    bool endMessage = readBuffer.Take(bytesRead).Contains((byte)'\n');
                    bool isWhitespaceOnly = readBuffer.Take(bytesRead).All(b => b == 0x20 || b == 0x09 || b == 0x0B || b == 0x0C || b == 0x0D);
                    string hexMessage = BitConverter.ToString(readBuffer.Take(bytesRead).ToArray());
                    if (!endMessage && isWhitespaceOnly)
                    {
                        LogEvent?.Invoke("Пустой фрагмент сообщения был проигнорирован");
                        continue;
                    }
                    if (bytesRead < 256)
                    {
                        LogEvent?.Invoke($"Получен фрагмент сообщения от клиента, размером {bytesRead} байт: {hexMessage}");
                    }
                    dataBuffer.AddRange(readBuffer.Take(bytesRead));
                    while (true)
                    {
                        int nlPos = dataBuffer.IndexOf((byte)'\n');
                        if (nlPos < 0) break;
                        if (nlPos > MaxMessageLength)
                        {
                            LogEvent?.Invoke("Ошибка: сообщение превысило максимальную длину");
                            byte[] errorResponse = Encoding.UTF8.GetBytes("Ошибка, сообщение превысило максимальную длину\n");
                            stream.Write(errorResponse, 0, errorResponse.Length);
                            LogEvent?.Invoke("Отправлено клиенту: error-max-lenght");
                            dataBuffer.Clear();
                            continue;
                        }
                        byte[] msgBytes = dataBuffer.GetRange(0, nlPos + 1).ToArray();
                        dataBuffer.RemoveRange(0, nlPos + 1);
                        LogEvent?.Invoke($"Полное сообщение от клиента : {BitConverter.ToString(msgBytes)}");
                        string msg;
                        try
                        {
                            msg = utf8Strict.GetString(msgBytes, 0, msgBytes.Length - 1);
                            if (string.IsNullOrWhiteSpace(msg))
                            {
                                LogEvent?.Invoke("Пустое сообщение было проигнорировано");
                                continue;
                            }
                            LogEvent?.Invoke($"Декодированное сообщение: {msg}\\n");
                            queueAccess.WaitOne();
                            try
                            {
                                messageQueue.Enqueue(msg);
                            }
                            finally
                            {
                                queueAccess.Set();
                            }
                            messageEvent.Set();

                        }
                        catch (DecoderFallbackException)
                        {
                            LogEvent?.Invoke("Ошибка формата, некорректные данные");
                            byte[] errorResponse = Encoding.UTF8.GetBytes("Ошибка, сообщение содержит некорректные данные\n");
                            stream.Write(errorResponse, 0, errorResponse.Length);
                            LogEvent?.Invoke("Отправлено клиенту: error-invalid-data");
                            dataBuffer.Clear();
                        }
                    }

                }
                catch (Exception ex)
                {
                    LogEvent?.Invoke($"ОШИБКА, {ex.GetType().Name}: {ex.Message}");
                    break;
                }
            }
            queueAccess.WaitOne();
            try
            {
                messageQueue.Clear();
            }
            finally { queueAccess.Set(); }
            messageEvent.Set();
            LogEvent?.Invoke("Клиент отключен");
        }

        private void ProcessLoop()
        {
            while (isRunning)
            {
                WaitHandle[] handles = { messageEvent, stopEvent };
                int index = WaitHandle.WaitAny(handles);
                if (index == 1) break;
                while (true)
                {
                    string msg = null;
                    queueAccess.WaitOne();
                    try
                    {
                        if (messageQueue.Count == 0) break;
                        msg = messageQueue.Dequeue();
                    }
                    finally { queueAccess.Set(); }
                    if (currentClient.Connected && !stopEvent.WaitOne(0))
                    {
                        try
                        {
                            string receiveMsg = ($"echo-{msg}\\n");
                            receiveMsg = receiveMsg.Replace("\\n", "\n");
                            byte[] response = Encoding.UTF8.GetBytes(receiveMsg);
                            stream.Write(response, 0, response.Length);
                            LogEvent?.Invoke($"Отправлено клиенту: echo-{msg}\\n");

                        }
                        catch (Exception ex)
                        {
                            LogEvent?.Invoke($"ОШИБКА, {ex.GetType().Name}: {ex.Message}");
                        }

                    }
                }
            }
        }
    }
}