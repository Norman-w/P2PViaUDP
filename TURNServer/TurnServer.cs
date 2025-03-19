// 修改TURN服务器代码

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using P2PViaUDP;
using P2PViaUDP.Model;
using P2PViaUDP.Model.TURN;

namespace TURNServer;

public class TurnServer
{
	private readonly UdpClient _udpServer;
	private readonly ConcurrentDictionary<Guid, List<TURNClient>> _groupDict;
	private readonly Settings _settings;

	public TurnServer(Settings settings)
	{
		_settings = settings;
		_groupDict = new ConcurrentDictionary<Guid, List<TURNClient>>();
		// 只指定端口，监听所有IP
		_udpServer = new UdpClient(settings.TURNServerPort);

		// 添加测试组
		_groupDict.TryAdd(Guid.Parse("00000000-0000-0000-0000-000000000001"),
			new List<TURNClient>());
	}

	public async Task StartAsync()
	{
		Console.WriteLine($"TURN服务器启动在端口: {_settings.TURNServerPort}");

		while (true)
		{
			try
			{
				var result = await _udpServer.ReceiveAsync();
				ProcessMessage(result.Buffer, result.RemoteEndPoint);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"TURN服务器错误: {ex.Message}");
			}
		}
	}

	private void ProcessMessage(byte[] data, IPEndPoint remoteEndPoint)
	{
		try
		{
			Console.WriteLine($"收到数据 来自: {remoteEndPoint}");
			Console.WriteLine($"数据长度: {data.Length}");
			Console.WriteLine($"原始数据: {BitConverter.ToString(data)}");

			var message = TURNRegisterMessage.FromBytes(data);

			Console.WriteLine($"解析后消息:");
			Console.WriteLine($"Guid: {message.Guid}");
			Console.WriteLine($"EndPoint: {message.EndPoint}");
			Console.WriteLine($"GroupGuid: {message.GroupGuid}");

			if (_groupDict.TryGetValue(message.GroupGuid, out var group))
			{
				var newClient = new TURNClient
				{
					EndPointFromTURN = remoteEndPoint,
					Guid = message.Guid, NATType = message.DetectedNATType ?? NATTypeEnum.Unknown
				};
				group.Add(newClient);

				Console.WriteLine($"客户端 {message.Guid} 已加入组 {message.GroupGuid}");
				BroadcastToGroup(message, group);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"处理消息时出错: {ex}");
		}
	}

	private void BroadcastToGroup(TURNRegisterMessage newClient, List<TURNClient> group)
	{
		/*
		 
		 
		 1 2 3 4 5 6 
		 当前的是6, 循环到1的时候,1是早期加入的客户端,6是最新加入的客户端
		 向1发送6新加入的消息,向2发送6新加入的消息,向3发送6新加入的消息,向4发送6新加入的消息,向5发送6新加入的消息
		 向6发送1早期加入的消息,向6发送2早期加入的消息,向6发送3早期加入的消息,向6发送4早期加入的消息,向6发送5早期加入的消息
		
		
		
*/
		Console.WriteLine($"向组内其他客户端广播新客户端 {newClient.Guid}, 共 {group.Count - 1} 个");
		var thisNewClient = group.First(c => c.Guid == newClient.Guid);
		var groupOtherClientsWithoutThisNewClient = group.Where(c => c.Guid != newClient.Guid).ToList();
		foreach (var existInGroupEarlierClient in groupOtherClientsWithoutThisNewClient)
		{
			try
			{
				if(!NeedContinueSendHolePunchingMessage(existInGroupEarlierClient, thisNewClient, true))
				{
					continue;
				}
				var broadcast = new TURNBroadcastMessage
				{
					//TODO 这里只是测试,实际上应该发送消息的时候,先决定了让对方客户端用哪个端口再发送,现在只测试全锥的情况
					EndPoint = existInGroupEarlierClient.EndPointFromTURN,
					Guid = existInGroupEarlierClient.Guid,
					GroupGuid = newClient.GroupGuid
				};
				var data = broadcast.ToBytes();
				_udpServer.Send(data, data.Length, existInGroupEarlierClient.EndPointFromTURN);
				Console.WriteLine($"广播已发送到 {existInGroupEarlierClient.Guid} 经由 {existInGroupEarlierClient.EndPointFromTURN}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"广播失败: {ex.Message}");
			}
		}
		// 向这个新的客户端发送其他客户端的信息
		Console.WriteLine($"向新客户端 {thisNewClient.Guid} 发送其他客户端信息, 共 {groupOtherClientsWithoutThisNewClient.Count} 个");
		foreach (var existInGroupEarlierClient in groupOtherClientsWithoutThisNewClient)
		{
			try
			{
				if(!NeedContinueSendHolePunchingMessage(existInGroupEarlierClient, thisNewClient, false))
				{
					continue;
				}
				var broadcast = new TURNBroadcastMessage
				{
					//TODO 这里只是测试,实际上应该发送消息的时候,先决定了让对方客户端用哪个端口再发送,现在只测试全锥的情况
					EndPoint = thisNewClient.EndPointFromTURN,
					Guid = thisNewClient.Guid,
					GroupGuid = newClient.GroupGuid
				};
				var data = broadcast.ToBytes();
				_udpServer.Send(data, data.Length, thisNewClient.EndPointFromTURN);
				Console.WriteLine($"广播已发送到 {thisNewClient.Guid}, 经由 {thisNewClient.EndPointFromTURN}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"广播失败: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 检查是否需要继续发送广播消息让客户端触发打洞.
	/// 如果是正在发送广播给早期加入的客户端,且主动方应该是后面加入这个,则不用继续发送广播,返回false
	/// </summary>
	/// <param name="earlierPair"></param>
	/// <param name="laterPair"></param>
	/// <param name="isSendingToEarlierPair"></param>
	/// <returns></returns>
	private static bool NeedContinueSendHolePunchingMessage(TURNClient earlierPair, TURNClient laterPair,
		bool isSendingToEarlierPair)
	{
		var decideResult = DecideWhichIsActiveAndWhichIsPassiveWhenHolePunching(
			earlierPair,
			laterPair,
			out var isBothNeedPassiveDoHolePunching,
			out var active,
			out var passive,
			out var errorMessage);

		#region 输出决定后的结果

		if (!decideResult)
		{
			Console.WriteLine($"决定打洞的主动和被动时出错: {errorMessage}");
			return false;
		}
		else if (isBothNeedPassiveDoHolePunching)
		{
			Console.WriteLine($"两个都需要主动发起打洞,先后加入的两个客户端类型分别是 {earlierPair.NATType} 和 {laterPair.NATType}");
		}
		else if (active is null || passive is null)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(
				$"决策好像失败了,bug?主动和被动客户端有一个为null不能打洞 ,先后加入的两个客户端类型分别是 {earlierPair.NATType} 和 {laterPair.NATType}");
			Console.ResetColor();
			return false;
		}
		else if (active.Guid == laterPair.Guid)
		{
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.WriteLine($"新加入的客户端是主动客户端,类型是 {laterPair.NATType}");
			Console.ResetColor();
		}
		else if (passive.Guid == laterPair.Guid)
		{
			Console.ForegroundColor = ConsoleColor.Magenta;
			Console.WriteLine($"新加入的客户端是被动客户端,类型是 {laterPair.NATType}");
			Console.ResetColor();
		}
		else
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"主动客户端是 {active.Guid}, 被动客户端是 {passive.Guid}");
			Console.ResetColor();
		}

		#endregion

		if (isSendingToEarlierPair)
		{
			return active!.Guid == earlierPair.Guid;
		}
		else
		{
			return active!.Guid == laterPair.Guid;
		}
	}

	/// <summary>
	/// 决定在打洞的时候哪个是被动的,哪个是主动的
	/// 如果isBothNeedPassiveDoHolePunching返回的是true,则两个都要同时主动发起打洞,passive和active都是null
	/// 否则,passive是被动的,active是主动的
	/// </summary>
	/// <param name="earlierPair">早些存在于组内的客户端</param>
	/// <param name="laterPair">后加入的客户端</param>
	/// <param name="isBothNeedPassiveDoHolePunching">是否两个都需要主动发起打洞</param>
	/// <param name="active">主动的客户端</param>
	/// <param name="passive">被动的客户端</param>
	/// <param name="errorMessage">当出现错误时的错误信息</param>
	/// <returns>是否出现错误</returns>
	private static bool DecideWhichIsActiveAndWhichIsPassiveWhenHolePunching(
		TURNClient earlierPair, TURNClient laterPair,
		out bool isBothNeedPassiveDoHolePunching,
		out TURNClient? active, out TURNClient? passive,
		out string errorMessage)
	{
		/*
	 打洞顺序说明:
	 当两端是
	 全锥形 <-> IP受限/端口受限/对称形
		全锥形作为"服务器",另一端作为"客户端",主动连接消息由"客户端"发起
	 全锥形 <-> 全锥形
		后加入的一端作为"服务器",另一端作为"客户端",主动连接消息由"客户端"发起, 这样做是为了减轻先加入的一端的负担
	 IP受限/端口受限 <-> IP受限/端口受限
		两端都作为"服务器",主动连接消息由另外一方发起,具体双方要猜测的对方端口号是什么,需要TURN服务器根据对方的公网IP和端口号来猜测
	 对称形 <-> IP受限/端口受限 ⏳尚未验证下方观点
		由于对称型发出去的消息需要原路返回,也要求返回方从原路返回,而IP受限/端口受限的NAT设备不会原路返回,每次都会更换端口号,所以这种情况下是无法打洞成功的
	 对称型 <-> 对称型
		无法打洞,使用TURN服务器中继
	 */
		if (earlierPair.NATType == NATTypeEnum.FullCone)
		{
			if (laterPair.NATType == NATTypeEnum.FullCone)
			{
				// 全锥形 <-> 全锥形
				active = earlierPair;
				passive = laterPair;
			}
			else
			{
				// 全锥形 <-> IP受限/端口受限/对称形
				active = laterPair;
				passive = earlierPair;
			}
			isBothNeedPassiveDoHolePunching = false;
			errorMessage = string.Empty;
			return true;
		}

		if (earlierPair.NATType is NATTypeEnum.RestrictedCone or NATTypeEnum.PortRestrictedCone)
		{
			if (laterPair.NATType is NATTypeEnum.RestrictedCone or NATTypeEnum.PortRestrictedCone)
			{
				// IP受限/端口受限 <-> IP受限/端口受限
				isBothNeedPassiveDoHolePunching = true;
				active = null;
				passive = null;
				errorMessage = string.Empty;
				return true;
			}
			else
			{
				// 对称形 <-> IP受限/端口受限
				isBothNeedPassiveDoHolePunching = false;
				active = earlierPair;
				passive = laterPair;
				errorMessage = string.Empty;
				return true;
			}
		}
		else
		{
			// 对称形 <-> 对称形
			isBothNeedPassiveDoHolePunching = true;
			active = null;
			passive = null;
			errorMessage = "对称形之间无法打洞";
			return false;
		}
	}
}