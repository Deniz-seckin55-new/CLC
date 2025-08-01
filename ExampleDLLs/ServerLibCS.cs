using CLC;
using ServerClient;
using System.IO.Pipelines;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Text;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace ServerClient
{
    public class Server
    {
        public TcpListener listener;
        public string CurrentVideoPlaying = "https://www.youtube.com/watch?v=DeumyOzKqgI";
        public List<TcpClient> clients = new();
        public bool Running = false;

        public Server(int port)
        {
            listener = new TcpListener(IPAddress.Any, port);
            Console.WriteLine("Started Server");
        }
        private void SendClientInitial(TcpClient client)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("SET:::CURRENTVIDEO:::"+CurrentVideoPlaying);
            client.GetStream().Write(bytes, 0, bytes.Length);

            Console.WriteLine("Sent initial");
        }
        public void Start(int maxUsers = 32)
        {
            listener.Start(maxUsers);

            Running = true;

            new Thread(() =>
            {
                while (Running) { 
                    Console.WriteLine("Listening...");
                    TcpClient client = listener.AcceptTcpClient();
                    clients.Add(client);
                    Console.WriteLine("Client connected!");

                    SendClientInitial(client);

                    Console.WriteLine("Sending initial...");
                }
            }).Start();
        }
    }
    public class Client
    {
        public TcpClient client;
        public Dictionary<string, string> vars = new();
        public bool Running = false;
        YoutubeDL ytdl;
        public Client()
        {
            client = new TcpClient();
            ytdl = new YoutubeDL();

            ytdl.OutputFolder = Environment.CurrentDirectory + "/sounds";
        }

        public void Start(string hostname, int port)
        {
            Console.WriteLine("Starting client");

            //YoutubeDLSharp.Utils.DownloadYtDlp().Wait();
            //YoutubeDLSharp.Utils.DownloadFFmpeg().Wait();

            client.Connect(hostname, port);

            Console.WriteLine("Connected");

            Running = true;

            new Thread(() =>
            {
                while (Running)
                {
                    Thread.Sleep(10000);
                    if (client.GetStream().DataAvailable)
                    {
                        byte[] bytes = new byte[2048];

                        int readBytes = client.GetStream().Read(bytes, 0, bytes.Length);
                        string message = Encoding.UTF8.GetString(bytes, 0, readBytes).Trim();

                        Console.WriteLine("Got: " + message);

                        string[] split = message.Split(":::");

                        if (split[0] == "SET")
                        {
                            if (vars.ContainsKey(split[1]))
                                vars[split[1]] = split[2];
                            else
                                vars.Add(split[1], split[2]);

                            if (split[1] == "CURRENTVIDEO")
                            {
                                ytdl.OutputFileTemplate = "downloaded_audio.%(ext)s";
                                var task = ytdl.RunAudioDownload(split[2], AudioConversionFormat.Wav);
                                task.Wait();
                                var res = task.Result;
                                if (res.Success)
                                {
                                    var path = res.Data;
                                    SoundPlayer sp = new SoundPlayer();
                                    sp.SoundLocation = path;

                                    sp.Play();
                                }
                                else
                                {
                                    Console.WriteLine("Error accured while downloading video");
                                }
                            }
                        }
                    }
                }
            }).Start();
        }
    }
}

public static class ServerLibCS
{
    public static Dictionary<string, Func<Scope, Variable[], Variable?>> Run(Scope scope)
    {
        Server server;
        Client client;
        Dictionary<string, Func<Scope, Variable[], Variable?>> extensionFunctions = new()
        {
            {"server.create", (Scope scope, Variable[] value) => {
                const string f = "server.create";
                if(value.Length != 1)
                    CLCPublic.ThrowArgumentError(f, 1, value.Length);

                if(value[0].T != typeof(int))
                    CLCPublic.ThrowTypeError(f, typeof(int), value[0].T);

                int arg1 = (int)value[0].Value;

                server = new(arg1);

                server.Start();

                return null;
            } },
            {"client.create", (Scope scope, Variable[] value) => {
                const string f = "server.create";
                if(value.Length == 1)
                    CLCPublic.ThrowArgumentError(f, 2, value.Length);

                if(value[0].T != typeof(string))
                    CLCPublic.ThrowTypeError(f, typeof(string), value[0].T);

                if(value[1].T != typeof(int))
                    CLCPublic.ThrowTypeError(f, typeof(int), value[0].T);

                string arg1 = (string)value[0].Value;
                int arg2 = (int)value[1].Value;

                client = new();

                client.Start(arg1, arg2);

                return null;
            } },
        };

        return extensionFunctions;
    }
}