using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;

namespace DMPServerReportingReceiver
{
    public delegate void ConnectionCallback(ClientObject client,byte[] messageData);
    public enum MessageTypes
    {
        HEARTBEAT,
        REPORTING_VERSION_1,
        REPORTING_VERSION_2,
    }

    public class MainClass
    {
        //Server socket
        private static TcpListener serverListener;
        //State tracking
        private static int connectedClients = 0;
        public static List<ClientObject> clients = new List<ClientObject>();
        public static Stopwatch programClock = new Stopwatch();
        //Server disappears after 60 seconds
        private const int CONNECTION_TIMEOUT = 30000;
        //5MB max message size
        private const int MAX_PAYLOAD_SIZE = 5000 * 1024;
        //Message handlers
        private static Dictionary<int, ConnectionCallback> registeredHandlers = new Dictionary<int, ConnectionCallback>();
        public static DatabaseConnection databaseConnection = new DatabaseConnection();

        public static void Main()
        {
            SetErrorCounter();
            programClock.Start();
            //Register handlers
            registeredHandlers.Add((int)MessageTypes.HEARTBEAT, MessageHandlers.HandleHeartbeat);
            registeredHandlers.Add((int)MessageTypes.REPORTING_VERSION_1, MessageHandlers.HandleReportingVersion1);
            registeredHandlers.Add((int)MessageTypes.REPORTING_VERSION_2, MessageHandlers.HandleReportingVersion2);
            ReportTee.StartReportTee();
            StartServer();
            ExpireAllOnlineServers();
            while (true)
            {
                CheckTimeouts();
                Thread.Sleep(500);
            }
        }

        private static void SetErrorCounter()
        {
            string errorDirectory = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
            if (!Directory.Exists(errorDirectory))
            {
                Directory.CreateDirectory(errorDirectory);
            }
            while (File.Exists(Path.Combine(errorDirectory, MessageHandlers.reportID + ".txt")))
            {
                MessageHandlers.reportID++;
            }
            Console.WriteLine("Error pointer set to " + MessageHandlers.reportID);
        }

        private static void ConnectClient(ClientObject newClient)
        {
            lock (clients)
            {
                ReportTee.QueueConnect(newClient);
                clients.Add(newClient);
                connectedClients = clients.Count;
                Console.WriteLine("New connection from " + newClient.address.ToString() + ", connected: " + connectedClients);
            }
        }

        public static void DisconnectClient(ClientObject disconnectClient)
        {
            lock (clients)
            {
                if (clients.Contains(disconnectClient))
                {
                    ReportTee.QueueDisconnect(disconnectClient);
                    clients.Remove(disconnectClient);
                    if (disconnectClient.initialized)
                    {
                        CallServerOffline(disconnectClient.serverHash);
                    }
                    connectedClients = clients.Count;
                    Console.WriteLine("Dropped connection from " + disconnectClient.address.ToString() + ", connected: " + connectedClients);
                    try
                    {
                        if (disconnectClient.clientConnection.Connected)
                        {
                            disconnectClient.clientConnection.GetStream().Close();
                            disconnectClient.clientConnection.GetStream().Dispose();
                            disconnectClient.clientConnection.Close();
                        }
                    }
                    catch
                    {
                        //Don't care.
                    }
                }
            }
        }

        private static void ExpireAllOnlineServers()
        {
            databaseConnection.ExecuteNonReader("CALL gameserverscleanup()");
        }

        private static void CallServerOffline(string hash)
        {
            Dictionary<string, object> offlineParams = new Dictionary<string, object>();
            offlineParams["@hash"] = hash;
            string mySql = "CALL gameserveroffline(@hash)";
            databaseConnection.ExecuteNonReader(mySql, offlineParams);
        }

        public static void DisconnectOtherClientsWithHash(ClientObject ourClient, string hash)
        {
            foreach (ClientObject client in clients.ToArray())
            {
                if (client.serverHash == hash && client != ourClient)
                {
                    Console.WriteLine("Disconnecting duplicate client: " + client.serverHash);
                    DisconnectClient(client);
                }
            }
        }

        private static void CheckTimeouts()
        {
            lock (clients)
            {
                foreach (ClientObject client in clients.ToArray())
                {
                    if (programClock.ElapsedMilliseconds > (client.lastReceiveTime + CONNECTION_TIMEOUT))
                    {
                        DisconnectClient(client);
                    }
                }
            }
        }

        private static void StartServer()
        {
            serverListener = new TcpListener(IPAddress.Any, 9001);
            serverListener.Start();
            serverListener.BeginAcceptTcpClient(AcceptCallback, null);
            Console.WriteLine("Listening for connections!");
        }

        private static void AcceptCallback(IAsyncResult ar)
        {
            TcpClient clientConnection = serverListener.EndAcceptTcpClient(ar);
            try
            {
                if (clientConnection.Connected)
                {
                    SetupNewClient(clientConnection);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Client failed to connect, error: " + e);
            }
            serverListener.BeginAcceptSocket(AcceptCallback, null);
        }

        private static void SetupNewClient(TcpClient clientConnection)
        {
            //Create a new ClientObject for the reporting client
            ClientObject newClient = new ClientObject();
            newClient.clientConnection = clientConnection;
            newClient.incomingMessage = new NetworkMessage();
            newClient.incomingMessage.data = new byte[8];
            newClient.bytesToReceive = 8;
            newClient.lastReceiveTime = programClock.ElapsedMilliseconds;
            newClient.address = (IPEndPoint)newClient.clientConnection.Client.RemoteEndPoint;
            ConnectClient(newClient);
            try
            {
                newClient.clientConnection.GetStream().BeginRead(newClient.incomingMessage.data, newClient.incomingMessage.data.Length - newClient.bytesToReceive, newClient.bytesToReceive, ReceiveCallback, newClient);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error setting up new client, Exception: " + e);
                DisconnectClient(newClient);
            }
        }

        private static void ReceiveCallback(IAsyncResult ar)
        {
            ClientObject client = (ClientObject)ar.AsyncState;
            try
            {
                int bytesReceived = client.clientConnection.GetStream().EndRead(ar);
                client.bytesToReceive -= bytesReceived;
                if (bytesReceived > 0)
                {
                    client.lastReceiveTime = programClock.ElapsedMilliseconds;
                }
                if (client.bytesToReceive == 0)
                {
                    //We have a header or a payload
                    if (!client.isRecevingPayload)
                    {
                        //We have a header
                        client.incomingMessage.type = BitConverter.ToInt32(client.incomingMessage.data, 0);
                        int messagePayload = BitConverter.ToInt32(client.incomingMessage.data, 4);
                        if (messagePayload > MAX_PAYLOAD_SIZE || MAX_PAYLOAD_SIZE < 0)
                        {
                            Console.WriteLine("Invalid TCP message. Disconnecting client.");
                            DisconnectClient(client);
                            return;
                        }
                        if (messagePayload == 0)
                        {
                            client.incomingMessage.data = null;
                            HandleMessage(client, client.incomingMessage);
                            client.incomingMessage = new NetworkMessage();
                            client.incomingMessage.data = new byte[8];
                            client.bytesToReceive = 8;
                        }
                        else
                        {
                            client.isRecevingPayload = true;
                            client.incomingMessage.data = new byte[messagePayload];
                            client.bytesToReceive = messagePayload;
                        }
                    }
                    else
                    {
                        //We have a payload
                        HandleMessage(client, client.incomingMessage);
                        client.isRecevingPayload = false;
                        client.incomingMessage = new NetworkMessage();
                        client.incomingMessage.data = new byte[8];
                        client.bytesToReceive = 8;
                    }
                }
                client.clientConnection.GetStream().BeginRead(client.incomingMessage.data, client.incomingMessage.data.Length - client.bytesToReceive, client.bytesToReceive, ReceiveCallback, client);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error reading data, Exception: " + e);
                DisconnectClient(client);
            }
        }

        private static void HandleMessage(ClientObject client, NetworkMessage receivedMessage)
        {
            if (registeredHandlers.ContainsKey(receivedMessage.type))
            {
                try
                {
                    registeredHandlers[receivedMessage.type](client, receivedMessage.data);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error processing type " + receivedMessage.type + ", Exception :" + e);
                }
            }
        }
    }
}
