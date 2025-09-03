using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Program
{
    static void Main()
    {
        try
        {
            using (TcpClient client = new TcpClient("localhost", 7777))
            {
                using (NetworkStream stream = client.GetStream())
                {
                    //Тест 1, отправка простого сообщения
                    SendAndReceive(stream, "Ping\n", "Отправлено: " + "Ping\\n");
                    
                    //Тест 2, отправка фрагментированного сообщения
                    SendAndReceive(stream, "Hello World", "Отправлено с фрагментацией: Hello World\\n", fragmented: true);

                    //Тест 3, отправка пустого сообщения
                    SendAndReceive(stream, "\n", "Отправлено пустое сообщение", skipRead: true);

                    //Тест 4, отправка слишком длинного сообщения
                    SendLargeandReceive(stream);

                    //Тест 5, отправка некорректных данных 
                    SendInvAndReceive(stream);

                    //Тест 6, быстрая отправка
                    var sw = new Stopwatch();
                    sw.Start();
                    for (int i = 0; i < 10; i++)
                    {
                        SendAndReceive(stream, $"Тест{i}\n", $"Быстрая отправка: Тест{i}\\n");
                    }
                    sw.Stop();
                    Console.WriteLine($"Завершена быстрая отправка 10 сообщений. Время: {sw.ElapsedMilliseconds} ms");
                    
                    Console.WriteLine("Автотесты завершены. Введите сообщение ('exit' - завершение):");

                    //Ручное тестирование
                    bool skipRead = false;
                    while (true)
                    {
                        string userInput = Console.ReadLine();
                        if (userInput?.ToLower() == "exit") break;
                        userInput = userInput.Replace("\\n", "\n");
                        SendMessage(stream, userInput);
                        if (string.IsNullOrWhiteSpace(userInput.TrimEnd('\n')) && userInput?.Contains("\n") == true)
                        {
                            Console.WriteLine("Пустое или пробельное сообщение игнорируется...");
                            skipRead = true;
                        }
                        else if (string.IsNullOrWhiteSpace(userInput) && !userInput?.Contains("\n") == true)
                        {
                            Console.WriteLine("Пустой фрагмент, продолжайте ввод...");
                            skipRead = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(userInput) && !userInput?.Contains("\n") == true)
                        {
                            Console.WriteLine($"Отправлено: {userInput}, продолжайте ввод");
                            skipRead = true;
                        }
                        else if (userInput?.Contains("\n") == true && !string.IsNullOrWhiteSpace(userInput.TrimEnd('\n')))
                        {
                            skipRead = false;
                        }
                        if (!skipRead)
                        {
                            ReadReceive(stream);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
        Console.ReadLine();
    }

    static void SendAndReceive(NetworkStream stream, string msg, string logMessage, bool fragmented = false, bool skipRead = false)
    {
        if (fragmented)
        {
            SendFragmented(stream, msg);
        }
        else
        {
            SendMessage(stream, msg);
        }
        Console.WriteLine(logMessage);
        
        if (!skipRead) 
        {
            ReadReceive(stream);
        }

        Thread.Sleep(100);
    }

    static void SendLargeandReceive(NetworkStream stream)
    {
        string longMsg = new string('A', 70000);
        byte[] data = Encoding.UTF8.GetBytes(longMsg + "\n");
        stream.Write(data, 0, data.Length);
        Console.WriteLine("Отправлено сообщение, превышающее максимальную длину");
        ReadReceive(stream);
    }

    static void SendInvAndReceive(NetworkStream stream)
    {
        byte[] invalidUtf = { 0xFF, 0xFF, 0xFE };
        stream.Write(invalidUtf, 0, invalidUtf.Length);
        stream.WriteByte((byte)'\n');
        Console.WriteLine("Отправлено сообщение с некорректными данными");
        ReadReceive(stream);
        Thread.Sleep(100);
    }

    static void SendMessage(NetworkStream stream, string msg)
    {
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);
    }

    static void SendFragmented(NetworkStream stream, string msg)
    {
        byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
        stream.Write(data, 0, data.Length / 2);
        Thread.Sleep(300);
        stream.Write(data, data.Length / 2, data.Length - data.Length / 2);
    }

    static void ReadReceive(NetworkStream stream)
    {
        byte[] buffer = new byte[1024];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        if (bytesRead > 0)
        {
            string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"Получено: {response.Replace("\n","\\n")}");
        }
        else
        {
            Console.WriteLine("Нет ответа от сервера, соединение разорвано");
        }
    }
}