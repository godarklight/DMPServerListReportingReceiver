using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using MessageStream;
using MessageStream2;
using System.Net;

namespace DMPServerReportingReceiver
{
    public class MessageHandlers
    {
        public static int reportID;

        private static DatabaseConnection databaseConnection
        {
            get
            {
                return MainClass.databaseConnection;
            }
        }

        public static void HandleHeartbeat(ClientObject client, byte[] messageData)
        {
            //Don't care - these only keep the connection alive
        }

        //Method for auto-fixing the game address
        private static string GetSafeGameAddress(string inputAddress, ClientObject client)
        {
            //If there is only 1 ':' mark, they have probably incorrectly put the port after the game address. Let's cut it off.
            if (inputAddress.Contains(":") && inputAddress.IndexOf(":") == inputAddress.LastIndexOf(":"))
            {
                inputAddress = inputAddress.Substring(0, inputAddress.IndexOf(":"));
            }
            string outputAddress = inputAddress;
            IPAddress parseAddress;
            bool overrideAddress = false;
            if (inputAddress == "")
            {
                overrideAddress = true;
            }
            //Check that it's a valid IP address or DNS address.
            if (!IPAddress.TryParse(inputAddress, out parseAddress))
            {
                try
                {
                    IAsyncResult ar = Dns.BeginGetHostAddresses(inputAddress, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(30000))
                    {
                        IPAddress[] addresses = Dns.EndGetHostAddresses(ar);
                        if (addresses.Length == 0)
                        {
                            overrideAddress = true;
                        }
                    }
                    else
                    {
                        overrideAddress = true;
                    }
                }
                catch
                {
                    overrideAddress = true;
                }
            }
            if (overrideAddress)
            {
                outputAddress = client.address.Address.ToString();
            }
            return outputAddress;
        }

        public static void HandleReportingVersion1(ClientObject client, byte[] messageData)
        {
            ServerReport serverReport = new ServerReport();
            using (MessageStream.MessageReader mr = new MessageStream.MessageReader(messageData, false))
            {
                serverReport.serverHash = mr.Read<string>();
                serverReport.serverName = mr.Read<string>();
                serverReport.description = mr.Read<string>();
                serverReport.gamePort = mr.Read<int>();
                serverReport.gameAddress = GetSafeGameAddress(mr.Read<string>(), client);
                serverReport.protocolVersion = mr.Read<int>();
                serverReport.programVersion = mr.Read<string>();
                serverReport.maxPlayers = mr.Read<int>();
                serverReport.modControl = mr.Read<int>();
                serverReport.modControlSha = mr.Read<string>();
                serverReport.gameMode = mr.Read<int>();
                serverReport.cheats = mr.Read<bool>();
                serverReport.warpMode = mr.Read<int>();
                serverReport.universeSize = mr.Read<long>();
                serverReport.banner = mr.Read<string>();
                serverReport.homepage = mr.Read<string>();
                serverReport.httpPort = mr.Read<int>();
                serverReport.admin = mr.Read<string>();
                serverReport.team = mr.Read<string>();
                serverReport.location = mr.Read<string>();
                serverReport.fixedIP = mr.Read<bool>();
                serverReport.players = mr.Read<string[]>();
            }
            HandleServerReport(client, serverReport);
        }

        //Exactly the same as above, but with MessageReader2
        public static void HandleReportingVersion2(ClientObject client, byte[] messageData)
        {
            ServerReport serverReport = new ServerReport();
            using (MessageStream2.MessageReader mr = new MessageStream2.MessageReader(messageData))
            {
                serverReport.serverHash = mr.Read<string>();
                serverReport.serverName = mr.Read<string>();
                serverReport.description = mr.Read<string>();
                serverReport.gamePort = mr.Read<int>();
                serverReport.gameAddress = GetSafeGameAddress(mr.Read<string>(), client);
                serverReport.protocolVersion = mr.Read<int>();
                serverReport.programVersion = mr.Read<string>();
                serverReport.maxPlayers = mr.Read<int>();
                serverReport.modControl = mr.Read<int>();
                serverReport.modControlSha = mr.Read<string>();
                serverReport.gameMode = mr.Read<int>();
                serverReport.cheats = mr.Read<bool>();
                serverReport.warpMode = mr.Read<int>();
                serverReport.universeSize = mr.Read<long>();
                serverReport.banner = mr.Read<string>();
                serverReport.homepage = mr.Read<string>();
                serverReport.httpPort = mr.Read<int>();
                serverReport.admin = mr.Read<string>();
                serverReport.team = mr.Read<string>();
                serverReport.location = mr.Read<string>();
                serverReport.fixedIP = mr.Read<bool>();
                serverReport.players = mr.Read<string[]>();
            }
            HandleServerReport(client, serverReport);
        }

        //Handle logic
        private static void HandleServerReport(ClientObject client, ServerReport serverReport)
        {
            client.lastReport = serverReport;
            ReportTee.QueueReport(client, serverReport);
            Stopwatch sw = new Stopwatch();
            sw.Start();

            //Check if this is a new server
            client.serverHash = serverReport.serverHash;
            Dictionary<string, object> parameters = serverReport.GetParameters();

            //Initialize if needed
            if (!client.initialized)
            {
                client.initialized = true;
                MainClass.DisconnectOtherClientsWithHash(client, serverReport.serverHash);
                string sqlQuery = "CALL gameserverinit(@serverhash, @namex, @descriptionx, @gameportx, @gameaddressx, @protocolx, @programversion, @maxplayersx, @modcontrolx, @modcontrolshax, @gamemodex, @cheatsx, @warpmodex, @universex, @bannerx, @homepagex, @httpportx, @adminx, @teamx, @locationx, @fixedipx);";
                Console.WriteLine("Server " + serverReport.serverHash + " is online!");
                try
                {
                    databaseConnection.ExecuteNonReader(sqlQuery, parameters);
                }
                catch
                {
                    int thisID = Interlocked.Increment(ref reportID);
                    string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                    string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                    using (StreamWriter errorFile = new StreamWriter(errorParameters))
                    {
                        errorFile.WriteLine("State: Init");
                        errorFile.WriteLine("SQL: " + sqlQuery);
                        foreach (KeyValuePair<string,object> kvp in parameters)
                        {
                            errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                        }
                    }
                    throw;
                }
            }

            if (client.connectedPlayers == null)
            {
                //Report connected players as connected
                foreach (string connectedPlayer in serverReport.players)
                {
                    Console.WriteLine("Player " + connectedPlayer + " joined " + serverReport.serverHash);
                    Dictionary<string, object> playerParams = new Dictionary<string, object>();
                    playerParams["@hash"] = serverReport.serverHash;
                    playerParams["@player"] = connectedPlayer;
                    string sqlQuery = "CALL gameserverplayer(@hash, @player, '1')";
                    try
                    {
                        databaseConnection.ExecuteNonReader(sqlQuery, playerParams);
                    }
                    catch
                    {
                        int thisID = Interlocked.Increment(ref reportID);
                        string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                        string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                        using (StreamWriter errorFile = new StreamWriter(errorParameters))
                        {
                            errorFile.WriteLine("State: Init Players");
                            errorFile.WriteLine("SQL: " + sqlQuery);
                            foreach (KeyValuePair<string,object> kvp in playerParams)
                            {
                                errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                            }
                        }
                        throw;
                    }
                }
            }
            else
            {
                foreach (string player in serverReport.players)
                {
                    Console.WriteLine("Player: " + player);
                }
                //Take all the currently connected players and remove the players that were connected already to generate a list of players to be added
                List<string> addList = new List<string>(serverReport.players);
                foreach (string player in client.connectedPlayers)
                {
                    if (addList.Contains(player))
                    {
                        addList.Remove(player);
                    }
                }
                //Take all the old players connected and remove the players that are connected already to generate a list of players to be removed
                List<string> removeList = new List<string>(client.connectedPlayers);
                foreach (string player in serverReport.players)
                {
                    if (removeList.Contains(player))
                    {
                        removeList.Remove(player);
                    }
                }
                //Add new players
                foreach (string player in addList)
                {
                    Console.WriteLine("Player " + player + " joined " + serverReport.serverHash);
                    Dictionary<string, object> playerParams = new Dictionary<string, object>();
                    playerParams["hash"] = serverReport.serverHash;
                    playerParams["player"] = player;
                    string sqlQuery = "CALL gameserverplayer(@hash ,@player, '1')";
                    try
                    {
                        databaseConnection.ExecuteNonReader(sqlQuery, playerParams);
                    }
                    catch
                    {
                        int thisID = Interlocked.Increment(ref reportID);
                        string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                        string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                        using (StreamWriter errorFile = new StreamWriter(errorParameters))
                        {
                            errorFile.WriteLine("State: Add Player");
                            errorFile.WriteLine("SQL: " + sqlQuery);
                            foreach (KeyValuePair<string,object> kvp in playerParams)
                            {
                                errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                            }
                        }
                        throw;
                    }
                }
                //Remove old players
                foreach (string player in removeList)
                {
                    Console.WriteLine("Player " + player + " left " + serverReport.serverHash);
                    Dictionary<string, object> playerParams = new Dictionary<string, object>();
                    playerParams["hash"] = serverReport.serverHash;
                    playerParams["player"] = player;
                    string sqlQuery = "CALL gameserverplayer(@hash ,@player, '0')";
                    try
                    {
                        databaseConnection.ExecuteNonReader(sqlQuery, playerParams);
                    }
                    catch
                    {
                        int thisID = Interlocked.Increment(ref reportID);
                        string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                        string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                        using (StreamWriter errorFile = new StreamWriter(errorParameters))
                        {
                            errorFile.WriteLine("State: Remove Player");
                            errorFile.WriteLine("SQL: " + sqlQuery);
                            foreach (KeyValuePair<string,object> kvp in playerParams)
                            {
                                errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                            }
                        }
                        throw;
                    }
                }
            }
            //Save connected players for tracking
            client.connectedPlayers = serverReport.players;

            sw.Stop();
            Console.WriteLine("Handled report from " + serverReport.serverName + " (" + client.address + "), Protocol " + serverReport.protocolVersion + ", Program Version: " + serverReport.programVersion + ", Time: " + sw.ElapsedMilliseconds);
        }
    }
}

