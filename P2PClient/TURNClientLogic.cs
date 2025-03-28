using System.Net;
using System.Net.Sockets;
using P2PViaUDP.Model;
using P2PViaUDP.Model.TURN;
using TURNServer;

namespace P2PClient;

public static class TURNClientLogic
{
	#region TURN 流程控制

	#region 注册到TURN服务器

	public static async Task RegisterToTurnServerAsync(P2PClientConfig settings, IPEndPoint myEndPointFromMainStunSecondaryPortReply, Guid clientId, NATTypeEnum myNATType, UdpClient udpClient)
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

			if (myEndPointFromMainStunSecondaryPortReply == null)
			{
				throw new Exception("STUN响应为空");
			}

			var registerMessage = new TURNRegisterMessage
			{
				EndPoint = myEndPointFromMainStunSecondaryPortReply,
				Guid = clientId,
				GroupGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
				DetectedNATType = myNATType
			};

			var turnServerEndPoint = new IPEndPoint(
				IPAddress.Parse(settings.TURNServerIP),
				settings.TURNServerPrimaryPort
			);

			Console.WriteLine($"正在向TURN服务器注册: {turnServerEndPoint}");
			Console.WriteLine($"本地终端点: {myEndPointFromMainStunSecondaryPortReply}");

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

	public static void ProcessReceivedMessage(byte[] data)
	{
		var message = (MessageType)BitConverter.ToInt32(data, 0);
		switch (message)
		{
			case MessageType.StunRequest:
			case MessageType.StunNATTypeCheckingRequest:
			case MessageType.StunNATTypeCheckingResponse:
			case MessageType.StunResponse:
			case MessageType.StunResponseError:
				break;
			case MessageType.TURNBroadcast:
				break;
			case MessageType.TURNRegister:
			case MessageType.TURNServer2ClientHeartbeat:
			case MessageType.TURNClient2ServerHeartbeat:
				break;
			case MessageType.P2PHolePunchingRequest:
			case MessageType.P2PHolePunchingResponse:
			case MessageType.P2PHeartbeat:
			default:
				throw new ArgumentOutOfRangeException();
		}
	}
}