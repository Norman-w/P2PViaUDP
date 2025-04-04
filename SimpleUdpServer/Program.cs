using System.Net;
using System.Net.Sockets;
using System.Text;

// 创建UdpClient实例并绑定到所有IP地址上
const int port = 3488; // 替换为需要监听的端口号
using var udpServer = new UdpClient(new IPEndPoint(IPAddress.Any, port));

// 输出服务启动日志
Console.WriteLine($"UDP服务器已启动，正在监听端口 {port}...");
Console.WriteLine("按 Ctrl+C 退出程序");

// 使用Task.Run启用用户输入逻辑的后台任务
Task.Run(UserInputClientTest);

// 处理Ctrl+C事件，在退出时清理资源
Console.CancelKeyPress += (_, e) =>
{
	e.Cancel = true; // 防止程序立即退出
	Console.WriteLine("正在关闭服务器...");
	udpServer.Close();
	Console.WriteLine("服务器已关闭");
	Environment.Exit(0); // 释放资源并退出
};

// 持续监听消息
while (true)
{
	try
	{
		var result = await udpServer.ReceiveAsync(); // 异步接收消息

		// 收到的消息内容
		var message = Encoding.UTF8.GetString(result.Buffer);

		// 打印下行日志
		Console.WriteLine($"↓ 收到来自 {result.RemoteEndPoint} 的消息: {message}");

		// 服务器回应消息
		var serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		var responseMessage = $"我收到了你的客户端时间: {message}, 我的服务器时间是: {serverTime}";

		var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
		await udpServer.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);

		// 打印上行日志
		Console.WriteLine($"↑ 发送给 {result.RemoteEndPoint} 的消息: {responseMessage}");
	}
	catch (SocketException ex)
	{
		Console.WriteLine($"网络错误: {ex.Message}");
		break;
	}
	catch (ObjectDisposedException)
	{
		// 服务器已关闭
		break;
	}
	catch (Exception ex)
	{
		Console.WriteLine($"监听时发生错误: {ex.Message}");
	}
}

// 用户输入逻辑，执行客户端测试发送任务
async Task UserInputClientTest()
{
	while (true)
	{
		try
		{
			// 提示用户输入目标地址
			Console.WriteLine("请输入要通讯测试的目标地址和端口 (格式: 127.0.0.1:3488):");
			var input = Console.ReadLine();

			if (string.IsNullOrEmpty(input)) continue;

			// 解析用户输入的地址和端口
			var split = input.Split(':');
			if (split.Length != 2 || !IPAddress.TryParse(split[0], out var ip) ||
			    !int.TryParse(split[1], out var targetPort))
			{
				Console.WriteLine("输入格式不正确，请重新输入 (格式: 127.0.0.1:3488)...");
				continue;
			}

			// 创建与目标通讯的UdpClient
			using var udpClient = new UdpClient();

			// 构造目标地址
			var targetEndPoint = new IPEndPoint(ip, targetPort);
			Console.WriteLine($"已设定目标地址 {targetEndPoint}，将开始发送消息");

			// 持续发送消息
			while (true)
			{
				// 每隔1秒发送一次当前时间
				var clientTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
				var message = $"客户端时间: {clientTime}";

				var messageBytes = Encoding.UTF8.GetBytes(message);
				await udpClient.SendAsync(messageBytes, messageBytes.Length, targetEndPoint);

				// 打印上行日志
				Console.WriteLine($"↑ 已发送到 {targetEndPoint} 的消息: {message}");

				// 尝试接收服务器回应
				try
				{
					udpClient.Client.ReceiveTimeout = 2000; // 设置2秒超时
					var receiveResult = await udpClient.ReceiveAsync();
					var responseMessage = Encoding.UTF8.GetString(receiveResult.Buffer);

					// 打印下行日志
					Console.WriteLine($"↓ 收到来自 {receiveResult.RemoteEndPoint} 的回应: {responseMessage}");
				}
				catch (SocketException ex)
				{
					Console.WriteLine($"接收服务器响应失败(超时或其他错误): {ex.Message}");
				}

				// 等待1秒后继续发送消息
				await Task.Delay(1000);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"用户测试通讯时发生错误: {ex.Message}");
		}
	}
}