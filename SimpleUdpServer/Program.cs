using System.Net.Sockets;
using System.Text;


// 创建UdpClient实例并绑定端口
const int port = 3478; // 替换为你需要监听的端口号
using var udpServer = new UdpClient(port);

// 输出服务启动日志
Console.WriteLine($"UDP服务器已启动，正在监听端口 {port}...");
Console.WriteLine("按 Ctrl+C 退出程序");

// 处理Ctrl+C事件，在退出时清理资源
Console.CancelKeyPress += (_, e) =>
{
	e.Cancel = true; // 防止程序立即退出
	Console.WriteLine("正在关闭服务器...");
	Console.WriteLine("服务器已关闭");
	Environment.Exit(0); // 退出程序
};

// 持续监听消息
while (true)
{
	try
	{
		var result = await udpServer.ReceiveAsync(); // 异步接收消息

		// 日志输出消息内容
		var message = Encoding.UTF8.GetString(result.Buffer);
		Console.WriteLine($"收到来自 {result.RemoteEndPoint} 的消息: {message}");
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