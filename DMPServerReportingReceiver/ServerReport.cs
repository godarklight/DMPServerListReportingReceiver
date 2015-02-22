using System;
using System.Collections.Generic;

namespace DMPServerReportingReceiver
{
    public class ServerReport
    {
        public string serverHash;
        public string serverName;
        public string description;
        public int gamePort;
        public string gameAddress;
        public int protocolVersion;
        public string programVersion;
        public int maxPlayers;
        public int modControl;
        public string modControlSha;
        public int gameMode;
        public bool cheats;
        public int warpMode;
        public long universeSize;
        public string banner;
        public string homepage;
        public int httpPort;
        public string admin;
        public string team;
        public string location;
        public bool fixedIP;
        public string[] players;

        public Dictionary<string, object> GetParameters()
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            parameters["@serverhash"] = serverHash;
            parameters["@namex"] = serverName;
            if (serverName.Length > 255)
            {
                serverName = serverName.Substring(0, 255);
            }
            parameters["@descriptionx"] = description;
            parameters["@gameportx"] = gamePort;
            parameters["@gameaddressx"] = gameAddress;
            parameters["@protocolx"] = protocolVersion;
            parameters["@programversion"] = programVersion;
            parameters["@maxplayersx"] = maxPlayers;
            parameters["@modcontrolx"] = modControl;
            parameters["@modcontrolshax"] = modControlSha;
            parameters["@gamemodex"] = gameMode;
            parameters["@cheatsx"] = cheats;
            parameters["@warpmodex"] = warpMode;
            parameters["@universex"] = universeSize;
            parameters["@bannerx"] = banner;
            parameters["@homepagex"] = homepage;
            parameters["@httpportx"] = httpPort;
            parameters["@adminx"] = admin;
            parameters["@teamx"] = team;
            parameters["@locationx"] = location;
            parameters["@fixedipx"] = fixedIP;
            return parameters;
        }

        public byte[] GetBytes()
        {
            byte[] retVal = null;
            using (MessageStream2.MessageWriter mw = new MessageStream2.MessageWriter())
            {
                mw.Write<string>(serverHash);
                mw.Write<string>(serverName);
                mw.Write<string>(description);
                mw.Write<int>(gamePort);
                mw.Write<string>(gameAddress);
                mw.Write<int>(protocolVersion);
                mw.Write<string>(programVersion);
                mw.Write<int>(maxPlayers);
                mw.Write<int>(modControl);
                mw.Write<string>(modControlSha);
                mw.Write<int>(gameMode);
                mw.Write<bool>(cheats);
                mw.Write<int>(warpMode);
                mw.Write<long>(universeSize);
                mw.Write<string>(banner);
                mw.Write<string>(homepage);
                mw.Write<int>(httpPort);
                mw.Write<string>(admin);
                mw.Write<string>(team);
                mw.Write<string>(location);
                mw.Write<bool>(fixedIP);
                mw.Write<string[]>(players);
                retVal = mw.GetMessageBytes();
            }
            return retVal;
        }
    }
}

