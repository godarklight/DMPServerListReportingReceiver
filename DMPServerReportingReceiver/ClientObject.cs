using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace DMPServerReportingReceiver
{
    public class ClientObject
    {
        public TcpClient clientConnection;
        public IPEndPoint address;
        public NetworkMessage incomingMessage;
        public bool isRecevingPayload = false;
        public int bytesToReceive = 8;
        public long lastReceiveTime = long.MinValue;
        public string serverHash;
        public string[] connectedPlayers;
        public bool initialized;
        public byte[] lastReport;
    }
}

