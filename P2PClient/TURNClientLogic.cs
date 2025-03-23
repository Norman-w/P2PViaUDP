using System.Net;
using System.Net.Sockets;
using P2PViaUDP.Model.TURN;
using TURNServer;

namespace P2PClient;

public static class TURNClientLogic
{
	#region TURN 流程控制

	#region 注册到TURN服务器

	public static async Task RegisterToTurnServerAsync(P2PClientConfig settings, IPEndPoint myEndPointFromMainStunMainPortReply, Guid clientId, NATTypeEnum myNATType, UdpClient udpClient)
	{
		try
		{
			//如果配置的TURN服务器IP不是IP格式的话要解析成IP
			var domain = settings.TURNServerIP;
			if (!IPAddress.TryParse(domain, out var _))
			{
				var ip = await Dns.GetHostAddressesAsync(domain);
				settings.TURNServerIP = ip[0].ToString();
			}

			if (myEndPointFromMainStunMainPortReply == null)
			{
				throw new Exception("STUN响应为空");
			}

			var registerMessage = new TURNRegisterMessage
			{
				EndPoint = myEndPointFromMainStunMainPortReply,
				Guid = clientId,
				GroupGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
				DetectedNATType = myNATType
			};

			var turnServerEndPoint = new IPEndPoint(
				IPAddress.Parse(settings.TURNServerIP),
				settings.TURNServerPrimaryPort
			);

			Console.WriteLine($"正在向TURN服务器注册: {turnServerEndPoint}");
			Console.WriteLine($"本地终端点: {myEndPointFromMainStunMainPortReply}");

			var registerBytes = registerMessage.ToBytes();
			Console.WriteLine($"发送数据大小: {registerBytes.Length}");

			await udpClient.SendAsync(registerBytes, registerBytes.Length, turnServerEndPoint);
			Console.WriteLine("TURN注册消息已发送");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"TURN注册失败: {ex}");
			throw;
		}
	}

	#endregion

	#endregion
}