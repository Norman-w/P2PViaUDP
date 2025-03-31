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

	#region 检查NAT一致性

	public static async Task CheckNATConsistencyAsync(P2PClientConfig settings, IPEndPoint myEndPoint, Guid clientId, UdpClient udpClient)
	{
		try
		{
			var checkMessage = new byte[28]; // 4 + 16 + 4 + 4
			BitConverter.GetBytes((int)MessageType.TURNCheckNATConsistencyRequest).CopyTo(checkMessage, 0);
			clientId.ToByteArray().CopyTo(checkMessage, 4);
			myEndPoint.Address.GetAddressBytes().CopyTo(checkMessage, 20);
			BitConverter.GetBytes(myEndPoint.Port).CopyTo(checkMessage, 24);

			var turnServerEndPoint = new IPEndPoint(
				IPAddress.Parse(settings.TURNServerIP),
				settings.NATTypeConsistencyKeepingCheckingPortTURN
			);

			Console.WriteLine($"正在检查NAT一致性: {turnServerEndPoint}");
			Console.WriteLine($"本地终端点: {myEndPoint}");

			await udpClient.SendAsync(checkMessage, checkMessage.Length, turnServerEndPoint);
			Console.WriteLine("NAT一致性检查请求已发送");

			// 等待响应
			var responseReceived = false;
			var timeout = DateTime.Now.AddSeconds(5); // 5秒超时
			
			while (!responseReceived && DateTime.Now < timeout)
			{
				if (udpClient.Available > 0)
				{
					var result = await udpClient.ReceiveAsync();
					var responseMessageType = (MessageType)BitConverter.ToInt32(result.Buffer, 0);
					
					if (responseMessageType == MessageType.TURNCheckNATConsistencyResponse)
					{
						var responseEndPoint = new IPEndPoint(
							new IPAddress(result.Buffer.Skip(20).Take(4).ToArray()),
							BitConverter.ToInt32(result.Buffer, 24)
						);

						if (responseEndPoint.Equals(myEndPoint))
						{
							Console.ForegroundColor = ConsoleColor.Green;
							Console.WriteLine($"【NAT一致性检查通过】: 当前外网地址 {result.RemoteEndPoint} 与注册时地址 {myEndPoint} 一致");
							Console.ResetColor();
						}
						else
						{
							Console.ForegroundColor = ConsoleColor.Red;
							Console.WriteLine($"【NAT一致性检查失败】: 当前外网地址 {result.RemoteEndPoint} 与注册时地址 {myEndPoint} 不一致");
							Console.WriteLine($"注册时的地址: {myEndPoint}, 当前地址: {result.RemoteEndPoint}");
							Console.ResetColor();
						}
						responseReceived = true;
					}
				}
				await Task.Delay(100); // 等待一段时间再检查是否有响应
			}
			
			if (!responseReceived)
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("【NAT一致性检查超时】: 未收到TURN服务器响应");
				Console.ResetColor();
			}
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"【NAT一致性检查失败】: {ex.Message}");
			Console.ResetColor();
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
				break;
			case MessageType.TURNCheckNATConsistencyRequest:
			case MessageType.TURNCheckNATConsistencyResponse:
				break;
			default:
				throw new ArgumentOutOfRangeException();
		}
	}
}