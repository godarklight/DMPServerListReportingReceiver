using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MessageStream2;

namespace DMPServerReportingReceiver
{
    static public class ReportTee
    {
        private static int newClientID = 0;
        private static Dictionary<ClientObject, int> clientIDs = new Dictionary<ClientObject, int>();
        private static Queue<ReportData> reportDataQueue = new Queue<ReportData>();
        private static Thread reportThread;
        private static TcpClient reportTCPClient;
        private static float lastSendTime = float.NegativeInfinity;
        private static float lastReceiveTime = float.NegativeInfinity;
        //Receive side
        private static byte[] receiveBytes;
        private static int bytesToReceive;
        private static bool receivingHeader;
        private static int receivingType;

        public static void StartReportTee()
        {
            reportThread = new Thread(new ThreadStart(SendThreadMain));
            reportThread.IsBackground = true;
            reportThread.Start();
        }

        private static void SendThreadMain()
        {
            while (true)
            {
                try
                {
                    if (reportTCPClient == null)
                    {
                        AttemptToConnect();
                        if (reportTCPClient != null)
                        {
                            Console.WriteLine("Sending all client reports");
                            foreach (ClientObject client in MainClass.clients.ToArray())
                            {
                                ReportData connectData = new ReportData();
                                connectData.clientObject = client;
                                connectData.reportType = ReportType.CONNECT;
                                byte[] connectBytes = GetReportBytes(connectData);
                                reportTCPClient.GetStream().Write(connectBytes, 0, connectBytes.Length);
                                if (client.initialized && client.lastReport != null)
                                {
                                    ReportData lastData = new ReportData();
                                    lastData.clientObject = client;
                                    lastData.clientReport = client.lastReport;
                                    lastData.reportType = ReportType.REPORT_V2;
                                    byte[] lastBytes = GetReportBytes(lastData);
                                    reportTCPClient.GetStream().Write(lastBytes, 0, lastBytes.Length);
                                }
                            }
                            Console.WriteLine("Sent all client reports");
                        }
                        else
                        {
                            //Try to connect every minute
                            Console.WriteLine("Failed to connect, waiting 60 seconds.");
                            Thread.Sleep(60000);
                        }
                    }
                    if (reportTCPClient != null)
                    {
                        while (reportDataQueue.Count > 0)
                        {
                            ReportData rd;
                            lock (reportDataQueue)
                            {
                                rd = reportDataQueue.Dequeue();
                            }
                            byte[] reportBytes = GetReportBytes(rd);
                            reportTCPClient.GetStream().Write(reportBytes, 0, reportBytes.Length);
                            lastSendTime = MainClass.programClock.ElapsedMilliseconds;
                        }
                        //30 sec heartbeat
                        if ((MainClass.programClock.ElapsedMilliseconds - lastSendTime) > 30000)
                        {
                            byte[] heartBeat = new byte[8];
                            reportTCPClient.GetStream().Write(heartBeat, 0, heartBeat.Length);
                        }
                        //60 sec timeout
                        if ((MainClass.programClock.ElapsedMilliseconds - lastReceiveTime) > 60000)
                        {
                            Console.WriteLine("Reporting tee connection timed out.");
                            try
                            {
                                reportTCPClient.Close();
                            }
                            catch
                            {
                                //Don't care
                            }
                            reportTCPClient = null;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Report tee error: " + e.Message);
                    if (reportTCPClient != null)
                    {
                        try
                        {
                            reportTCPClient.Close();
                        }
                        catch
                        {
                            //Don't care.
                        }
                        reportTCPClient = null;
                        Thread.Sleep(60000);
                    }
                }
                Thread.Sleep(1000);
            }
        }

        private static void ReceiveCallback(IAsyncResult ar)
        {
            TcpClient receiveClient = (TcpClient)ar.AsyncState;
            try
            {  
                if (receiveClient != reportTCPClient)
                {
                    receiveClient.Close();
                    return;
                }
                int receivedBytes = receiveClient.GetStream().EndRead(ar);
            
                if (receivedBytes > 0)
                {
                    lastReceiveTime = MainClass.programClock.ElapsedMilliseconds;
                    bytesToReceive -= receivedBytes;
                    if (bytesToReceive == 0)
                    {
                        if (!receivingHeader)
                        {
                            byte[] typeBytes = new byte[4];
                            byte[] lengthBytes = new byte[4];
                            Array.Copy(receiveBytes, 0, typeBytes, 0, typeBytes.Length);
                            Array.Copy(receiveBytes, typeBytes.Length, lengthBytes, 0, lengthBytes.Length);
                            if (BitConverter.IsLittleEndian)
                            {
                                Array.Reverse(typeBytes);
                                Array.Reverse(lengthBytes);
                            }
                            receivingType = BitConverter.ToInt32(typeBytes, 0);
                            int receivingLength = BitConverter.ToInt32(lengthBytes, 0);
                            if (receivingLength == 0)
                            {
                                bytesToReceive = 8;
                                receiveBytes = new byte[8];
                            }
                            else
                            {
                                receivingHeader = true;
                                bytesToReceive = receivingLength;
                                receiveBytes = new byte[receivingLength];
                            }
                        }
                        else
                        {
                            Console.WriteLine("Reporting tee, type: " + receivingType + ", length: " + receiveBytes.Length);
                            receivingHeader = false;
                            bytesToReceive = 8;
                            receiveBytes = new byte[8];
                        }
                    }
                }
                receiveClient.GetStream().BeginRead(receiveBytes, 0, bytesToReceive, ReceiveCallback, receiveClient);
            }
            catch
            {
                try
                {
                    receiveClient.Close();
                }
                catch
                {
                }
            }
        }

        private static void AttemptToConnect()
        {
            try
            {
                IPHostEntry hostEntry = Dns.GetHostEntry("godarklight.info.tm");
                if (hostEntry.AddressList.Length > 0)
                {
                    IPAddress firstAddress = hostEntry.AddressList[0];
                    TcpClient newConnection = new TcpClient(firstAddress.AddressFamily);
                    IAsyncResult ar = newConnection.BeginConnect(firstAddress, 9003, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(5000))
                    {
                        if (newConnection.Connected)
                        {
                            newConnection.EndConnect(ar);
                            lastReceiveTime = MainClass.programClock.ElapsedMilliseconds;
                            lastSendTime = MainClass.programClock.ElapsedMilliseconds;
                            reportTCPClient = newConnection;
                            receiveBytes = new byte[8];
                            bytesToReceive = receiveBytes.Length;
                            reportTCPClient.GetStream().BeginRead(receiveBytes, 0, bytesToReceive, ReceiveCallback, reportTCPClient);
                        }
                        else
                        {
                            Console.WriteLine("Failed to connect reporting tee to " + firstAddress + ", refused");
                            try
                            {
                                newConnection.Close();
                            }
                            catch
                            {
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Error connecting reporting tee to " + firstAddress + ", timeout.");
                        try
                        {
                            newConnection.Close();
                        }
                        catch
                        {
                        }
                    }
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("Error connecting reporting tee, exception: " + e.Message);
            }
        }

        private static byte[] GetReportBytes(ReportData reportData)
        {
            int clientID = clientIDs[reportData.clientObject];
            byte[] reportBytes;
            byte[] retBytes;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(clientID);
                mw.Write<int>((int)reportData.reportType);
                if (reportData.reportType == ReportType.REPORT_V2)
                {
                    mw.Write<byte[]>(reportData.clientReport);
                }
                reportBytes = mw.GetMessageBytes();
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(1);
                mw.Write(reportBytes);
                retBytes = mw.GetMessageBytes();
            }
            return retBytes;
        }

        public static void QueueConnect(ClientObject client)
        {
            ReportData rd = new ReportData();
            rd.reportType = ReportType.CONNECT;
            rd.clientObject = client;
            clientIDs.Add(client, Interlocked.Increment(ref newClientID));
            lock (reportDataQueue)
            {
                reportDataQueue.Enqueue(rd);
            }
        }

        public static void QueueReport(ClientObject client, byte[] data)
        {
            ReportData rd = new ReportData();
            rd.reportType = ReportType.REPORT_V2;
            rd.clientObject = client;
            rd.clientReport = data;
            lock (reportDataQueue)
            {
                reportDataQueue.Enqueue(rd);
            }
        }

        public static void QueueDisconnect(ClientObject client)
        {
            ReportData rd = new ReportData();
            rd.reportType = ReportType.DICONNECT;
            rd.clientObject = client;
            clientIDs.Remove(client);
            lock (reportDataQueue)
            {
                reportDataQueue.Enqueue(rd);
            }
        }

        private class ReportData
        {
            public ClientObject clientObject;
            public ReportType reportType;
            public byte[] clientReport;
        }

        private enum ReportType
        {
            CONNECT,
            REPORT_V2,
            DICONNECT,
        }
    }
}

