// 修改TURN服务器代码

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using P2PViaUDP.Model;
using P2PViaUDP.Model.TURN;

namespace TURNServer;

public class TurnServer
{
	private readonly UdpClient _udpServer;
	private readonly UdpClient _natTypeConsistencyKeepingCheckingServer;
	private readonly ConcurrentDictionary<Guid, List<TURNClient>> _groupDict;
	private readonly TURNServerConfig _settings;
	private bool _isRunning;
	/// <summary>
	/// 客户端超时时间
	/// </summary>
	private const int CLIENT_TIMEOUT_SECONDS = 30;
	/// <summary>
	/// 用于控制异步操作的取消令牌,在服务器关闭时取消所有操作
	/// </summary>
	private readonly CancellationTokenSource _cts = new();

	public TurnServer(TURNServerConfig settings)
	{
		_settings = settings;
		_groupDict = new ConcurrentDictionary<Guid, List<TURNClient>>();
		// 只指定端口，监听所有IP
		_udpServer = new UdpClient(settings.MainPort);
		_natTypeConsistencyKeepingCheckingServer = new UdpClient(settings.NATTypeConsistencyKeepingCheckingPort);

		// 添加测试组
		_groupDict.TryAdd(Guid.Parse("00000000-0000-0000-0000-000000000001"),
			new List<TURNClient>());
		
		_ = StartCleanupTaskAsync();
	}

	private async Task StartCleanupTaskAsync()
	{
		while (!_cts.Token.IsCancellationRequested)
		{
			//每隔一段时间检查客户端的活动状态
			await Task.Delay(TimeSpan.FromSeconds(CLIENT_TIMEOUT_SECONDS), _cts.Token);
			CleanupInactiveClients();
		}
	}

	private void CleanupInactiveClients()
	{
		var now = DateTime.UtcNow;
		foreach (var (groupGuid, clients) in _groupDict)
		{
			// 清理不活跃的客户端
			clients.RemoveAll(client => (now - client.LastActivityTime).TotalSeconds > CLIENT_TIMEOUT_SECONDS);
			if (clients.Count == 0)
			{
				// 如果组内没有客户端了，告知组已经清空,我们先暂时不把组删掉
				//TODO 后面会增加组如果不存在了(房间没人太久房间也没了)的逻辑
				Console.WriteLine($"组 {groupGuid} 已经清空");
			}
			else
			{
				Console.WriteLine($"组 {groupGuid} 仍然存在, 当前客户端数量: {clients.Count}");
			}
		}
	}

	/// <summary>
	/// 更新客户端的活动时间
	/// </summary>
	/// <param name="clientId"></param>
	private void UpdateClientActivity(Guid clientId)
	{
		foreach (var group in _groupDict.Values)
		{
			var client = group.FirstOrDefault(c=>c.Guid == clientId);
			if (client == null) continue;
			client.LastActivityTime = DateTime.UtcNow;
			break;
		}
	}

	public async Task StartAsync()
	{
		if (_isRunning)
		{
			Console.WriteLine("TURN服务器已经在运行中");
			return;
		}

		_isRunning = true;
		Console.WriteLine($"TURN服务器启动在端口: {_settings.MainPort}");
		Console.WriteLine($"NAT一致性检查服务器启动在端口: {_settings.NATTypeConsistencyKeepingCheckingPort}");

		// 启动两个接收任务
		var mainTask = Task.Run(ReceiveMainServerMessagesAsync);
		var natCheckTask = Task.Run(ReceiveNATConsistencyCheckMessagesAsync);

		// 等待任务完成（实际上这两个任务应该不会自然结束，除非抛出异常或主动停止）
		var tasks = new[] { mainTask, natCheckTask };
		await Task.WhenAll(tasks);

		// 添加额外的阻塞机制，防止服务器退出
		Console.WriteLine("服务器已启动，按任意键停止服务...");
		Console.ReadKey();
	}

	private async Task ReceiveMainServerMessagesAsync()
	{
		while (_isRunning)
		{
			try
			{
				var result = await _udpServer.ReceiveAsync();
				ProcessTURNRegisterMessage(result.Buffer, result.RemoteEndPoint);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"TURN服务器错误: {ex.Message}");
			}
		}
	}

	private async Task ReceiveNATConsistencyCheckMessagesAsync()
	{
		while (_isRunning)
		{
			try
			{
				var result = await _natTypeConsistencyKeepingCheckingServer.ReceiveAsync();
				ProcessNATConsistencyCheckMessage(result.Buffer, result.RemoteEndPoint);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"NAT一致性检查服务器错误: {ex.Message}");
			}
		}
	}

	private void ProcessNATConsistencyCheckMessage(byte[] data, IPEndPoint remoteEndPoint)
	{
		try
		{
			var messageType = (MessageType)BitConverter.ToInt32(data, 0);
			if (messageType != MessageType.TURNCheckNATConsistencyRequest)
			{
				Console.WriteLine($"收到未知消息类型: {messageType}");
				return;
			}

			var request = TURNCheckNATConsistencyRequest.FromBytes(data);
			
			UpdateClientActivity(request.ClientId);

			Console.WriteLine($"收到NAT一致性检查请求 来自: {remoteEndPoint}");

			// 发送响应
			var response = new TURNCheckNATConsistencyResponse
			{
				ClientId = request.ClientId,
				EndPoint = remoteEndPoint
			};
			var bytes = response.ToBytes();
			_natTypeConsistencyKeepingCheckingServer.Send(bytes, bytes.Length, remoteEndPoint);

			Console.WriteLine($"已发送NAT一致性检查响应到: {remoteEndPoint}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"处理NAT一致性检查消息时出错: {ex}");
		}
	}

	private void ProcessTURNRegisterMessage(byte[] data, IPEndPoint remoteEndPoint)
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
			
			UpdateClientActivity(message.Guid);

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
		Console.WriteLine($"向组内其他早期已经存在的客户端广播新客户端 {newClient.Guid}, 共 {group.Count - 1} 个");
		var thisNewClient = group.First(c => c.Guid == newClient.Guid);
		var groupOtherClientsWithoutThisNewClient = group.Where(c => c.Guid != newClient.Guid).ToList();
		foreach (var existInGroupEarlierClient in groupOtherClientsWithoutThisNewClient)
		{
			try
			{
				if (!DecideWhichIsActiveAndWhichIsPassiveWhenHolePunching(
					    existInGroupEarlierClient,
					    thisNewClient,
					    out var active,
					    out _,
					    out var errorMessage))
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine(errorMessage);
					Console.ResetColor();
					continue;
				}

				var broadcast = new TURNBroadcastMessage
				{
					ClientSideEndPointToTURN = thisNewClient.EndPointFromTURN,
					Guid = thisNewClient.Guid,
					IsNeedPrepareAcceptIncomingConnectionForThisClient = active == existInGroupEarlierClient,
					IsNeedWaitForPrepareAcceptIncomingConnectionForThisClient = active != existInGroupEarlierClient,
					GroupGuid = newClient.GroupGuid,
					IsNeedHolePunchingToThisClient = true,
					IsFullConeDetected = thisNewClient.NATType == NATTypeEnum.FullCone
				};
				var data = broadcast.ToBytes();
				_udpServer.Send(data, data.Length, existInGroupEarlierClient.EndPointFromTURN);
				Console.WriteLine(
					$"广播已发送到 {existInGroupEarlierClient.Guid} 经由 {existInGroupEarlierClient.EndPointFromTURN}");
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
				if (!DecideWhichIsActiveAndWhichIsPassiveWhenHolePunching(
					    existInGroupEarlierClient,
					    thisNewClient,
					    out var active,
					    out _,
					    out var errorMessage))
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine(errorMessage);
					Console.ResetColor();
					continue;
				}

				var isNeedPrepareAcceptIncomingConnectionForThisClient = active == thisNewClient;
				var isNeedWaitForPrepareAcceptIncomingConnectionForThisClient =
					!isNeedPrepareAcceptIncomingConnectionForThisClient;
				var broadcast = new TURNBroadcastMessage
				{
					ClientSideEndPointToTURN = existInGroupEarlierClient.EndPointFromTURN,
					Guid = existInGroupEarlierClient.Guid,
					IsNeedPrepareAcceptIncomingConnectionForThisClient =
						isNeedPrepareAcceptIncomingConnectionForThisClient,
					IsNeedWaitForPrepareAcceptIncomingConnectionForThisClient =
						isNeedWaitForPrepareAcceptIncomingConnectionForThisClient,
					GroupGuid = newClient.GroupGuid,
					IsNeedHolePunchingToThisClient = true,
					IsFullConeDetected = existInGroupEarlierClient.NATType == NATTypeEnum.FullCone
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
	/// 决定在打洞的时候哪个是被动的,哪个是主动的
	/// 主动就是说"来搞我啊"的那一端,被动就是"好嘞"的那一端
	/// </summary>
	/// <param name="earlierPair">早些存在于组内的客户端</param>
	/// <param name="laterPair">后加入的客户端</param>
	/// <param name="active">主动的客户端(先出手的)</param>
	/// <param name="passive">被动的客户端(后出手的)</param>
	/// <param name="errorMessage">当出现错误时的错误信息</param>
	/// <exception cref="ArgumentOutOfRangeException"></exception>
	/// <returns>是否出现错误</returns>
	private static bool DecideWhichIsActiveAndWhichIsPassiveWhenHolePunching(
		TURNClient earlierPair, TURNClient laterPair,
		out TURNClient? active, out TURNClient? passive,
		out string errorMessage)
	{
		TURNClient? fullConePair = null, fullConePairAnOther = null;
		TURNClient? restrictedConePair = null, restrictedConePairAnOther = null;
		TURNClient? portRestrictedConePair = null, portRestrictedConePairAnOther = null;
		TURNClient? symmetricPair = null, symmetricPairAnOther = null;
		switch (laterPair.NATType)
		{
			case NATTypeEnum.Unknown:
				Console.WriteLine($"未知的NAT类型: 先加入的客户端 {earlierPair.NATType}, 后加入的客户端 {laterPair.NATType}");
				break;
			case NATTypeEnum.FullCone:
				fullConePair = laterPair;
				break;
			case NATTypeEnum.RestrictedCone:
				restrictedConePair = laterPair;
				break;
			case NATTypeEnum.PortRestrictedCone:
				portRestrictedConePair = laterPair;
				break;
			case NATTypeEnum.Symmetric:
				symmetricPair = laterPair;
				break;
			default:
				throw new ArgumentOutOfRangeException($"在决定主动和被动时出现了未知的NAT类型: {laterPair.NATType}");
		}

		switch (earlierPair.NATType)
		{
			case NATTypeEnum.Unknown:
				Console.WriteLine($"未知的NAT类型: 先加入的客户端 {earlierPair.NATType}, 后加入的客户端 {laterPair.NATType}");
				break;
			case NATTypeEnum.FullCone:
				if (fullConePair != null)
				{
					fullConePairAnOther = earlierPair;
				}
				else
				{
					fullConePair = earlierPair;
				}

				break;
			case NATTypeEnum.RestrictedCone:
				if (restrictedConePair != null)
				{
					restrictedConePairAnOther = earlierPair;
				}
				else
				{
					restrictedConePair = earlierPair;
				}

				break;
			case NATTypeEnum.PortRestrictedCone:
				if (portRestrictedConePair != null)
				{
					portRestrictedConePairAnOther = earlierPair;
				}
				else
				{
					portRestrictedConePair = earlierPair;
				}

				break;
			case NATTypeEnum.Symmetric:
				if (symmetricPair != null)
				{
					symmetricPairAnOther = earlierPair;
				}
				else
				{
					symmetricPair = earlierPair;
				}

				break;
			default:
				throw new ArgumentOutOfRangeException($"在决定主动和被动时出现了未知的NAT类型: {earlierPair.NATType}");
		}

		if (fullConePair != null && fullConePairAnOther != null)
		{
			// 全锥形 <-> 全锥形
			active = fullConePair;
			passive = fullConePairAnOther;
			errorMessage = string.Empty;
			return true;
		}

		if (fullConePair != null && restrictedConePair != null)
		{
			// 全锥形 <-> IP受限
			active = fullConePair;
			passive = restrictedConePair;
			errorMessage = string.Empty;
			return true;
		}

		if (fullConePair != null && portRestrictedConePair != null)
		{
			// 全锥形 <-> 端口受限
			active = fullConePair;
			passive = portRestrictedConePair;
			errorMessage = string.Empty;
			return true;
		}

		if (fullConePair != null && symmetricPair != null)
		{
			// 全锥形 <-> 对称形
			active = fullConePair;
			passive = symmetricPair;
			errorMessage = string.Empty;
			return true;
		}

		if (restrictedConePair != null && restrictedConePairAnOther != null)
		{
			// IP受限 <-> IP受限
			active = restrictedConePair;
			passive = restrictedConePairAnOther;
			errorMessage = string.Empty;
			return true;
		}

		if (restrictedConePair != null && portRestrictedConePair != null)
		{
			// IP受限 <-> 端口受限
			active = restrictedConePair;
			passive = portRestrictedConePair;
			errorMessage = string.Empty;
			return true;
		}

		if (restrictedConePair != null && symmetricPair != null)
		{
			// IP受限 <-> 对称形
			active = restrictedConePair;
			passive = symmetricPair;
			errorMessage = string.Empty;
			return true;
		}

		if (portRestrictedConePair != null && portRestrictedConePairAnOther != null)
		{
			// 端口受限 <-> 端口受限
			active = portRestrictedConePairAnOther;
			passive = portRestrictedConePair;
			errorMessage = string.Empty;
			return true;
		}

		if (portRestrictedConePair != null && symmetricPair != null)
		{
			// 端口受限 <-> 对称形
			active = null;
			passive = null;
			errorMessage =
				"端口受限型和对称形之间的打洞, 没有办法进行,因为虽然端口受限型的端口虽然不会变化,但是必须要端口受限型先发送链接到对方(确切的端口)然后对方才能通过这个端口返回数据,但是对称型的端口一般又没法预测,随机性很高,所以没法打洞)";
			Console.WriteLine(errorMessage);
			return false;
		}

		if (symmetricPair != null && symmetricPairAnOther != null)
		{
			// 对称形 <-> 对称形
			active = null;
			passive = null;
			errorMessage = "对称形之间无法打洞";
			Console.WriteLine(errorMessage);
			return false;
		}
		else
		{
			var message = $"未知的NAT类型: 先加入的客户端 {earlierPair.NATType}, 后加入的客户端 {laterPair.NATType}";
			Console.WriteLine(message);
			// 对称形 <-> 对称形
			active = null;
			passive = null;
			errorMessage = message;
			return false;
		}
	}
}