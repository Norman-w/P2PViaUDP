using System.Net;

namespace STUNServer;

public class StunClient
{
	public StunClient(Guid clientId, IPEndPoint initialServerEndPoint, IPEndPoint initialClientEndPoint)
	{
		Id = clientId;
		InitialServerEndPoint = initialServerEndPoint;
		InitialClientEndPoint = initialClientEndPoint;
	}
	public Guid Id { get; private set; }
	/// <summary>
	/// 创建这个客户端的服务端终结点(最初的终结点)
	/// </summary>
	public IPEndPoint InitialServerEndPoint { get; private set; }
	/// <summary>
	/// 客户端的初始公网IP和端口(最初的终结点)
	/// </summary>
	public IPEndPoint InitialClientEndPoint { get; private set; }
	public DateTime? LastToServerTime { get; set; }
	public DateTime? LastToClientTime { get; set; }
	public DateTime LastActivity { get; set; } = DateTime.UtcNow;

}