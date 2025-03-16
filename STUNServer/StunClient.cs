using System.Net;

namespace STUNServer;

public class StunClient
{
	public StunClient(Guid clientId, IPEndPoint serverEndPoint, IPEndPoint initialClientEndPoint)
	{
		Id = clientId;
		ServerEndPoint = serverEndPoint;
		InitialClientEndPoint = initialClientEndPoint;
	}
	public Guid Id { get; private set; }
	/// <summary>
	/// 创建这个客户端的服务端终结点
	/// </summary>
	public IPEndPoint ServerEndPoint { get; set; }
	/// <summary>
	/// 客户端的初始公网IP和端口
	/// </summary>
	public IPEndPoint InitialClientEndPoint { get; private set; }
	/// <summary>
	/// 客户端的额外公网IP和端口
	/// </summary>
	public List<IPEndPoint> AdditionalClientEndPoints { get; private set; } = new();
	public DateTime? LastToServerTime { get; private set; }
	public DateTime? LastToClientTime { get; private set; }
	public DateTime LastActivity { get; set; } = DateTime.UtcNow;

}