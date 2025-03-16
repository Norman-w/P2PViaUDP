// Program.cs

using P2PViaUDP;
using P2PViaUDP.Model;
using TURNServer;

var settings = new Settings();
var turnServer = new TurnServer(settings);
await turnServer.StartAsync();